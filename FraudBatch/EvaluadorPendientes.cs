using System.Data;
using Domain.Entities.Internals;
using Domain.Services;
using Microsoft.EntityFrameworkCore;
using SqlServerInfrastructure.Persistence;

namespace FraudBatch;

internal sealed record ResultadoProcesamiento(long EvaluacionId, string Estado, NivelRiesgo? Nivel);

/// <summary>
/// Consume una fila pendiente por transacción. La política vive en Domain y la persistencia
/// en Infrastructure; este proceso compone ambos y no ejecuta movimientos financieros.
/// </summary>
internal sealed class EvaluadorPendientes(TransferenciasDbContext db, TimeProvider? clock = null)
{
    private readonly TimeProvider timeProvider = clock ?? TimeProvider.System;

    public async Task<ResultadoProcesamiento?> ProcesarUnaAsync(CancellationToken ct)
    {
        // RepeatableRead permite READPAST incluso cuando SQL Server tiene RCSI activado.
        // UPDLOCK conserva la fila hasta confirmar; otro worker la omite con READPAST.
        await using var transaction = await db.Database.BeginTransactionAsync(IsolationLevel.RepeatableRead, ct);
        var rows = await db.EvaluacionesFraude.FromSqlRaw("""
            SELECT TOP (1) *
            FROM [transferencias].[EvaluacionFraude] WITH (UPDLOCK, READPAST, ROWLOCK)
            WHERE [EstadoEvaluacion] = 'Pendiente'
            ORDER BY [FechaSolicitud], [EvaluacionFraudeId]
            """).ToListAsync(ct);
        var evaluation = rows.SingleOrDefault();
        if (evaluation is null)
        {
            await transaction.CommitAsync(ct);
            return null;
        }

        evaluation.EstadoEvaluacion = "Procesando";
        await db.SaveChangesAsync(ct);
        var transfer = await db.Transferencias.AsNoTracking()
            .SingleAsync(x => x.TransferenciaId == evaluation.TransferenciaId, ct);

        // La versión solicitada se verifica para no aplicar silenciosamente una política diferente.
        if (!string.Equals(evaluation.VersionRegla, PoliticaFraude.Version, StringComparison.Ordinal))
        {
            evaluation.EstadoEvaluacion = "Error";
            evaluation.NivelRiesgoId = null;
            evaluation.Motivo = $"La versión solicitada no está soportada por este batch; versión disponible: {PoliticaFraude.Version}.";
            evaluation.Proveedor = "FraudBatch";
            evaluation.FechaRespuesta = MaxDate(timeProvider.GetUtcNow(), evaluation.FechaSolicitud);
            await db.SaveChangesAsync(ct);
            await transaction.CommitAsync(ct);
            return new(evaluation.EvaluacionFraudeId, "Error", null);
        }

        // La ventana se calcula al evaluar, no con la fecha original de la transferencia:
        // un reintento OTP o una cola demorada debe considerar la actividad reciente.
        // Se cuenta una transferencia por DEBITO_MONTO, nunca su comisión ni solicitudes
        // rechazadas. Se excluye la operación actual y todo hecho posterior a este instante.
        var reference = timeProvider.GetUtcNow();
        var history = from movement in db.MovimientosCuenta.AsNoTracking()
                      join previous in db.Transferencias.AsNoTracking()
                          on movement.TransferenciaId equals previous.TransferenciaId
                      where movement.CuentaId == transfer.CuentaOrigenId
                            && previous.CuentaOrigenId == transfer.CuentaOrigenId
                            && movement.TipoMovimiento == "DEBITO_MONTO"
                            && movement.Naturaleza == "D"
                            && movement.TransferenciaId != transfer.TransferenciaId
                            && movement.FechaMovimiento <= reference
                            && previous.FechaSolicitud <= reference
                      select new
                      {
                          movement.TransferenciaId,
                          movement.FechaMovimiento,
                          previous.BancoDestinoId,
                          previous.NumeroCuentaDestino
                      };
        var windowStart = reference.AddMinutes(-15);
        var previousDebits = await history.Where(x => x.FechaMovimiento >= windowStart)
            .Select(x => x.TransferenciaId).Distinct().CountAsync(ct);
        var knownDestination = await history.AnyAsync(x => x.BancoDestinoId == transfer.BancoDestinoId
            && x.NumeroCuentaDestino == transfer.NumeroCuentaDestino, ct);

        var decision = PoliticaFraude.Evaluar(new(transfer.Monto, previousDebits, knownDestination));
        evaluation.EstadoEvaluacion = "Completada";
        evaluation.NivelRiesgoId = checked((byte)decision.Nivel);
        evaluation.Motivo = decision.Motivo;
        evaluation.VersionRegla = decision.VersionRegla;
        evaluation.Proveedor = "FraudBatch";
        evaluation.FechaRespuesta = MaxDate(timeProvider.GetUtcNow(), evaluation.FechaSolicitud);
        await db.SaveChangesAsync(ct);
        // Procesando y Completada se confirman juntos. Si el proceso cae antes, vuelve
        // a Pendiente al revertirse toda la transacción; no queda una reserva huérfana.
        await transaction.CommitAsync(ct);
        return new(evaluation.EvaluacionFraudeId, "Completada", decision.Nivel);
    }

    private static DateTimeOffset MaxDate(DateTimeOffset now, DateTimeOffset requested) =>
        (now >= requested ? now : requested).ToUniversalTime();
}
