using Application.Handlers.Clientes;
using Application.Handlers.Cuentas;
using Application.Handlers.Transferencias;
using Application.Ports;
using Microsoft.Extensions.DependencyInjection;

namespace Application;

public static class ApplicationSetting
{
    /// <summary>
    /// Registra los handlers y el reloj en el contenedor de dependencias. Las implementaciones de infraestructura se seleccionan en la API.
    /// </summary>
    public static IServiceCollection AddApplicationServices(this IServiceCollection services)
    {
        services.AddSingleton(TimeProvider.System);
        services.AddTransient<IConsultarCliente, ConsultarClienteHandler>();
        services.AddTransient<IConsultarCuenta, ConsultarCuentaHandler>();
        services.AddTransient<IConsultarTransferencia, ConsultarTransferenciaHandler>();
        // Una instancia coordina solicitudes concurrentes en la demo de un solo proceso.
        services.AddSingleton<IRealizarTransferencia, RealizarTransferenciaHandler>();
        return services;
    }
}
