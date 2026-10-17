using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Options;
using System.Security.Claims;
using System.Text.Encodings.Web;

namespace SystemAPI.Handlers.ArifyAuthorizer;

internal class ArifyBasicAuthenticationHandler : AuthenticationHandler<AuthenticationSchemeOptions>
{
#pragma warning disable CS0618 // Type or member is obsolete
    public ArifyBasicAuthenticationHandler(IOptionsMonitor<AuthenticationSchemeOptions> options, ILoggerFactory logger, UrlEncoder encoder, ISystemClock clock) : base(options, logger, encoder, clock) 
#pragma warning restore CS0618 // Type or member is obsolete
    {}

    protected override Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        // Crear un principal básico para el sistema Arify
        // En un escenario real, aquí validarías tokens o credenciales
        var claims = new[] 
        { 
            new Claim(ClaimTypes.Name, "ArifyUser"),
            new Claim(ClaimTypes.AuthenticationMethod, "Arify")
        };
        
        var identity = new ClaimsIdentity(claims, Scheme.Name);
        var principal = new ClaimsPrincipal(identity);
        var ticket = new AuthenticationTicket(principal, Scheme.Name);

        return Task.FromResult(AuthenticateResult.Success(ticket));
    }
}