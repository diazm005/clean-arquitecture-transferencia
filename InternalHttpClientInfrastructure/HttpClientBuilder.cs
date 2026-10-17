using Domain.Containers.MemoryEvent;
using Domain.Entities.Internals;
using InternalHttpClientInfrastructure.Collections;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;

/* ********************************************************************************************************          
# * Copyright � 2025 Arify Labs - Todos los derechos reservados.
# * 
# * Descripción                  : Constructor de solicitudes HTTP.
# *
# * Autor                    : Victor Jhampier Caxi Maquera
# * Correo/Móvil/Teléfono    : victorjhampier@gmail.com | 968991714
# *
# * Fecha de creación         : 01/01/2026
# * 
# **********************************************************************************************************/

namespace InternalHttpClientInfrastructure;

public sealed class HttpClientBuilder
{
    private readonly IHttpClientFactory _factory;
    private readonly ILogger _logger;
    private readonly JsonSerializerOptions _jsonOptions;

    private string _clientName = "ArifyClient";
    private string? _baseUrl;
    private string? _endpoint;
    private readonly Dictionary<string, string> _headers = new();
    private readonly Dictionary<string, string> _query = new();
    private TimeSpan? _timeout;
    private MicroserviceCallMemoryQueue? _queue;
    private string? _operationName;
    private string? _keyword;
    private DateTime _startDate = DateTime.Now;

    public HttpClientBuilder(IHttpClientFactory factory, ILogger logger)
    {
        _factory = factory ?? throw new ArgumentNullException(nameof(factory));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _jsonOptions = new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            WriteIndented = false
        };        
    }

    public HttpClientBuilder WithMemoryQueue(MicroserviceCallMemoryQueue queue, string operationName, string? keyword = null)
    {
        ArgumentNullException.ThrowIfNull(queue);
        ArgumentException.ThrowIfNullOrWhiteSpace(operationName);
        
        _queue = queue;
        _operationName = operationName;
        _keyword = keyword;
        return this;
    }        

    public HttpClientBuilder WithClient(string name)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        _clientName = name;
        return this;
    }

    public HttpClientBuilder WithBaseUrl(string baseUrl)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(baseUrl);
        _baseUrl = baseUrl.TrimEnd('/');
        return this;
    }

    public HttpClientBuilder WithEndpoint(string endpoint)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(endpoint);
        _endpoint = endpoint.TrimStart('/');
        return this;
    }

    public HttpClientBuilder WithHeader(string key, string value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);
        ArgumentNullException.ThrowIfNull(value);
        _headers[key] = value;
        return this;
    }

    public HttpClientBuilder WithBearerToken(string token)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(token);
        return WithHeader("Authorization", $"Bearer {token}");
    }

    public HttpClientBuilder WithAuthorization(string scheme, string token)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(scheme);
        ArgumentException.ThrowIfNullOrWhiteSpace(token);
        return WithHeader("Authorization", $"{scheme} {token}");
    }

    public HttpClientBuilder WithQuery(string key, string value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);
        ArgumentNullException.ThrowIfNull(value);
        _query[key] = value;
        return this;
    }

    public HttpClientBuilder WithTimeout(TimeSpan timeout)
    {
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(timeout, TimeSpan.Zero);
        _timeout = timeout;
        return this;
    }

    private Uri BuildUri()
    {
        if (string.IsNullOrWhiteSpace(_baseUrl))
            throw new InvalidOperationException("Base URL must be set before making requests");

        var path = string.IsNullOrWhiteSpace(_endpoint) 
            ? _baseUrl 
            : $"{_baseUrl}/{_endpoint}";

        if (_query.Count == 0)
            return new Uri(path);

        var uriBuilder = new UriBuilder(path);
        var queryString = string.Join("&", _query.Select(kv =>
            $"{Uri.EscapeDataString(kv.Key)}={Uri.EscapeDataString(kv.Value)}"));
        uriBuilder.Query = queryString;

        return uriBuilder.Uri;
    }

    private HttpRequestMessage BuildRequest(HttpMethod method, object? body, out string? serializedBody)
    {
        var uri = BuildUri();
        var request = new HttpRequestMessage(method, uri);

        // Agregar las cabeceras de la solicitud.
        foreach (var header in _headers)
        {
            if (!request.Headers.TryAddWithoutValidation(header.Key, header.Value))
            {
                _logger.LogWarning("Failed to add header {HeaderKey} with value {HeaderValue}", header.Key, header.Value);
            }
        }

        // Agregar el contenido si se proporcionó un cuerpo para la solicitud.
        serializedBody = null;
        if (body is not null)
        {
            serializedBody = JsonSerializer.Serialize(body, _jsonOptions);
            request.Content = new StringContent(serializedBody, Encoding.UTF8, "application/json");
        }

        return request;
    }

    public async Task<HttpResponseResult<T>> GetAsync<T>(CancellationToken cancellationToken = default)
    {
        return await ExecuteRequestAsync<T>(HttpMethod.Get, null, cancellationToken);
    }

    public async Task<HttpResponseResult<T>> PostAsync<T>(object? body = null, CancellationToken cancellationToken = default)
    {
        return await ExecuteRequestAsync<T>(HttpMethod.Post, body, cancellationToken);
    }

    public async Task<HttpResponseResult<T>> PutAsync<T>(object? body = null, CancellationToken cancellationToken = default)
    {
        return await ExecuteRequestAsync<T>(HttpMethod.Put, body, cancellationToken);
    }

    public async Task<HttpResponseResult<T>> DeleteAsync<T>(CancellationToken cancellationToken = default)
    {
        return await ExecuteRequestAsync<T>(HttpMethod.Delete, null, cancellationToken);
    }

    private async Task<HttpResponseResult<T>> ExecuteRequestAsync<T>(HttpMethod method, object? body, CancellationToken cancellationToken)
    {
        var client = _factory.CreateClient(_clientName);
        
        if (_timeout.HasValue)
            client.Timeout = _timeout.Value;

        using var request = BuildRequest(method, body, out var serializedBody);
        
        // Inicializar el registro de seguimiento si se configuró la cola en memoria
        MicroserviceCallTraceEntity? trace = null;
        
        if (_queue is not null && !string.IsNullOrEmpty(_operationName))
        {
            trace = CreateTraceEntity(request, serializedBody, _startDate);
        }
        
        try
        {
            _logger.LogDebug("Executing {Method} request to {Uri}", method, request.RequestUri);
            
            using var response = await client.SendAsync(request, cancellationToken);
            var result = await BuildResponseAsync<T>(response, trace);
            
            return result;
        }
        catch (HttpRequestException ex)
        {
            _logger.LogError("{Message} < HTTP request failed for {Method} {Uri}", ex.Message, method, request.RequestUri);
            
            // Registrar el seguimiento con error HTTP
            if (trace is not null)
            {
                await CompleteAndPushTraceAsync(trace, 0, default(T), $"HttpRequestException: {ex.Message}", DateTime.Now);
            }
            
            // No propagar excepción, retornar resultado con error
            return new HttpResponseResult<T>(
                0,
                false,
                default,
                request.RequestUri?.ToString() ?? string.Empty
            );
        }
        catch (TaskCanceledException ex) when (ex.InnerException is TimeoutException)
        {
            _logger.LogError("{Message} < Request timeout for {Method} {Uri}", ex.Message, method, request.RequestUri);
            
            // Registrar el seguimiento al agotarse el tiempo de espera
            if (trace is not null)
            {
                await CompleteAndPushTraceAsync(trace, 0, default(T), $"Timeout: {ex.Message}", DateTime.Now);
            }
            
            // No propagar excepción, retornar resultado con error
            return new HttpResponseResult<T>(
                0,
                false,
                default,
                request.RequestUri?.ToString() ?? string.Empty
            );
        }
        catch (TaskCanceledException ex)
        {
            _logger.LogError("{Message} < Request cancelled for {Method} {Uri}", ex.Message, method, request.RequestUri);
            
            // Registrar el seguimiento con cancelación
            if (trace is not null)
            {
                await CompleteAndPushTraceAsync(trace, 0, default(T), $"Cancelled: {ex.Message}", DateTime.Now);
            }
            
            // No propagar excepción, retornar resultado con error
            return new HttpResponseResult<T>(
                0,
                false,
                default,
                request.RequestUri?.ToString() ?? string.Empty
            );
        }
        catch (Exception ex)
        {
            _logger.LogError(" {Message} < Unexpected error during {Method} request to {Uri}", ex.Message, method, request.RequestUri);
            
            // Registrar el seguimiento con error inesperado
            if (trace is not null)
            {
                await CompleteAndPushTraceAsync(trace, 0, default(T), $"Exception: {ex.GetType().Name} - {ex.Message}", DateTime.Now);
            }
            
            // No propagar excepción, retornar resultado con error
            return new HttpResponseResult<T>(
                0,
                false,
                default,
                request.RequestUri?.ToString() ?? string.Empty
            );
        }
    }

    private async Task<HttpResponseResult<T>> BuildResponseAsync<T>(HttpResponseMessage response, MicroserviceCallTraceEntity? trace)
    {
        T? content = default;
        var requestUri = response.RequestMessage?.RequestUri?.ToString() ?? string.Empty;
        bool isSuccess = false;
        string? errorMessage = null;
        string? rawResponseBody = null;

        try
        {
            // Leer el cuerpo original de la respuesta para el seguimiento de la solicitud
            rawResponseBody = await response.Content.ReadAsStringAsync();
            
            if ((int)response.StatusCode == 200)
            {
                if (!string.IsNullOrEmpty(rawResponseBody))
                {
                    content = JsonSerializer.Deserialize<T>(rawResponseBody, _jsonOptions);
                    isSuccess = content is not null;
                    
                    if (!isSuccess)
                    {
                        errorMessage = "Deserialization returned null";
                    }
                }
                else
                {
                    errorMessage = "Response body is empty";
                }
            }            
            else
            {
                var headers = response.Headers
                    .Concat(response.Content.Headers)
                    .ToDictionary(h => h.Key, h => string.Join(",", h.Value));
                _logger.LogWarning("Response >> {StatusCode} {Uri}\nHeaders={Headers}\nContent={Content}", 
                    (int)response.StatusCode, requestUri, headers, rawResponseBody);
                
                errorMessage = $"HTTP {(int)response.StatusCode}: {response.ReasonPhrase}";
            }   
        }
        catch (JsonException ex)
        {
            errorMessage = $"JsonException: {ex.Message}";
            _logger.LogError("{Message} >> Failed to deserialize response >> {StatusCode} {Uri} >> Content may not match expected type {Type}\n{Content}", 
                ex.Message, (int)response.StatusCode, requestUri, typeof(T).Name, rawResponseBody);
        }
        catch (Exception ex)
        {
            errorMessage = $"Exception: {ex.GetType().Name} - {ex.Message}";
            _logger.LogError("{Message} >> Unexpected error while reading response >> {StatusCode} {Uri}\n{Content}", 
                ex.Message, (int)response.StatusCode, requestUri, rawResponseBody);
        }
        
        // Registrar el seguimiento si está configurado
        if (trace is not null)
        {
            await CompleteAndPushTraceAsync(trace, (int)response.StatusCode, rawResponseBody, errorMessage, DateTime.Now);
        }

        return new HttpResponseResult<T>(
            (int)response.StatusCode,
            isSuccess,
            content,
            requestUri
        );
    }

    private MicroserviceCallTraceEntity CreateTraceEntity(HttpRequestMessage request, string? serializedBody, DateTime requestTime)
    {        
        return new MicroserviceCallTraceEntity
        {
            Identity = Guid.NewGuid().ToString(),
            TraceId = _headers.TryGetValue("message-identification", out var traceId) ? traceId : "unknown",
            ChannelId = _headers.TryGetValue("channel-identification", out var channelId) ? channelId : "unknown",
            DeviceId = _headers.TryGetValue("device-identification", out var deviceId) ? deviceId : "unknown",
            OperationName = _operationName!,
            Keyword = _keyword,
            RequestUrl = request.RequestUri?.ToString() ?? string.Empty,
            RequestPayload = serializedBody,
            RequestDatetime = requestTime,
            ResponseStatusCode = 0
        };
    }

    private async Task CompleteAndPushTraceAsync<T>(MicroserviceCallTraceEntity trace, int statusCode, T? responseBody, string? errorMessage, DateTime responseTime)
    {
        trace.ResponseStatusCode = statusCode;
        trace.ResponseDatetime = responseTime;

        // Si hay un mensaje de error, agregarlo como prefijo
        if (errorMessage is not null && responseBody is string rawBody)
        {
            // Combinar el mensaje de error con el cuerpo original de la respuesta
            trace.ResponsePayload = $"[{errorMessage}] {rawBody}";
        }
        else if (errorMessage is not null)
        {
            // Solo el mensaje de error (tiempo de espera agotado, cancelación, etc.)
            trace.ResponsePayload = errorMessage;
        }
        else if (responseBody is string rawBodySuccess)
        {
            // Cuerpo original de la respuesta (ya serializado)
            trace.ResponsePayload = rawBodySuccess;
        }
        else if (responseBody is not null)
        {
            // Alternativa: serializar si el valor no es una cadena de texto
            trace.ResponsePayload = JsonSerializer.Serialize(responseBody, _jsonOptions);
        }

        // Envío no bloqueante a la cola
        if (!_queue!.TryPush(trace))
        {
            _logger.LogWarning("Memory Queue fully. URL: TraceId: {TraceId}, {RequestUrl}, Request: {RequestPayload}, Response:{ResponsePayload}", trace.TraceId, trace.RequestUrl, trace.RequestPayload, trace.ResponsePayload);
        }
    }
}
