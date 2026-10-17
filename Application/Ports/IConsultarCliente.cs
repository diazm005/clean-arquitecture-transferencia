using Domain.Entities.Internals;
namespace Application.Ports;
public interface IConsultarCliente
{
    /// <summary>
    /// Define la consulta de un cliente por DNI; devuelve null si no existe.
    /// </summary>
    Task<Cliente?> ConsultarClienteAsync(string dni, CancellationToken ct = default);
}
