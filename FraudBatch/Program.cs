using FraudBatch;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using SqlServerInfrastructure.Persistence;

// El batchero puede atender consultas HTTP o procesar una cola mediante los modos explícitos once/watch.
try
{
    if (args.Length == 0 || args[0] == "--serve")
    {
        await FraudBatch.Http.BatchHttpHost.RunAsync(args.Length == 0 ? [] : args[1..]);
        return 0;
    }
    var options = BatchOptions.Parse(args);
    if (options.Help)
    {
        Console.WriteLine("FraudBatch --serve (servicio HTTP de evaluación; no necesita conexión SQL)");
        Console.WriteLine("FraudBatch --once | --watch [--interval-seconds 1] [--max-per-pass 1000] (cola SQL opcional)");
        Console.WriteLine("FraudBatch --self-test (comprueba reglas sin acceder a SQL Server)");
        Console.WriteLine("Conexión: variable ConnectionStrings__Transferencias; destino exclusivo BD_Equipo2.");
        return 0;
    }
    if (options.SelfTest)
        return PolicySelfTest.Run();

    var connectionString = Environment.GetEnvironmentVariable("ConnectionStrings__Transferencias");
    if (string.IsNullOrWhiteSpace(connectionString))
        throw new ArgumentException("Define ConnectionStrings__Transferencias antes de ejecutar el batch.");
    // Se valida la base de destino antes de abrir una conexión; no se imprime la cadena.
    try
    {
        connectionString = TransferenciasConnectionString.Validar(connectionString);
    }
    catch (InvalidOperationException)
    {
        throw new ArgumentException("La conexión debe tener formato SQL Server válido y apuntar exclusivamente a BD_Equipo2, sin adjuntar archivos.");
    }
    var contextOptions = new DbContextOptionsBuilder<TransferenciasDbContext>()
        .UseSqlServer(connectionString, sql => sql.CommandTimeout(15))
        .Options;

    using var cancellation = new CancellationTokenSource();
    Console.CancelKeyPress += (_, eventArgs) =>
    {
        eventArgs.Cancel = true;
        cancellation.Cancel();
    };
    Console.WriteLine(options.Watch ? "Batch de riesgo iniciado. Ctrl+C para detener." : "Procesando evaluaciones de riesgo pendientes.");
    do
    {
        var processed = 0;
        while (processed < options.MaxPerPass && !cancellation.IsCancellationRequested)
        {
            await using var db = new TransferenciasDbContext(contextOptions);
            var result = await new EvaluadorPendientes(db).ProcesarUnaAsync(cancellation.Token);
            if (result is null)
                break;
            processed++;
            Console.WriteLine($"Evaluación {result.EvaluacionId}: {result.Estado}; riesgo={result.Nivel?.ToString() ?? "sin resultado"}.");
        }
        Console.WriteLine($"Evaluaciones procesadas en esta pasada: {processed}.");
        if (processed == options.MaxPerPass)
            Console.WriteLine("Se alcanzó el máximo por pasada; podrían quedar solicitudes pendientes.");
        if (options.Watch)
            await Task.Delay(TimeSpan.FromSeconds(options.IntervalSeconds), cancellation.Token);
    } while (options.Watch && !cancellation.IsCancellationRequested);
    return 0;
}
catch (OperationCanceledException)
{
    Console.WriteLine("Batch detenido; las transacciones no confirmadas se revierten.");
    return 0;
}
catch (ArgumentException exception)
{
    // Los errores de configuración internos no deben revelar servidor, usuario o contraseña.
    Console.Error.WriteLine(exception.ParamName is null ? exception.Message : "Configuración no válida. Revisa los argumentos y la base de destino BD_Equipo2.");
    return 2;
}
catch (Exception exception)
{
    var sql = exception as SqlException ?? exception.InnerException as SqlException;
    Console.Error.WriteLine(sql is null
        ? $"No se pudo completar el batch ({exception.GetType().Name}). Ninguna transacción incompleta se confirma."
        : $"SQL Server rechazó o interrumpió el procesamiento (código {sql.Number}). Revisa conectividad, permisos y el esquema existente.");
    return 1;
}
