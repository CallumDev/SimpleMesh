using System.IO;
using System.Text;

namespace SimpleMesh.Formats
{
    static class Autodetect
    {
        public static Model Load(Stream stream, ModelLoadContext ctx)
        {
            var buf = new byte[256];
            int len = stream.Read(buf, 0, 256);
            stream.Seek(-len, SeekOrigin.Current);
            if (len < 4) {
                throw new ModelLoadException("Not a valid model file");
            }
            //Binary formats
            if (buf[0] == (byte) 'S' &&
                buf[1] == (byte) 'M' &&
                buf[2] == (byte) 'S' &&
                buf[3] == (byte) 'H')
            {
                return SMesh.SMeshLoader.Load(stream, ctx);
            }
            if (buf[0] == (byte) 'g' &&
                buf[1] == (byte) 'l' &&
                buf[2] == (byte) 'T' &&
                buf[3] == (byte) 'F')
            {
                return GLTF.GLBLoader.Load(stream, ctx);
            }
            //Text-based formats
            var encoding = DetectEncoding(buf, out int startIndex);
            if ((len - startIndex) < 2) throw new ModelLoadException("Unrecognised file format");
            var str = encoding.GetString(buf, startIndex, (len - startIndex));
            var tStart = str.TrimStart();
            if (tStart[0] == '<')
            {
                return Collada.ColladaLoader.Load(stream, ctx);
            } 
            if (tStart[0] == '{')
            {
                return GLTF.GLTFLoader.Load(stream, ctx);
            }
            return Obj.ObjLoader.Load(stream, ctx);
        }
        
        static Encoding DetectEncoding(byte[] bom, out int startIndex)
        {
            if (bom[0] == 0x2b && bom[1] == 0x2f && bom[2] == 0x76)
            {
                startIndex = 3;
                return Encoding.UTF7;
            }
            if (bom[0] == 0xef && bom[1] == 0xbb && bom[2] == 0xbf)
            {
                startIndex = 3;
                return Encoding.UTF8;
            }
            if (bom[0] == 0xff && bom[1] == 0xfe && bom[2] == 0 && bom[3] == 0)
            {
                startIndex = 4;
                return Encoding.UTF32; //UTF-32LE
            }
            if (bom[0] == 0xff && bom[1] == 0xfe)
            {
                startIndex = 2;
                return Encoding.Unicode; //UTF-16LE
            }
            if (bom[0] == 0xfe && bom[1] == 0xff)
            {
                startIndex = 2;
                return Encoding.BigEndianUnicode; //UTF-16BE
            }
            if (bom[0] == 0 && bom[1] == 0 && bom[2] == 0xfe && bom[3] == 0xff)
            {
                startIndex = 4;
                return new UTF32Encoding(true, true);  //UTF-32BE
            }
            // Default to UTF8
            startIndex = 0;
            return Encoding.UTF8;
        }
    }
}