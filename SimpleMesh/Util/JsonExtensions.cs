using System.Diagnostics.CodeAnalysis;
using System.Text.Json;

namespace SimpleMesh.Util;

static class JsonExtensions
{
    public static bool TryGetStringProperty(this JsonElement element, string propertyName,
        [NotNullWhen(true)] out string? value)
    {
        value = null;
        if (!element.TryGetProperty(propertyName, out var prop))
            return false;
        value = prop.GetString();
        return value != null;
    }

    public static bool TryGetSingleProperty(this JsonElement element, string propertyName, out float value, float def = 0)
    {
        value = def;
        if (!element.TryGetProperty(propertyName, out var prop))
            return false;
        if (!prop.TryGetSingle(out value))
        {
            value = def;
            return false;
        }
        return true;
    }
}
