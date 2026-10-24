using Domain.Services;

namespace FraudBatch.Http;

/// <summary>Expone el evaluador al servicio de transferencias. En este modo el batchero no accede a SQL.</summary>
internal static class BatchHttpHost
{
    public static async Task RunAsync(string[] args)
    {
        var builder = WebApplication.CreateBuilder(args);
        if (!builder.Environment.IsDevelopment())
            throw new InvalidOperationException("El batchero HTTP del taller requiere Development hasta implementar autenticación entre servicios.");
        if (string.IsNullOrWhiteSpace(builder.Configuration["urls"]))
            builder.WebHost.UseUrls("http://127.0.0.1:5210");
        builder.Services.AddProblemDetails();
        var app = builder.Build();
        app.UseExceptionHandler();
        app.MapGet("/health", () => Results.Ok(new { status = "Healthy", service = "FraudBatch", ruleVersion = PoliticaFraude.Version }));
        app.MapPost("/api/fraud/evaluations", (SolicitudEvaluacion request) =>
        {
            var errors = new Dictionary<string, string[]>();
            if (request.TransactionReference is null || request.TransactionReference == Guid.Empty)
                errors["transactionReference"] = ["Indica la referencia de la transferencia."];
            if (request.Amount is not { } amount || amount <= 0 || amount > 9_999_999_999_999_999.99m || decimal.Round(amount, 2) != amount)
                errors["amount"] = ["Indica un monto positivo compatible con decimal(18,2)."];
            if (request.PreviousDebitsIn15Minutes is null or < 0)
                errors["previousDebitsIn15Minutes"] = ["Indica la cantidad de débitos previos, mayor o igual que cero."];
            if (request.KnownDestination is null)
                errors["knownDestination"] = ["Indica si el destino tiene historial previo."];
            if (errors.Count != 0) return Results.ValidationProblem(errors);

            var decision = PoliticaFraude.Evaluar(new(request.Amount!.Value,
                request.PreviousDebitsIn15Minutes!.Value, request.KnownDestination!.Value));
            return Results.Ok(new RespuestaEvaluacion(request.TransactionReference!.Value,
                decision.Nivel.ToString(), decision.Motivo, decision.VersionRegla));
        });
        await app.RunAsync();
    }
}

// Los campos anulables permiten distinguir un cero/false válido de un dato omitido.
internal sealed record SolicitudEvaluacion(Guid? TransactionReference, decimal? Amount,
    int? PreviousDebitsIn15Minutes, bool? KnownDestination);
internal sealed record RespuestaEvaluacion(Guid TransactionReference, string RiskLevel, string Reason, string RuleVersion);
