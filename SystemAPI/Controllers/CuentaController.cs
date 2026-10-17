using Application.Ports;
using Microsoft.AspNetCore.Mvc;
namespace SystemAPI.Controllers;

[ApiController]
[Route("api/account")]
public sealed class CuentaController(IConsultarCuenta consultar) : ControllerBase
{
    [HttpGet("{numero}")]
    /// <summary>
    /// Devuelve los datos de la cuenta y su saldo actual, o HTTP 404 cuando no existe.
    /// </summary>
    public async Task<IActionResult> ConsultarCuenta(string numero, CancellationToken ct)
    {
        var cuenta = await consultar.ConsultarCuentaAsync(numero, ct);
        return cuenta is null ? NotFound() : Ok(cuenta);
    }
}
