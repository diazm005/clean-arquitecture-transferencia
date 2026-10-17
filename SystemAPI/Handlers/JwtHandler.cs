using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.IdentityModel.Tokens;
using System.Collections.Concurrent;

namespace SystemAPI.Handlers;

public static class JwtHandler
{
    // Al ser estatico se vuelve un singleton en toda la aplicacion
    private static readonly ConcurrentDictionary<string, JsonWebKeySet> _jwksCache = new();

    public static void ConfigureJwtAuthentication(this IServiceCollection services, IConfiguration configuration, bool isDevelopment)
    {
        //https://cognito-idp.us-east-1.amazonaws.com/us-east-1_RyxEjxH9S
        string? authorityUrl = isDevelopment ? configuration["Jwt:Issuer"] : Environment.GetEnvironmentVariable("JWT_ISSUER");
        string? validAudience = isDevelopment ? configuration["Jwt:Audience"] : Environment.GetEnvironmentVariable("JWT_AUDIENCE");

        if (string.IsNullOrEmpty(authorityUrl))
            throw new InvalidOperationException("JWT Issuer configuration is required");

        if (string.IsNullOrEmpty(validAudience))
            throw new InvalidOperationException("JWT Audience configuration is required");

        services.AddAuthentication(options =>
        {
            options.DefaultAuthenticateScheme = JwtBearerDefaults.AuthenticationScheme;
            options.DefaultChallengeScheme = JwtBearerDefaults.AuthenticationScheme;
        })
        .AddJwtBearer(options =>
        {
            options.Authority = authorityUrl;
            options.TokenValidationParameters = new TokenValidationParameters
            {
                ValidateIssuer = true,
                ValidateAudience = false,
                ValidateIssuerSigningKey = true,
                ValidIssuer = authorityUrl,
                ValidAudience = validAudience,
                IssuerSigningKeyResolver = (token, securityToken, kid, validationParameters) =>
                {
                    if (!string.IsNullOrEmpty(options.Authority) && _jwksCache.TryGetValue(options.Authority, out var cachedJwks))
                    {
                        return cachedJwks.Keys;
                    }

                    using var httpClient = new HttpClient();
                    var jwksJson = httpClient.GetStringAsync($"{options.Authority}/.well-known/jwks.json").Result;
                    var newJwks = new JsonWebKeySet(jwksJson);

                    if (!string.IsNullOrEmpty(options.Authority))
                    {
                        _jwksCache[options.Authority] = newJwks;
                    }

                    return newJwks.Keys;
                }
            };
        });
    }

    public static void ConfigureJwtScopes(this IServiceCollection services, IConfiguration configuration, bool isDevelopment)
    {
        services.AddAuthorization(options =>
        {
            var readScope = isDevelopment ? configuration["Jwt:ReadScope"] : Environment.GetEnvironmentVariable("JWT_READ_SCOPE");
            var updateScope = isDevelopment ? configuration["Jwt:UpdateScope"] : Environment.GetEnvironmentVariable("JWT_UPDATE_SCOPE");
            var writeScope = isDevelopment ? configuration["Jwt:WriteScope"] : Environment.GetEnvironmentVariable("JWT_WRITE_SCOPE");

            options.AddPolicy("ReadScope", policy =>
                policy.RequireAssertion(context => HasRequiredScope(context, readScope ?? string.Empty)));

            options.AddPolicy("UpdateScope", policy =>
                policy.RequireAssertion(context => HasRequiredScope(context, updateScope ?? string.Empty)));

            options.AddPolicy("WriteScope", policy =>
                policy.RequireAssertion(context => HasRequiredScope(context, writeScope ?? string.Empty)));
        });
    }

    private static bool HasRequiredScope(Microsoft.AspNetCore.Authorization.AuthorizationHandlerContext context, string requiredScope)
    {
        var scopeClaim = context.User.FindFirst("scope")?.Value;
        if (string.IsNullOrEmpty(scopeClaim))
            return false;

        var scopes = scopeClaim.Split(new char[] { ' ', ',' }, StringSplitOptions.RemoveEmptyEntries).Select(s => s.Trim()).Where(s => !string.IsNullOrEmpty(s)).ToArray();

        return scopes.Contains(requiredScope, StringComparer.OrdinalIgnoreCase);
    }

    public static void ConfigureJwtKeycloak(this IServiceCollection services, IConfiguration configuration, bool isDevelopment)
    {
        //string authorityUrl = "http://localhost:8080/realms/master";
        string? authorityUrl = isDevelopment ? configuration["Jwt:Issuer"] : Environment.GetEnvironmentVariable("JWT_ISSUER");
        string? validAudience = isDevelopment ? configuration["Jwt:Audience"] : Environment.GetEnvironmentVariable("JWT_AUDIENCE");

        if (string.IsNullOrEmpty(authorityUrl))
            throw new InvalidOperationException("JWT Issuer configuration is required");

        if (string.IsNullOrEmpty(validAudience))
            throw new InvalidOperationException("JWT Audience configuration is required");

        services.AddAuthentication(options =>
        {
            options.DefaultAuthenticateScheme = JwtBearerDefaults.AuthenticationScheme;
            options.DefaultChallengeScheme = JwtBearerDefaults.AuthenticationScheme;
        })
        .AddJwtBearer(options =>
        {
            options.Authority = authorityUrl;
            options.RequireHttpsMetadata = false;
            options.TokenValidationParameters = new TokenValidationParameters
            {
                ValidateIssuer = true,
                ValidateAudience = true,
                ValidateIssuerSigningKey = true,
                ValidIssuer = authorityUrl,
                ValidAudience = validAudience,
                IssuerSigningKeyResolver = (token, securityToken, kid, validationParameters) =>
                {
                    using var httpClient = new HttpClient();
                    return Task.Run(async () =>
                    {
                        var discoveryResponse = await httpClient.GetStringAsync($"{options.Authority}/protocol/openid-connect/certs");
                        var jwks = new JsonWebKeySet(discoveryResponse);
                        return jwks.Keys;
                    }).Result;
                }
            };
        });
    }

}