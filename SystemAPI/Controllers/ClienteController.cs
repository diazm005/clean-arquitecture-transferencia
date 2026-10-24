using Application.Ports;
using Microsoft.AspNetCore.Mvc;
using SystemAPI.Helpers;
using Microsoft.AspNetCore.Authorization;
using SystemAPI.Handlers;
namespace SystemAPI.Controllers;

[ApiController]
[Authorize(Policy = JwtHandler.ReadPolicy)]
[Route("api/client")]
public sealed class ClienteController(IConsultarCliente customer) : ControllerBase
{
    /// <summary>
    /// Consulta los datos del cliente autenticado; devuelve 404 si no existe o pertenece a otra identidad.
    /// </summary>
    [HttpGet("{nationalId}")]
    public async Task<IActionResult> RetrieveCustomer(string nationalId, CancellationToken ct) =>
        this.ToActionResult(await customer.RetrieveCustomerAsync(Request.ToTrace(), nationalId, ct));
}
