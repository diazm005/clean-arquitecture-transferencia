using Application.Ports;
using Domain.Entities.Internals;
using Domain.Interfaces;
namespace Application.Usecases.ClienteUseCase;

public sealed class ValidarCliente(IConsultarClienteRepository clientes) : IConsultarCliente
{
    /// <summary>
    /// Consulta el cliente por DNI a través del repositorio. Devuelve null si no existe.
    /// </summary>
    public Task<Cliente?> ConsultarClienteAsync(string dni, CancellationToken ct = default) =>
        clientes.ObtenerClienteAsync(dni, ct);
}
