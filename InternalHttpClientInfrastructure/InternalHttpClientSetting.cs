using Microsoft.Extensions.DependencyInjection;
using Polly;
using Polly.Extensions.Http;
using System.Net;
using System.Net.Http.Headers;

/* ********************************************************************************************************          
# * Copyright � 2026 Arify Labs - Todos los derechos reservados.
# * 
# * Descripción                  : Configuración del cliente HTTP y reutilización de conexiones.
# *
# * Autor                    : Victor Jhampier Caxi Maquera
# * Correo/Móvil/Teléfono    : victorjhampier@gmail.com | 968991714
# *
# * Fecha de creación         : 01/01/2026
# * 
# **********************************************************************************************************/

namespace InternalHttpClientInfrastructure;

public static class InternalHttpClientSetting
{
    public static void AddInternalHttpClientConnector(this IServiceCollection services)
    {
        services.AddHttpClient("ArifyClient", client =>
        {
            client.Timeout = TimeSpan.FromSeconds(6);
            client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
            // Priorizar HTTP/1.1
            client.DefaultRequestVersion = HttpVersion.Version11;
            client.DefaultVersionPolicy = HttpVersionPolicy.RequestVersionOrLower;
        })
        .ConfigurePrimaryHttpMessageHandler(() => new SocketsHttpHandler
        {
            // Reutilización de conexiones y mantenimiento de conexiones activas
            MaxConnectionsPerServer = 20,
            PooledConnectionIdleTimeout = TimeSpan.FromSeconds(60),
            PooledConnectionLifetime = TimeSpan.FromMinutes(5),
            // Evita cuelgues al abrir conexiones nuevas
            ConnectTimeout = TimeSpan.FromSeconds(2),
            // Configuración básica de rendimiento
            AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate,
            // Si el servidor usa intermediarios o certificados especiales, se configura aquí
            // UseProxy = false,
        })
        // Configurar con Polly el tiempo máximo de espera, la interrupción temporal ante fallos y los reintentos
        // 1. Política de tiempo máximo de espera: primera línea de defensa
        .AddPolicyHandler(Policy.TimeoutAsync<HttpResponseMessage>(TimeSpan.FromSeconds(5)))
        // 2. Interrupción temporal ante fallos repetidos: protege de fallos en cascada
        .AddPolicyHandler(HttpPolicyExtensions
            .HandleTransientHttpError()
            .OrResult(r => r.StatusCode == HttpStatusCode.TooManyRequests)
            .CircuitBreakerAsync(
                handledEventsAllowedBeforeBreaking: 5,  // Abre el circuito después de 5 fallos consecutivos
                durationOfBreak: TimeSpan.FromSeconds(30) // Mantiene el circuito abierto por 30 segundos
            ))
        // 3. Política de reintentos con espera creciente exponencial
        .AddPolicyHandler((request) =>
        {
            // Reintentos seguros solo para métodos idempotentes
            if (request.Method == HttpMethod.Get || request.Method == HttpMethod.Head || request.Method == HttpMethod.Options)
            {
                return HttpPolicyExtensions
                    .HandleTransientHttpError()
                    .OrResult(r => r.StatusCode == HttpStatusCode.TooManyRequests)
                    .WaitAndRetryAsync(
                        retryCount: 3,
                        sleepDurationProvider: retry => TimeSpan.FromMilliseconds(Math.Pow(2, retry) * 100), // Backoff exponencial: 200ms, 400ms, 800ms
                        onRetry: (outcome, timespan, retryAttempt, context) =>
                        {
                            // Registro opcional para supervisión
                            // logger.LogWarning($"Retry {retryAttempt} after {timespan.TotalMilliseconds}ms");
                        });
            }

            // Sin reintentos para POST/PUT/PATCH por defecto (evita duplicidad) porque no son idempotentes
            return Policy.NoOpAsync<HttpResponseMessage>();
        });
    }
}

//public static void AddInternalHttpClientConnector(this IServiceCollection services)
//    {
//        services.AddHttpClient("ArifyClient", client =>
//        {
//            client.Timeout = TimeSpan.FromSeconds(7);
//            client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
//        })
//        .ConfigurePrimaryHttpMessageHandler(() => new SocketsHttpHandler
//        {
//            // === Equivalentes a tu pool ===
//            MaxConnectionsPerServer = 20,   // max_connections
//            PooledConnectionIdleTimeout = TimeSpan.FromSeconds(60), // keepalive_expiry (idle)
//            PooledConnectionLifetime = TimeSpan.FromMinutes(5), // rotación para DNS/TLS
//            // Keep-alive HTTP/2 y HTTP/1.1 se gestiona automáticamente por el handler
//            AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate,
//        });

//        services.AddHttpClient("ArifyClient")
//        .ConfigureHttpClient(client =>
//        {
//            client.DefaultRequestVersion = HttpVersion.Version20;
//            client.DefaultVersionPolicy = HttpVersionPolicy.RequestVersionOrHigher;
//        });

//        services.AddHttpClient("ArifyClient")
//        .AddPolicyHandler(HttpPolicyExtensions
//        .HandleTransientHttpError()
//        .WaitAndRetryAsync(2, _ => TimeSpan.FromMilliseconds(200)));
//    }