using Application.Ports;
using Microsoft.AspNetCore.Mvc;
using SystemAPI.Helpers;
namespace SystemAPI.Controllers;

[ApiController]
[Route("api/account")]
public sealed class CuentaController(IConsultarCuenta account) : ControllerBase
{
    /// <summary>
    /// Returns the account and its available balance (RetrieveAccountResponse) or HTTP 404 when it does not exist.
    /// </summary>
    [HttpGet("{accountNumber}")]
    public async Task<IActionResult> RetrieveAccount(string accountNumber, CancellationToken ct) =>
        this.ToActionResult(await account.RetrieveAccountAsync(Request.ToTrace(), accountNumber, ct));
}
