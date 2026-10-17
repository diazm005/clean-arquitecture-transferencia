using Domain.Entities.Internals;
namespace Application.Ports;
public interface IConsultarCuenta
{
    /// <summary>
    /// Define la consulta de una cuenta por número; devuelve null si no existe.
    /// </summary>
    Task<Cuenta?> ConsultarCuentaAsync(string numero, CancellationToken ct = default);
}
