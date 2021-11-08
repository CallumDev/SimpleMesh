using System;
using System.IO;
using System.Text;

namespace SimpleMesh.Formats.GLTF
{
    static class GLBLoader
    {
        private const uint GLTF_MAGIC = 0x46546C67;
        private const uint CHUNK_JSON = 0x4E4F534A;
        private const uint CHUNK_BIN = 0x004E4942;
        
        public static Model Load(Stream stream, ModelLoadContext ctx)
        {
            using var reader = new BinaryReader(stream);
            var magic = reader.ReadUInt32();
            if (magic != GLTF_MAGIC) throw new ModelLoadException("Not a valid glb binary (magic)");
            var version = reader.ReadUInt32();
            if (version != 2) throw new ModelLoadException("Invalid glb version (must be 2)");
            var totalLength = reader.ReadUInt32(); //length

            uint jsonLength = reader.ReadUInt32();
            uint jsonMagic = reader.ReadUInt32();
            if(jsonMagic != CHUNK_JSON) throw new ModelLoadException("Not a valid glb binary (json chunk)");

            var jsonText = Encoding.UTF8.GetString(reader.ReadBytes((int)jsonLength));
            byte[] binChunk = null;
            if (totalLength > 5 * sizeof(uint) + jsonLength)
            {
                uint binLength = reader.ReadUInt32();
                uint binMagic = reader.ReadUInt32();
                if (binMagic != CHUNK_BIN) throw new ModelLoadException("Not a valid glb binary (bin chunk)");
                binChunk = reader.ReadBytes((int) binLength);
            }

            return GLTFLoader.Load(jsonText, binChunk, ctx);
        }
    }
}