using SystemAPI.Handlers.ArifyAuthorizer;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authorization;

namespace SystemAPI.Handlers;

public static class ArifyAuthorizerHandler
{
    public static void ConfigureArixAuthentication(this IServiceCollection services, IConfiguration configuration)
    {
        // Configurar esquema de autenticación optimizado
        services.AddAuthentication(options =>
        {
            options.DefaultScheme = "ArifyScheme";
            options.DefaultForbidScheme = "ArifyScheme";
        }).AddScheme<AuthenticationSchemeOptions, ArifyBasicAuthenticationHandler>("ArifyScheme", options => { });

        // Registrar IHttpContextAccessor como Scoped para mejor rendimiento
        services.AddHttpContextAccessor();

        // Configurar opciones de mapeo de scopes desde configuración
        services.Configure<ArifyScopeOptions>(options =>
        {
            // Configuración por defecto - puede ser sobrescrita por appsettings.json
            options.PolicyMappings = new Dictionary<string, string>
            {
                { "ReadExample", "api/r:ex" },
                { "WriteExample", "api/w:ex" }
            };
        });

        // Registrar el manejador como Scoped para optimizar memoria
        services.AddScoped<IAuthorizationHandler, ArifyScopeRequirementHandler>();

        // Configurar autorización con política dinámica
        services.AddAuthorization(options =>
        {
            // Política base que maneja cualquier scope dinámicamente
            options.AddPolicy("ArifyPolicy", policy => 
                policy.Requirements.Add(new ArifyScopeRequirement()));
        });
    }
}