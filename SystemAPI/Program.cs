using Application;
using Application.Internals.Adapters;
using Microsoft.AspNetCore.Mvc;
using SystemAPI.Helpers;
using AntifraudeInfrastructure;
using AntifraudeInfrastructure.Http;
using AuditoriaInfrastructure;
using CamaraCompensatorioInfrastructure;
using CoreBancarioInfrastructure;
using Domain.Interfaces;
using NotificacionInfrastructure;
using System.Text.Json.Serialization;
using SqlServerInfrastructure;
using Application.Ports;
using SystemAPI.Handlers;
using SystemAPI.Security;
using Microsoft.OpenApi;

var builder = WebApplication.CreateBuilder(args);

// Este arranque habilita datos y OTP simulados exclusivamente para el taller local.
if (!builder.Environment.IsDevelopment())
    throw new InvalidOperationException("La demo solo funciona en Development. Configura proveedores reales e identidad de producción antes de desplegar.");

builder.Services.AddApplicationServices();
builder.Services.AddHttpContextAccessor();
builder.Services.AddScoped<IClienteActual, ClienteActualHttp>();
builder.Services.AddTransferenciasJwt();
var provider = builder.Configuration["Infrastructure:Provider"] ?? "Memory";
if (string.Equals(provider, "SqlServer", StringComparison.OrdinalIgnoreCase))
{
    builder.Services.AddSqlServerInfrastructure(builder.Configuration.GetConnectionString("Transferencias"),
        builder.Configuration["Antifraude:BaseUrl"]);
}
else if (string.Equals(provider, "Memory", StringComparison.OrdinalIgnoreCase))
{
    builder.Services.AddSingleton<CoreBancarioAdapter>();
    builder.Services.AddSingleton<IConsultarClienteRepository>(sp => sp.GetRequiredService<CoreBancarioAdapter>());
    builder.Services.AddSingleton<IConsultarCuentaRepository>(sp => sp.GetRequiredService<CoreBancarioAdapter>());
    builder.Services.AddSingleton<IConsultarBancoRepository>(sp => sp.GetRequiredService<CoreBancarioAdapter>());
    builder.Services.AddSingleton<ICoreBancarioGateway>(sp => sp.GetRequiredService<CoreBancarioAdapter>());
    builder.Services.AddSingleton<IControlOperacion, ControlOperacionMemoria>();
    // El riesgo lo calcula Fraude.Batch (transferencias-api-antifraude) también sin SQL.
    builder.Services.AddFraudBatchAntifraude(builder.Configuration["Antifraude:BaseUrl"]);
    builder.Services.AddSingleton<IAuditoriaRepository, AuditoriaRepository>();
    builder.Services.AddSingleton<ISegmentoLimiteRepository, SegmentoLimiteRepository>();
    builder.Services.AddSingleton<INotificadorCliente, NotificacionesAdapter>();
}
else throw new InvalidOperationException("Infrastructure:Provider debe ser Memory o SqlServer.");

// OTP y cámara siguen simulados mientras el taller incorpora proveedores reales.
builder.Services.AddSingleton<IOtpService, OtpSimulado>();
builder.Services.AddSingleton<ICamaraCompensacionGateway, CamaraCompensacionAdapter>();
builder.Services.AddHealthChecks();
builder.Services.AddControllers()
    .AddJsonOptions(o => o.JsonSerializerOptions.Converters.Add(new JsonStringEnumConverter()))
    // Los cuerpos mal formados, como un GUID inválido, también devuelven la lista de errores BIAN.
    .ConfigureApiBehaviorOptions(o => o.InvalidModelStateResponseFactory = ctx =>
        new ObjectResult(EasyBianResponseHelper.WarningResponse(ctx.ModelState
            .SelectMany(e => e.Value!.Errors.Select(x => new ValidationResultAdapter
                { Code = "400", Message = string.IsNullOrEmpty(x.ErrorMessage) ? "Solicitud inválida." : x.ErrorMessage, Field = e.Key }))
            .ToList())) { StatusCode = 400 });
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(options =>
{
    options.AddSecurityDefinition("Bearer", new OpenApiSecurityScheme
    {
        Type = SecuritySchemeType.Http, Scheme = "bearer", BearerFormat = "JWT",
        Description = "Token de cliente del taller. Usa Authorize e ingresa únicamente el token."
    });
    options.AddSecurityRequirement(document => new OpenApiSecurityRequirement
    {
        [new OpenApiSecuritySchemeReference("Bearer", document)] = []
    });
});
builder.Services.AddExceptionHandler<SystemAPI.Handlers.InfrastructureExceptionHandler>();
builder.Services.AddProblemDetails();
var app = builder.Build();
app.UseExceptionHandler();
app.UseSwagger();
app.UseSwaggerUI();
app.UseAuthentication();
app.UseAuthorization();
app.MapControllers();
app.Run();

// Permite probar el pipeline real con WebApplicationFactory sin abrir puertos ni usar SQL Server.
public partial class Program { }
