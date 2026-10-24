using Domain.Entities.Internals;
using Domain.Services;

namespace FraudBatch;

/// <summary>Comprobaciones de límites e invariantes; no abre conexiones ni modifica datos.</summary>
internal static class PolicySelfTest
{
    public static int Run()
    {
        (string Name, ContextoFraude Input, NivelRiesgo Expected)[] cases =
        [
            ("Monto mínimo", new(0.01m, 0, false), NivelRiesgo.Bajo),
            ("Destino nuevo debajo del umbral", new(4999.99m, 0, false), NivelRiesgo.Bajo),
            ("Destino nuevo en umbral", new(5000m, 0, false), NivelRiesgo.Medio),
            ("Destino conocido en umbral", new(5000m, 0, true), NivelRiesgo.Bajo),
            ("Monto debajo de diez mil", new(9999.99m, 0, true), NivelRiesgo.Bajo),
            ("Monto diez mil", new(10000m, 0, true), NivelRiesgo.Medio),
            ("Monto debajo de cuarenta mil", new(39999.99m, 0, true), NivelRiesgo.Medio),
            ("Monto cuarenta mil", new(40000m, 0, true), NivelRiesgo.Alto),
            ("Cuatro débitos previos", new(100m, 4, true), NivelRiesgo.Bajo),
            ("Cinco débitos previos", new(100m, 5, true), NivelRiesgo.Alto),
            ("Riesgo alto prevalece sobre medio", new(10000m, 5, false), NivelRiesgo.Alto),
        ];
        var passed = 0;
        foreach (var item in cases)
        {
            var decision = PoliticaFraude.Evaluar(item.Input);
            Check(decision.Nivel == item.Expected && decision.VersionRegla == "TALLER-1.0"
                && !string.IsNullOrWhiteSpace(decision.Motivo), item.Name);
            passed++;
        }
        foreach (var invalid in new[] { new ContextoFraude(0m, 0, true), new(-1m, 0, true), new(100m, -1, true) })
        {
            var rejected = false;
            try { PoliticaFraude.Evaluar(invalid); }
            catch (ArgumentOutOfRangeException) { rejected = true; }
            Check(rejected, "Contexto inválido rechazado");
            passed++;
        }
        var conflictRejected = false;
        try { BatchOptions.Parse(["--watch", "--once"]); }
        catch (ArgumentException) { conflictRejected = true; }
        Check(conflictRejected, "Modos de ejecución excluyentes");
        passed++;
        Console.WriteLine($"{passed} comprobaciones aprobadas. No se accedió a ninguna base de datos.");
        return 0;
    }

    private static void Check(bool condition, string scenario)
    {
        if (!condition)
            throw new InvalidOperationException($"Comprobación fallida: {scenario}.");
        Console.WriteLine($"OK: {scenario}");
    }
}
