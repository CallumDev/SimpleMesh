using System;

namespace SimpleMesh
{
    public class ImageData(string name, byte[] data, string mimeType)
    {
        public string Name { get; private set; } = name;
        public ReadOnlySpan<byte> Data => data;
        public string MimeType { get; private set; } = mimeType;
    }
}