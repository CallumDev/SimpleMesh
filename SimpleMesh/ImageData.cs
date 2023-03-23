using System;

namespace SimpleMesh
{
    public class ImageData
    {
        private byte[] data;
        
        public string Name { get; private set; }
        public ReadOnlySpan<byte> Data => data;
        public string MimeType { get; private set; }

        public ImageData(string name, byte[] data, string mimeType)
        {
            Name = name;
            MimeType = mimeType;
            this.data = data;
        }

        internal ImageData()
        {
        }
    }
}