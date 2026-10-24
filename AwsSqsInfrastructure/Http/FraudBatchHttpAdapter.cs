using System.Net.Http.Json;
using System.Text.Json;
using Domain.Entities.Internals;
using Domain.Interfaces;
using Domain.Services;
namespace AntifraudeInfrastructure.Http;

/// <summary>
/// Consulta el riesgo a Fraude.Batch (transferencias-api-antifraude) por HTTP. Sin SQL, el historial de débitos
/// y destinos se aproxima en memoria con las evaluaciones no bloqueadas de este proceso; se pierde al reiniciar.
/// </summary>
public sealed class FraudBatchHttpAdapter(IHttpClientFactory httpFactory, TimeProvider clock) : IAntifraudeService
{
    public const string ClientName = "FraudBatch";
    private readonly object _sync = new();
    private readonly List<(string CuentaOrigen, string Destino, Guid Id, DateTimeOffset Fecha)> _historial = [];

    public async Task<NivelRiesgo> EvaluarAsync(Transferencia transferencia, CancellationToken ct = default)
    {
        var now = clock.GetUtcNow();
        var destino = $"{transferencia.BancoDestino.ToUpperInvariant()}|{transferencia.CuentaDestino}";
        int previos;
        bool conocido;
        lock (_sync)
        {
            var propios = _historial.Where(x => x.CuentaOrigen == transferencia.CuentaOrigen && x.Id != transferencia.Id).ToList();
            previos = propios.Where(x => x.Fecha >= now.AddMinutes(-15)).Select(x => x.Id).Distinct().Count();
            conocido = propios.Any(x => x.Destino == destino);
        }

        var http = httpFactory.CreateClient(ClientName);
        using var response = await http.PostAsJsonAsync("api/fraud/evaluations", new
        {
            transactionReference = transferencia.Id,
            amount = transferencia.Monto,
            previousDebitsIn15Minutes = previos,
            knownDestination = conocido
        }, ct);
        response.EnsureSuccessStatusCode();
        Respuesta? result;
        try { result = await response.Content.ReadFromJsonAsync<Respuesta>(ct); }
        catch (Exception ex) when (ex is JsonException or NotSupportedException)
        { throw new HttpRequestException("Fraude.Batch devolvió una respuesta ilegible.", ex); }
        // Una respuesta que no corresponde a la solicitud nunca se interpreta como riesgo bajo.
        if (result is null || result.TransactionReference != transferencia.Id ||
            result.RuleVersion != PoliticaFraude.Version || string.IsNullOrWhiteSpace(result.Reason))
            throw new HttpRequestException("La respuesta de Fraude.Batch no corresponde a la solicitud o a la versión esperada.");
        var nivel = result.RiskLevel?.ToUpperInvariant() switch
        {
            "BAJO" => NivelRiesgo.Bajo,
            "MEDIO" => NivelRiesgo.Medio,
            "ALTO" => NivelRiesgo.Alto,
            _ => throw new HttpRequestException("Fraude.Batch devolvió un nivel de riesgo desconocido.")
        };

        if (nivel != NivelRiesgo.Alto)
            lock (_sync) _historial.Add((transferencia.CuentaOrigen, destino, transferencia.Id, now));
        return nivel;
    }

    private sealed record Respuesta(Guid TransactionReference, string? RiskLevel, string? Reason, string? RuleVersion);
}
