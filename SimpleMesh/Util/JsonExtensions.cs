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
}