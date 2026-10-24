using Application.Ports;
using Microsoft.AspNetCore.Mvc;
using SystemAPI.Helpers;
using Microsoft.AspNetCore.Authorization;
using SystemAPI.Handlers;
namespace SystemAPI.Controllers;

[ApiController]
[Authorize(Policy = JwtHandler.ReadPolicy)]
[Route("api/account")]
public sealed class CuentaController(IConsultarCuenta account) : ControllerBase
{
    /// <summary>
    /// Consulta una cuenta propia y su saldo; devuelve 404 si no existe o pertenece a otro cliente.
    /// </summary>
    [HttpGet("{accountNumber}")]
    public async Task<IActionResult> RetrieveAccount(string accountNumber, CancellationToken ct) =>
        this.ToActionResult(await account.RetrieveAccountAsync(Request.ToTrace(), accountNumber, ct));
}
