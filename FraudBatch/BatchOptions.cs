namespace FraudBatch;

/// <summary>Valida los argumentos antes de abrir una conexión o procesar operaciones.</summary>
internal sealed record BatchOptions(bool Watch, bool SelfTest, bool Help, int IntervalSeconds, int MaxPerPass)
{
    public static BatchOptions Parse(string[] args)
    {
        bool watch = false, once = false, selfTest = false, help = false;
        int interval = 1, maximum = 1000;
        for (var i = 0; i < args.Length; i++)
        {
            switch (args[i])
            {
                case "--watch": watch = true; break;
                case "--once": once = true; break;
                case "--self-test": selfTest = true; break;
                case "--help" or "-h": help = true; break;
                case "--interval-seconds": interval = ReadNumber(args, ref i, 1, 300); break;
                case "--max-per-pass": maximum = ReadNumber(args, ref i, 1, 100_000); break;
                default: throw new ArgumentException("Argumento no reconocido. Consulta --help.");
            }
        }
        if (watch && once)
            throw new ArgumentException("Selecciona --once o --watch, no ambos.");
        if (selfTest && (watch || once))
            throw new ArgumentException("--self-test se ejecuta por separado, sin conexión a la base de datos.");
        return new(watch, selfTest, help, interval, maximum);
    }

    private static int ReadNumber(string[] args, ref int position, int min, int max)
    {
        if (++position >= args.Length || !int.TryParse(args[position], out var value) || value < min || value > max)
            throw new ArgumentException($"El argumento numérico debe estar entre {min} y {max}.");
        return value;
    }
}
