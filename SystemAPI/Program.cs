using Application;
using AntifraudeInfrastructure;
using AuditoriaInfrastructure;
using CamaraCompensatorioInfrastructure;
using CoreBancarioInfrastructure;
using Domain.Interfaces;
using NotificacionInfrastructure;
using System.Text.Json.Serialization;

var builder = WebApplication.CreateBuilder(args);

// Este arranque habilita datos y OTP simulados exclusivamente para el taller local.
if (!builder.Environment.IsDevelopment())
    throw new InvalidOperationException("La demo solo funciona en Development. Configura adaptadores reales y autenticación antes de desplegar.");

builder.Services.AddApplicationServices();
builder.Services.AddSingleton<CoreBancarioAdapter>();
builder.Services.AddSingleton<IConsultarClienteRepository>(sp => sp.GetRequiredService<CoreBancarioAdapter>());
builder.Services.AddSingleton<IConsultarCuentaRepository>(sp => sp.GetRequiredService<CoreBancarioAdapter>());
builder.Services.AddSingleton<ICoreBancarioGateway>(sp => sp.GetRequiredService<CoreBancarioAdapter>());
builder.Services.AddSingleton<IAntifraudeService, AntifraudeAdapter>();
builder.Services.AddSingleton<IOtpService, OtpSimulado>();
builder.Services.AddSingleton<IAuditoriaRepository, AuditoriaRepository>();
builder.Services.AddSingleton<ISegmentoLimiteRepository, SegmentoLimiteRepository>();
builder.Services.AddSingleton<ICamaraCompensacionGateway, CamaraCompensacionAdapter>();
builder.Services.AddSingleton<INotificadorCliente, NotificacionesAdapter>();
builder.Services.AddHealthChecks();
builder.Services.AddControllers().AddJsonOptions(o => o.JsonSerializerOptions.Converters.Add(new JsonStringEnumConverter()));
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();
builder.Services.AddProblemDetails();
var app = builder.Build();
app.UseExceptionHandler();
app.UseSwagger();
app.UseSwaggerUI();
app.MapControllers();
app.Run();
