using System.IO;
using System.Numerics;
using System.Text;
namespace SimpleMesh.Util
{
    static class ReadWriteExtensions
    {
        public static void WriteStringUTF8(this BinaryWriter writer, string s)
        {
            if(s == null) { 
                writer.Write7BitEncodedInt(0);
            }
            else {
                writer.Write7BitEncodedInt(s.Length + 1);
                var bytes = Encoding.UTF8.GetBytes(s);
                writer.Write(bytes);
            }
        }

        public static string ReadStringUTF8(this BinaryReader reader)
        {
            var len = reader.Read7BitEncodedInt();
            if (len == 0) return null;
            else
            {
                var bytes = reader.ReadBytes(len - 1);
                return Encoding.UTF8.GetString(bytes);
            }
        }

        public static void Write(this BinaryWriter writer, Matrix4x4 mat)
        {
            writer.Write(mat.M11);
            writer.Write(mat.M12); 
            writer.Write(mat.M13); 
            writer.Write(mat.M14);
            
            writer.Write(mat.M21); 
            writer.Write(mat.M22); 
            writer.Write(mat.M23); 
            writer.Write(mat.M24);
            
            writer.Write(mat.M31); 
            writer.Write(mat.M32); 
            writer.Write(mat.M33); 
            writer.Write(mat.M34);
            
            writer.Write(mat.M41);
            writer.Write(mat.M42);
            writer.Write(mat.M43);
            writer.Write(mat.M44);
        }

        public static Matrix4x4 ReadMatrix4x4(this BinaryReader reader)
        {
            return new Matrix4x4(
                reader.ReadSingle(), reader.ReadSingle(), reader.ReadSingle(), reader.ReadSingle(),
                reader.ReadSingle(), reader.ReadSingle(), reader.ReadSingle(), reader.ReadSingle(), 
                reader.ReadSingle(), reader.ReadSingle(), reader.ReadSingle(), reader.ReadSingle(), 
                reader.ReadSingle(), reader.ReadSingle(), reader.ReadSingle(), reader.ReadSingle()
                );
        }

        public static void Write(this BinaryWriter writer, Vector4 vec)
        {
            writer.Write(vec.X);
            writer.Write(vec.Y);
            writer.Write(vec.Z);
            writer.Write(vec.W);
        }
        
        public static void Write(this BinaryWriter writer, LinearColor vec)
        {
            writer.Write(vec.R);
            writer.Write(vec.G);
            writer.Write(vec.B);
            writer.Write(vec.A);
        }
        
        public static void Write(this BinaryWriter writer, Vector3 vec)
        {
            writer.Write(vec.X);
            writer.Write(vec.Y);
            writer.Write(vec.Z);
        }
        
        public static void Write(this BinaryWriter writer, Vector2 vec)
        {
            writer.Write(vec.X);
            writer.Write(vec.Y);
        }

        public static Vector4 ReadVector4(this BinaryReader reader)
        {
            return new Vector4(reader.ReadSingle(), reader.ReadSingle(), reader.ReadSingle(), reader.ReadSingle());
        }
        
        public static LinearColor ReadLinearColor(this BinaryReader reader)
        {
            return new LinearColor(reader.ReadSingle(), reader.ReadSingle(), reader.ReadSingle(), reader.ReadSingle());
        }
        
        public static Vector3 ReadVector3(this BinaryReader reader)
        {
            return new Vector3(reader.ReadSingle(), reader.ReadSingle(), reader.ReadSingle());
        }
        
        public static Vector2 ReadVector2(this BinaryReader reader)
        {
            return new Vector2(reader.ReadSingle(), reader.ReadSingle());
        }
    }
}