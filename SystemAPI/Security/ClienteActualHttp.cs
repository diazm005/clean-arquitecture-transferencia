using Application.Ports;

namespace SystemAPI.Security;

/// <summary>Traduce una identidad autenticada al puerto que utiliza Aplicación.</summary>
public sealed class ClienteActualHttp(IHttpContextAccessor accessor) : IClienteActual
{
    public string? Dni => accessor.HttpContext?.User.Identity?.IsAuthenticated == true
        ? accessor.HttpContext.User.FindFirst("national_id")?.Value : null;
}
