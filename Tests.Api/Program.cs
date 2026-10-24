using System.IdentityModel.Tokens.Jwt;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text.Json;
using Application.Adapters;
using Domain.Interfaces;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.IdentityModel.Tokens;
using Microsoft.Extensions.Options;

namespace Transferencias.Api.Tests;

// Pruebas del middleware JWT y de los casos de uso reales, con repositorios aislados en memoria.
internal static class JwtTests
{
    private static readonly SymmetricSecurityKey Key = new(RandomNumberGenerator.GetBytes(64));
    private static int _total;

    private static string Token(string? dni = "12345678", string scope = "transferencias.read transferencias.write",
        string issuer = "clean-transferencias-dev", string audience = "clean-transferencias-api",
        DateTime? expires = null, DateTime? notBefore = null, bool noExpiration = false,
        bool unsigned = false, SecurityKey? key = null, string algorithm = SecurityAlgorithms.HmacSha256,
        bool duplicateDni = false, bool separateScopes = false)
    {
        var claims = new List<Claim> { new("sub", "cliente-prueba") };
        if (dni is not null) claims.Add(new("national_id", dni));
        if (duplicateDni) claims.Add(new("national_id", "87654321"));
        claims.AddRange((separateScopes ? scope.Split(' ') : [scope]).Select(s => new Claim("scope", s)));
        var jwt = new JwtSecurityToken(issuer, audience, claims, notBefore ?? DateTime.UtcNow.AddMinutes(-30),
            noExpiration ? null : expires ?? DateTime.UtcNow.AddMinutes(15),
            unsigned ? null : new SigningCredentials(key ?? Key, algorithm));
        return new JwtSecurityTokenHandler().WriteToken(jwt);
    }

    private static void Check(bool condition, string name)
    {
        if (!condition) throw new InvalidOperationException("FALLÓ: " + name);
        Console.WriteLine("OK " + ++_total + ": " + name);
    }

    public static async Task Main()
    {
        // Una copia nueva sin claves debe detenerse al arrancar, con un mensaje de configuración claro.
        await using (var missingKeys = new ApiFactory(missingKey: true))
        {
            var failedSafely = false;
            try { using var invalidClient = missingKeys.CreateClient(); }
            catch (OptionsValidationException e) { failedSafely = e.Message.Contains("JWT no está configurado"); }
            Check(failedSafely, "Sin clave JWT la API no inicia");
        }
        await using var factory = new ApiFactory();
        using var client = factory.CreateClient();
        async Task<HttpResponseMessage> Send(string path, string? token, object? body = null)
        {
            using var request = new HttpRequestMessage(body is null ? HttpMethod.Get : HttpMethod.Post, path);
            if (token is not null) request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
            if (body is not null) request.Content = JsonContent.Create(body);
            return await client.SendAsync(request);
        }
        async Task Expect(string path, string? token, HttpStatusCode status, string name, object? body = null)
        {
            using var response = await Send(path, token, body);
            var content = await response.Content.ReadAsStringAsync();
            Check(response.StatusCode == status, $"{name} ({(int)response.StatusCode})");
            if (status is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden)
            {
                using var json = JsonDocument.Parse(content);
                Check(json.RootElement.GetProperty("errors").GetArrayLength() > 0, name + ": error BIAN");
            }
            if (status == HttpStatusCode.Unauthorized)
                Check(response.Headers.WwwAuthenticate.Any(h => h.Scheme == "Bearer"), name + ": challenge Bearer");
        }

        var ana = Token();
        var luis = Token("87654321");
        foreach (var path in new[] { "/api/client/12345678", "/api/account/001", "/api/transactions/" + Guid.NewGuid(), "/debug-claims" })
            await Expect(path, null, HttpStatusCode.Unauthorized, "Sin token " + path);
        var request = new InitiatePaymentExecutionRequest(Guid.NewGuid(), "12345678", "001", "DESTINO", "BANCO_B", 100);
        await Expect("/api/transactions", null, HttpStatusCode.Unauthorized, "POST sin token", request);
        foreach (var (name, token) in new (string, string)[]
        {
            ("Vencido", Token(expires: DateTime.UtcNow.AddMinutes(-5))),
            ("Aún no vigente", Token(notBefore: DateTime.UtcNow.AddMinutes(5))),
            ("Firma incorrecta", Token(key: new SymmetricSecurityKey(RandomNumberGenerator.GetBytes(64)))),
            ("Issuer incorrecto", Token(issuer: "otro")), ("Audience incorrecta", Token(audience: "otro")),
            ("Sin expiración", Token(noExpiration: true)), ("Sin firma", Token(unsigned: true)),
            ("Algoritmo no permitido", Token(algorithm: SecurityAlgorithms.HmacSha384)),
            ("DNI ausente", Token(dni: null)), ("DNI ambiguo", Token(duplicateDni: true)),
            ("DNI inválido", Token(dni: "123")), ("DNI Unicode", Token(dni: "١٢٣٤٥٦٧٨")),
            ("JWT mal formado", "no-es-un-jwt")
        }) await Expect("/api/account/001", token, HttpStatusCode.Unauthorized, name);

        // Cambiar el contenido sin volver a firmar debe invalidar el JWT.
        var parts = ana.Split('.');
        parts[1] = Base64UrlEncoder.Encode(Base64UrlEncoder.Decode(parts[1]).Replace("12345678", "87654321"));
        await Expect("/api/account/002", string.Join('.', parts), HttpStatusCode.Unauthorized, "Token manipulado");
        await Expect("/api/account/001?access_token=" + ana, null, HttpStatusCode.Unauthorized, "Token por query rechazado");
        await Expect("/api/account/001", Token(scope: "transferencias.write"), HttpStatusCode.Forbidden, "Sin permiso de lectura");
        await Expect("/api/account/001", Token(scope: "prefix.transferencias.read"), HttpStatusCode.Forbidden, "Scope debe ser exacto");
        await Expect("/api/account/001", Token(scope: "TRANSFERENCIAS.READ"), HttpStatusCode.Forbidden, "Scope distingue mayúsculas");
        await Expect("/api/transactions", Token(scope: "transferencias.read"), HttpStatusCode.Forbidden, "Sin permiso de escritura", request);
        await Expect("/api/account/001", Token(separateScopes: true), HttpStatusCode.OK, "Scopes separados admitidos");
        await Expect("/api/client/12345678", ana, HttpStatusCode.OK, "Cliente propio");
        await Expect("/api/client/87654321", ana, HttpStatusCode.NotFound, "Cliente ajeno oculto");
        await Expect("/api/account/001", ana, HttpStatusCode.OK, "Cuenta propia");
        await Expect("/api/account/002", ana, HttpStatusCode.NotFound, "Cuenta ajena oculta");
        await Expect("/api/account/002", luis, HttpStatusCode.OK, "Luis consulta su cuenta");

        var accounts = factory.Services.GetRequiredService<IConsultarCuentaRepository>();
        var audit = factory.Services.GetRequiredService<IAuditoriaRepository>();
        var saldoInicial = (await accounts.ObtenerCuentaAsync("001"))!.Saldo;
        await Expect("/api/transactions", ana, HttpStatusCode.Forbidden, "DNI del cuerpo no suplanta al token",
            request with { CustomerNationalId = "87654321", SourceAccountNumber = "002" });
        await Expect("/api/transactions", ana, HttpStatusCode.Forbidden, "No se puede debitar una cuenta ajena",
            request with { SourceAccountNumber = "002" });
        Check(await audit.ObtenerAsync(request.TransactionReference) is null &&
            (await accounts.ObtenerCuentaAsync("001"))!.Saldo == saldoInicial &&
            (await accounts.ObtenerCuentaAsync("002"))!.Saldo == 2000, "Intentos no autorizados no registran operaciones ni mueven dinero");
        await Expect("/api/transactions", ana, HttpStatusCode.BadRequest, "Entrada inválida autenticada",
            request with { Amount = -1 });
        await Expect("/api/transactions", ana, HttpStatusCode.OK, "Transferencia propia autenticada", request);
        await Expect("/api/transactions", ana, HttpStatusCode.OK, "Reintento idempotente con JWT", request);
        Check((await accounts.ObtenerCuentaAsync("001"))!.Saldo == saldoInicial - 105, "JWT conserva un solo débito y comisión");
        await Expect("/api/transactions/" + request.TransactionReference, ana, HttpStatusCode.OK, "Consulta de transferencia propia");
        await Expect("/api/transactions/" + request.TransactionReference, luis, HttpStatusCode.NotFound, "Transferencia ajena oculta");
        await Expect("/api/transactions", luis, HttpStatusCode.NotFound, "Referencia de otro cliente oculta al enviar",
            request with { CustomerNationalId = "87654321", SourceAccountNumber = "002" });
        await Expect("/ping", null, HttpStatusCode.OK, "Ping público");
        await Expect("/health/live", null, HttpStatusCode.OK, "Disponibilidad pública");
        using var swagger = await Send("/swagger/v1/swagger.json", null);
        using var schema = JsonDocument.Parse(await swagger.Content.ReadAsStringAsync());
        Check(schema.RootElement.GetProperty("components").GetProperty("securitySchemes").GetProperty("Bearer")
            .GetProperty("scheme").GetString() == "bearer", "Swagger anuncia autenticación Bearer");
        Console.WriteLine($"Completadas: {_total} comprobaciones JWT/API. SQL Server no utilizado.");
    }

    private sealed class ApiFactory(bool missingKey = false) : WebApplicationFactory<global::Program>
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseEnvironment("Development");
            // Program.cs lee el proveedor antes de Build; UseSetting llega a tiempo, ConfigureAppConfiguration no.
            builder.UseSetting("Infrastructure:Provider", "Memory");
            builder.ConfigureAppConfiguration((_, config) => config.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Infrastructure:Provider"] = "Memory", ["Logging:LogLevel:Default"] = "Error"
            }));
            // El riesgo usa el simulador para no depender de que Fraude.Batch esté levantado.
            builder.ConfigureServices(services => services.AddSingleton<IAntifraudeService, AntifraudeInfrastructure.AntifraudeAdapter>());
            builder.ConfigureServices(services => services.PostConfigure<JwtBearerOptions>("Bearer", options =>
            {
                // Solo se sustituye la clave y su emisor de prueba; se conserva la validación del servicio.
                options.TokenValidationParameters.IssuerSigningKeys = missingKey ? [] : [Key];
                options.TokenValidationParameters.IssuerSigningKey = null;
                options.TokenValidationParameters.ValidIssuer = "clean-transferencias-dev";
                options.TokenValidationParameters.ValidIssuers = null;
                options.TokenValidationParameters.ValidAudience = null;
                options.TokenValidationParameters.ValidAudiences = ["clean-transferencias-api"];
            }));
        }
    }
}
