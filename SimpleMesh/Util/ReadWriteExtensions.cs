using System.IO;
using System.Numerics;
using System.Text;

namespace SimpleMesh.Util
{
    static class ReadWriteExtensions
    {
        public static void WriteStringUTF8(this BinaryWriter writer, string s)
        {
            if (s == null)
            {
                writer.WriteVarUInt32(0);
            }
            else
            {
                writer.WriteVarUInt32((uint)(s.Length + 1));
                var bytes = Encoding.UTF8.GetBytes(s);
                writer.Write(bytes);
            }
        }

        public static string? ReadStringUTF8(this BinaryReader reader)
        {
            var len = (int)reader.ReadVarUInt32();
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

        public static LinearColor ReadLinearColor(this BinaryReader reader)
        {
            return new LinearColor(reader.ReadSingle(), reader.ReadSingle(), reader.ReadSingle(), reader.ReadSingle());
        }

        public static Vector3 ReadVector3(this BinaryReader reader)
        {
            return new Vector3(reader.ReadSingle(), reader.ReadSingle(), reader.ReadSingle());
        }

        public static void WriteVarUInt32(this BinaryWriter writer, uint u)
        {
            if (u <= 127)
            {
                writer.Write((byte)u);
            }
            else if (u <= 16511)
            {
                u -= 128;
                writer.Write((byte)((u & 0x7f) | 0x80));
                writer.Write((byte)((u >> 7) & 0x7f));
            }
            else if (u <= 2113662)
            {
                u -= 16512;
                writer.Write((byte)((u & 0x7f) | 0x80));
                writer.Write((byte)(((u >> 7) & 0x7f) | 0x80));
                writer.Write((byte)((u >> 14) & 0x7f));
            }
            else if (u <= 270549118)
            {
                u -= 2113663;
                writer.Write((byte)((u & 0x7f) | 0x80));
                writer.Write((byte)(((u >> 7) & 0x7f) | 0x80));
                writer.Write((byte)(((u >> 14) & 0x7f) | 0x80));
                writer.Write((byte)((u >> 21) & 0x7f));
            }
            else
            {
                u -= 270549119;
                writer.Write((byte)((u & 0x7f) | 0x80));
                writer.Write((byte)(((u >> 7) & 0x7f) | 0x80));
                writer.Write((byte)(((u >> 14) & 0x7f) | 0x80));
                writer.Write((byte)(((u >> 21) & 0x7f) | 0x80));
                writer.Write((byte)((u >> 28) & 0x7f));
            }
        }

        public static uint ReadVarUInt32(this BinaryReader reader)
        {
            long b = reader.ReadByte();
            var a = (uint)(b & 0x7f);
            var extraCount = 0;
            //first extra
            if ((b & 0x80) == 0x80)
            {
                b = reader.ReadByte();
                a |= (uint)((b & 0x7f) << 7);
                extraCount++;
            }

            //second extra
            if ((b & 0x80) == 0x80)
            {
                b = reader.ReadByte();
                a |= (uint)((b & 0x7f) << 14);
                extraCount++;
            }

            //third extra
            if ((b & 0x80) == 0x80)
            {
                b = reader.ReadByte();
                a |= (uint)((b & 0x7f) << 21);
                extraCount++;
            }

            //fourth extra
            if ((b & 0x80) == 0x80)
            {
                b = reader.ReadByte();
                a |= (uint)((b & 0x7f) << 28);
                extraCount++;
            }

            switch (extraCount)
            {
                case 1:
                    a += 128;
                    break;
                case 2:
                    a += 16512;
                    break;
                case 3:
                    a += 2113663;
                    break;
                case 4:
                    a += 270549119;
                    break;
            }
            return a;
        }
    }
}