using Microsoft.AspNetCore.Authorization;

namespace SystemAPI.Handlers.ArifyAuthorizer;

internal class ArifyScopeRequirement : IAuthorizationRequirement
{
    public string? RequiredScope { get; }

    public ArifyScopeRequirement(string? requiredScope = null)
    {
        RequiredScope = requiredScope;
    }
}
