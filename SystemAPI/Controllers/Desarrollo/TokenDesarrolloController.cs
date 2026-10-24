using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
namespace SystemAPI.Controllers.Desarrollo;

/// <summary>Request del emisor local: DNI del cliente y si el token solo puede leer.</summary>
public sealed record TokenDesarrolloRequest(string NationalId, bool ReadOnly = false);

/// <summary>
/// Emisor local para Postman y Swagger, equivalente a scripts/nuevo-token.ps1. No es un login:
/// la API solo arranca en Development y este endpoint solo atiende solicitudes desde la misma máquina.
/// </summary>
[ApiController]
[AllowAnonymous]
[ApiExplorerSettings(IgnoreApi = true)]
[Route("api/dev/token")]
public sealed class TokenDesarrolloController(IOptionsMonitor<JwtBearerOptions> jwt, TimeProvider clock) : ControllerBase
{
    private static readonly TimeSpan Vigencia = TimeSpan.FromMinutes(15);

    [HttpPost]
    public IActionResult Emitir(TokenDesarrolloRequest request)
    {
        if (HttpContext.Connection.RemoteIpAddress is not { } ip || !System.Net.IPAddress.IsLoopback(ip))
            return NotFound();
        if (request.NationalId is not { Length: 8 } dni || dni.Any(c => c < '0' || c > '9'))
            return BadRequest(new { message = "El DNI debe tener 8 dígitos del 0 al 9." });

        // Usa la misma clave, emisor y audiencia que valida el esquema Bearer (User Secrets + appsettings).
        var validation = jwt.Get(JwtBearerDefaults.AuthenticationScheme).TokenValidationParameters;
        var key = validation.IssuerSigningKeys?.FirstOrDefault()
            ?? throw new InvalidOperationException("JWT no está configurado. Ejecuta scripts/nuevo-token.ps1 una vez.");
        var scopes = request.ReadOnly ? "transferencias.read" : "transferencias.read transferencias.write";
        var now = clock.GetUtcNow().UtcDateTime;
        var token = new JwtSecurityToken(validation.ValidIssuer, validation.ValidAudiences!.First(),
            [new Claim(JwtRegisteredClaimNames.Sub, $"cliente-{dni}"), new Claim("national_id", dni), new Claim("scope", scopes)],
            now, now.Add(Vigencia), new SigningCredentials(key, SecurityAlgorithms.HmacSha256));
        return Ok(new { accessToken = new JwtSecurityTokenHandler().WriteToken(token), expiresIn = (int)Vigencia.TotalSeconds });
    }
}
