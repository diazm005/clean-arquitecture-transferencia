using Domain.Interfaces;
using Microsoft.Extensions.DependencyInjection;
namespace AntifraudeInfrastructure.Http;

public static class AntifraudeSetting
{
    /// <summary>Registra el cliente HTTP de Fraude.Batch como implementación de IAntifraudeService.</summary>
    public static IServiceCollection AddFraudBatchAntifraude(this IServiceCollection services,
        string? baseUrl = "http://127.0.0.1:5310/")
    {
        if (!Uri.TryCreate(baseUrl, UriKind.Absolute, out var uri) || uri.Scheme is not ("http" or "https") ||
            uri.Query.Length != 0 || uri.Fragment.Length != 0 || uri.UserInfo.Length != 0)
            throw new InvalidOperationException("Antifraude:BaseUrl debe ser una dirección HTTP/HTTPS absoluta de Fraude.Batch.");
        services.AddHttpClient(FraudBatchHttpAdapter.ClientName, client =>
        {
            client.BaseAddress = new Uri(uri.AbsoluteUri.TrimEnd('/') + "/");
            client.Timeout = TimeSpan.FromSeconds(2);
            client.MaxResponseContentBufferSize = 16_384;
        }).ConfigurePrimaryHttpMessageHandler(() => new SocketsHttpHandler { AllowAutoRedirect = false });
        // Singleton: conserva el historial en memoria entre solicitudes.
        services.AddSingleton<IAntifraudeService, FraudBatchHttpAdapter>();
        return services;
    }
}
