using System;
using System.IO;

using System.Text.RegularExpressions;

namespace SimpleMesh.Formats.GLTF;

static class UriTools
{
    public static byte[] BytesFromUri(string str, IExternalResources external)
    {
        if (IsDataUri(str, out _, out int len))
            return Convert.FromBase64String(str.Substring(len));
        var mem = new MemoryStream();
        var stream = external.OpenStream(Uri.UnescapeDataString(str));
        if (stream == null)
            throw new ModelLoadException($"Unable to find external resource '{str}'");
        stream.CopyTo(mem);
        return mem.ToArray();
    }

    private static Regex dataUriRegex = new Regex(@"^data:(.*);(.*),");
    
    static bool IsDataUri(string uri, out string mime, out int len)
    {
        var match = dataUriRegex.Match(uri);
        if (match.Success)
        {
            mime = match.Groups[1].Value;
            len = match.Length;
            return true;
        }
        mime = null;
        len = 0;
        return false;
    }

    public static string MimeTypeFromUri(string str)
    {
        if (IsDataUri(str, out string mime, out _))
            return mime;
        var ext = Path.GetExtension(str);
        if (".png".Equals(ext, StringComparison.OrdinalIgnoreCase))
            return "image/png";
        if (".jpeg".Equals(ext, StringComparison.OrdinalIgnoreCase))
            return "image/jpeg";
        return "application/octet-stream";
    }

    public static string NameFromUri(string str, ref int counter)
    {
        if (IsDataUri(str, out string mime, out _))
        {
            if (mime.Equals("image/png", StringComparison.OrdinalIgnoreCase))
                return $"texture{counter++}.png";
            if (mime.Equals("image/jpeg", StringComparison.OrdinalIgnoreCase))
                return $"texture{counter++}.jpg";
            return $"file{counter++}.bin";
        }
        return str;
    }
}