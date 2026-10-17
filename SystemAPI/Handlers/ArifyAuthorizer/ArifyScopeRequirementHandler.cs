using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc.Controllers;
using Microsoft.Extensions.Options;
using System.Reflection;

namespace SystemAPI.Handlers.ArifyAuthorizer;

internal class ArifyScopeRequirementHandler : AuthorizationHandler<ArifyScopeRequirement>
{
    private readonly IHttpContextAccessor _httpContextAccessor;
    private readonly ILogger<ArifyScopeRequirementHandler> _logger;
    private readonly ArifyScopeOptions _scopeOptions;
    
    // Mapeo de políticas a scopes reales (fallback si no hay configuración)
    private static readonly Dictionary<string, string> DefaultPolicyToScopeMap = new()
    {
        { "ReadExample", "api/r:ex" },
        { "WriteExample", "api/w:ex" }
    };

    public ArifyScopeRequirementHandler(
        IHttpContextAccessor httpContextAccessor, 
        ILogger<ArifyScopeRequirementHandler> logger,
        IOptions<ArifyScopeOptions> scopeOptions)
    {
        _httpContextAccessor = httpContextAccessor;
        _logger = logger;
        _scopeOptions = scopeOptions.Value;
    }

    protected override Task HandleRequirementAsync(AuthorizationHandlerContext context, ArifyScopeRequirement requirement)
    {
        var httpContext = _httpContextAccessor.HttpContext;
        if (httpContext == null)
        {
            _logger.LogWarning("HttpContext is null during authorization");
            return Task.CompletedTask;
        }

        // Obtener el nombre de la política desde el atributo ArifyAuthorize
        var policyName = GetRequiredScopeFromContext(context);
        
        if (string.IsNullOrEmpty(policyName))
        {
            _logger.LogWarning("No required policy specified for authorization");
            return Task.CompletedTask;
        }

        // Mapear la política al scope real
        var policyMappings = _scopeOptions.PolicyMappings?.Any() == true 
            ? _scopeOptions.PolicyMappings 
            : DefaultPolicyToScopeMap;
            
        if (!policyMappings.TryGetValue(policyName, out var requiredScope))
        {
            _logger.LogWarning("Policy '{PolicyName}' not found in scope mapping", policyName);
            return Task.CompletedTask;
        }

        var headers = httpContext.Request.Headers;
        if (headers.TryGetValue("x-scope", out var scopeHeader))
        {
            var headerValue = scopeHeader.ToString();
            if (!string.IsNullOrWhiteSpace(headerValue))
            {
                var scopes = headerValue.Split(' ', StringSplitOptions.RemoveEmptyEntries);
                
                if (scopes.Contains(requiredScope, StringComparer.OrdinalIgnoreCase))
                {
                    _logger.LogDebug("Authorization successful. Policy: {PolicyName}, Scope: {RequiredScope}", policyName, requiredScope);
                    context.Succeed(requirement);
                    return Task.CompletedTask;
                }
            }
        }

        _logger.LogWarning("Authorization failed. Policy: {PolicyName}, Required scope: {RequiredScope}, Header: {ScopeHeader}", 
            policyName, requiredScope, headers.ContainsKey("x-scope") ? headers["x-scope"].ToString() : "missing");
        
        return Task.CompletedTask;
    }

    private string? GetRequiredScopeFromContext(AuthorizationHandlerContext context)
    {
        // Intentar obtener desde el resource primero
        if (context.Resource is string resourceScope)
        {
            return resourceScope;
        }

        // Si no está en resource, buscar en el endpoint
        if (_httpContextAccessor.HttpContext?.GetEndpoint() is { } endpoint)
        {
            var arifyAttribute = endpoint.Metadata.GetMetadata<ArifyAuthorizeAttribute>();
            if (arifyAttribute != null)
            {
                return arifyAttribute.RequiredScope; // Usar la propiedad correcta
            }
        }

        return null;
    }
}