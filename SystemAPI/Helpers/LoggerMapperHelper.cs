namespace SystemAPI.Helpers;

public static class LoggerMapperHelper
{
    public static string ToString<T>(T obj) where T : class
    {
        if (obj == null)
            return string.Empty;

        var props = typeof(T).GetProperties();
        var list = new List<string>();

        foreach (var prop in props)
        {
            var value = prop.GetValue(obj);

            if (value == null)
                continue;

            if (IsSimpleType(prop.PropertyType))
            {
                list.Add($"{prop.Name}:{value}");
            }
        }

        return string.Join(" ", list);
    }

    private static bool IsSimpleType(Type type)
    {
        return
            type.IsPrimitive ||
            type.IsEnum ||
            type == typeof(string) ||
            type == typeof(decimal);
    }
}
