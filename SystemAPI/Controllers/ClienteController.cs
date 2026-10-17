using Application.Ports;
using Microsoft.AspNetCore.Mvc;
namespace SystemAPI.Controllers;

[ApiController]
[Route("api/client")]
public sealed class ClienteController(IConsultarCliente consultar) : ControllerBase
{
    [HttpGet("{dni}")]
    /// <summary>
    /// Devuelve los datos del cliente o una respuesta HTTP 404 cuando no existe.
    /// </summary>
    public async Task<IActionResult> ConsultarCliente(string dni, CancellationToken ct)
    {
        var cliente = await consultar.ConsultarClienteAsync(dni, ct);
        return cliente is null ? NotFound() : Ok(cliente);
    }
}
