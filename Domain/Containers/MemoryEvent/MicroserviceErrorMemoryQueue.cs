using Domain.Entities.Internals;
using System.Runtime.CompilerServices;
using System.Threading.Channels;

namespace Domain.Containers.MemoryEvent;

public class MicroserviceErrorMemoryQueue : IDisposable
{
    private readonly Channel<MicroserviceErrorTraceEntity> _channel;
    private int _length; // contador aproximado
    private bool _disposed;
    private readonly int _capacity;
    private int capacity = 500; // Tiene que ser estatico para no saturar la memoria

    public MicroserviceErrorMemoryQueue(BoundedChannelFullMode fullMode = BoundedChannelFullMode.Wait)
    {
        if (capacity <= 0) throw new ArgumentOutOfRangeException(nameof(capacity));
        _capacity = capacity;

        var options = new BoundedChannelOptions(_capacity)
        {
            FullMode = fullMode,       // Wait | DropWrite | DropOldest | DropNewest
            SingleReader = false,      // múltiples consumidores
            SingleWriter = false       // múltiples productores
        };

        _channel = Channel.CreateBounded<MicroserviceErrorTraceEntity>(options);
    }

    // ---- Agregar de forma asíncrona; esperar si la cola está llena
    public async ValueTask<bool> PushAsync(MicroserviceErrorTraceEntity item, CancellationToken ct = default)
    {
        ThrowIfDisposed();
        ArgumentNullException.ThrowIfNull(item);

        while (await _channel.Writer.WaitToWriteAsync(ct).ConfigureAwait(false))
        {
            if (_channel.Writer.TryWrite(item))
            {
                System.Threading.Interlocked.Increment(ref _length);
                return true;
            }
        }
        return false;
    }

    public bool TryPush(MicroserviceErrorTraceEntity item)
    {
        ThrowIfDisposed();
        ArgumentNullException.ThrowIfNull(item);

        if (_channel.Writer.TryWrite(item))
        {
            System.Threading.Interlocked.Increment(ref _length);
            return true;
        }
        return false;
    }

    // ---- Retirar un elemento de la cola
    public async ValueTask<MicroserviceErrorTraceEntity> PopAsync(CancellationToken ct = default)
    {
        var item = await _channel.Reader.ReadAsync(ct).ConfigureAwait(false);
        System.Threading.Interlocked.Decrement(ref _length);
        return item;
    }

    // ---- Consumo continuo (para BackgroundService)
    public async IAsyncEnumerable<MicroserviceErrorTraceEntity> ReadAllAsync([EnumeratorCancellation] CancellationToken ct = default)
    {
        while (await _channel.Reader.WaitToReadAsync(ct).ConfigureAwait(false))
        {
            while (_channel.Reader.TryRead(out var item))
            {
                System.Threading.Interlocked.Decrement(ref _length);
                yield return item;
            }
        }
    }

    // Señala que no se aceptarán más escrituras
    public void Complete() => _channel.Writer.TryComplete();

    // Estado útil para métricas/observabilidad
    public bool IsCompleted => _channel.Reader.Completion.IsCompleted;
    public int ApproxLength => System.Threading.Interlocked.CompareExchange(ref _length, 0, 0);

    // Acceso al lector y al escritor de la cola para casos avanzados
    public ChannelReader<MicroserviceErrorTraceEntity> Reader => _channel.Reader;
    public ChannelWriter<MicroserviceErrorTraceEntity> Writer => _channel.Writer;

    // Liberar recursos y cerrar la escritura en la cola
    public void Dispose()
    {
        if (!_disposed)
        {
            Complete();
            _disposed = true;
        }
    }

    private void ThrowIfDisposed()
    {
        if (_disposed) throw new ObjectDisposedException(nameof(MicroserviceErrorMemoryQueue));
    }
}
