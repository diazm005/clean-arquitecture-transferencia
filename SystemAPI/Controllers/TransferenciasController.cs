using Application.Adapters;
using Application.Ports;
using Microsoft.AspNetCore.Mvc;
using SystemAPI.Helpers;
namespace SystemAPI.Controllers;

[ApiController]
[Route("api/transactions")]
public sealed class TransferenciasController(IRealizarTransferencia payment, IConsultarTransferencia paymentQuery) : ControllerBase
{
    /// <summary>
    /// Initiates a payment execution (InitiatePaymentExecutionResponse): 200 completed, 202 pending, 409 needs OTP or reused reference, 422 rejected, 503 unavailable and 400 invalid request.
    /// </summary>
    [HttpPost]
    public async Task<IActionResult> InitiatePaymentExecution(InitiatePaymentExecutionRequest request, CancellationToken ct) =>
        this.ToActionResult(await payment.InitiatePaymentExecutionAsync(Request.ToTrace(), request, ct));

    /// <summary>
    /// Returns the last audited state of a payment execution (RetrievePaymentExecutionResponse) or HTTP 404 when it is not found.
    /// </summary>
    [HttpGet("{transactionReference:guid}")]
    public async Task<IActionResult> RetrievePaymentExecution(Guid transactionReference, CancellationToken ct) =>
        this.ToActionResult(await paymentQuery.RetrievePaymentExecutionAsync(Request.ToTrace(), transactionReference, ct));
}
