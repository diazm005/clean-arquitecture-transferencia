using Application.Ports;
using Domain.Entities.Internals;
using Domain.Interfaces;
namespace Application.Usecases.CuentaUseCase;

public sealed class ValidarCuenta(IConsultarCuentaRepository cuentas) : IConsultarCuenta
{
    /// <summary>
    /// Consulta la cuenta por su número a través del repositorio. Devuelve null si no existe.
    /// </summary>
    public Task<Cuenta?> ConsultarCuentaAsync(string numero, CancellationToken ct = default) =>
        cuentas.ObtenerCuentaAsync(numero, ct);
}
