using Application.Adapters;
using Application.Ports;
using Domain.Entities.Internals;
using Domain.Interfaces;
using Microsoft.AspNetCore.Mvc;
namespace SystemAPI.Controllers;

[ApiController]
[Route("api/transactions")]
public sealed class TransferenciasController(IRealizarTransferencia realizar, IAuditoriaRepository auditoria) : ControllerBase
{
    [HttpPost]
    /// <summary>
    /// Ejecuta el caso de uso y traduce su resultado a un estado HTTP, incluyendo rechazos, confirmaciones pendientes y errores de entrada.
    /// </summary>
    public async Task<IActionResult> Realizar(SolicitudTransferencia solicitud, CancellationToken ct)
    {
        try
        {
            var resultado = await realizar.EjecutarAsync(solicitud, ct);
            return StatusCode(resultado.Estado switch
            {
                EstadoTransferencia.Completada => 200,
                EstadoTransferencia.Pendiente => 202,
                EstadoTransferencia.NoDisponible => 503,
                EstadoTransferencia.RequiereConfirmacion => 409,
                _ => 422
            }, resultado);
        }
        catch (ArgumentException ex) { return Problem(statusCode: 400, detail: ex.Message); }
        catch (InvalidOperationException ex) { return Problem(statusCode: 409, detail: ex.Message); }
    }

    [HttpGet("{id:guid}")]
    /// <summary>
    /// Obtiene el último resultado auditado por ID de operación; devuelve HTTP 404 si no se encuentra.
    /// </summary>
    public async Task<IActionResult> Consultar(Guid id, CancellationToken ct)
    {
        var resultado = await auditoria.ObtenerAsync(id, ct);
        return resultado is null ? NotFound() : Ok(resultado);
    }
}
