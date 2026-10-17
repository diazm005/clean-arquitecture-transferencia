using Microsoft.AspNetCore.Authorization;

namespace SystemAPI.Handlers.ArifyAuthorizer;

/// <summary>
/// Atributo de autorización Arify que valida scopes mediante header x-scope
/// </summary>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Method, AllowMultiple = false)]
public class ArifyAuthorizeAttribute : AuthorizeAttribute
{
    public string RequiredScope { get; }

    public ArifyAuthorizeAttribute(string requiredScope)
    {
        RequiredScope = requiredScope;
        Policy = "ArifyPolicy";
    }
}