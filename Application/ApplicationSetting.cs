using Application.Ports;
using Application.Usecases.ClienteUseCase;
using Application.Usecases.CuentaUseCase;
using Application.Usecases.TransferenciaUseCase;
using Microsoft.Extensions.DependencyInjection;

namespace Application;

public static class ApplicationSetting
{
    /// <summary>
    /// Registra los casos de uso y el reloj en el contenedor de dependencias. Las implementaciones de infraestructura se seleccionan en la API.
    /// </summary>
    public static IServiceCollection AddApplicationServices(this IServiceCollection services)
    {
        services.AddSingleton(TimeProvider.System);
        services.AddTransient<IConsultarCliente, ValidarCliente>();
        services.AddTransient<IConsultarCuenta, ValidarCuenta>();
        // Una instancia coordina solicitudes concurrentes en la demo de un solo proceso.
        services.AddSingleton<IRealizarTransferencia, ValidarTransferencia>();
        return services;
    }
}
