using Application.Ports;
using Microsoft.AspNetCore.Mvc;
using SystemAPI.Helpers;
namespace SystemAPI.Controllers;

[ApiController]
[Route("api/client")]
public sealed class ClienteController(IConsultarCliente customer) : ControllerBase
{
    /// <summary>
    /// Returns the customer data (RetrieveCustomerResponse) or HTTP 404 when it does not exist.
    /// </summary>
    [HttpGet("{nationalId}")]
    public async Task<IActionResult> RetrieveCustomer(string nationalId, CancellationToken ct) =>
        this.ToActionResult(await customer.RetrieveCustomerAsync(Request.ToTrace(), nationalId, ct));
}
