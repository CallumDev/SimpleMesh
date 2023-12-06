using System;
using System.IO;
using SimpleMesh.Util;

namespace SimpleMesh.Formats.SMesh
{
    //Class for encoding indices in a smaller format
    //This format is also much kinder to deflate for compression and produces much smaller output.
    static class IndexCoding
    {
        //32-bit index coding format
        
        //top 2 bits of each byte indicate type
        //0 = cache +/- 1 
        //1 = negative value
        //2 = positive value
        
        // TYPE 2
        //3rd bit = sign
        //4+5th bit = 1-4 extra bytes
        public static void Encode32(uint[] indices, BinaryWriter writer)
        {
            writer.Write7BitEncodedInt(indices.Length);
            if (indices.Length == 0)
                return;
            writer.Write(indices[0]);
            var buffer = new CircularBuffer<uint>(16);
            buffer.Enqueue(indices[0]);
            for (int i = 1; i < indices.Length; i++)
            {
                long idx = -1, dist = 0;
                for (int j = 0; j < buffer.Count; j++)
                {
                    dist = indices[i] - buffer[j];
                    if (dist >= -2 && dist <= 1)
                    {
                        idx = j;
                        break;
                    }
                }
                if (idx != -1)
                {
                    //0 extra bytes (1 total, 75% decrease)
                    var b = (idx << 2) | (dist + 2);
                    writer.Write((byte) b);
                }
                else
                {
                    dist = indices[i] - indices[i - 1];
                    var absDist = Math.Abs(dist) - 2; //+-2 handled by cache
                    if (absDist <= 0x3f)
                    {
                        //0 extra bytes (1 total, 75% decrease)
                        if (dist < 0)
                            writer.Write((byte) (absDist & 0x3f | 0x40));
                        else
                            writer.Write((byte) (absDist & 0x3f | 0x80));
                    }
                    else
                    {
                        var code = dist > 0 ? (byte) 0xE0 : (byte) 0xC0;
                        if (absDist <= 0x7FF) {
                            code |= (byte) ((absDist >> 8) & 0x7);
                            //1 extra byte (2 total, 50% decrease)
                            writer.Write(code);
                            writer.Write((byte) (absDist & 0xFF));
                        }
                        else if (absDist <= 0x7FFFF) {
                            //2 extra bytes (3 total, 25% decrease)
                            code |= (1 << 3);
                            code |= (byte)((absDist >> 16) & 0x7);
                            writer.Write(code);
                            writer.Write((byte)(absDist >> 8 & 0xFF));
                            writer.Write((byte)(absDist & 0xFF));
                        }
                        else if (absDist <= 0x7FFFFFF) {
                            //3 extra bytes (4 total, 0%)
                            code |= (2 << 3);
                            code |= (byte)((absDist >> 24) & 0x7);
                            writer.Write(code);
                            writer.Write((byte)(absDist >> 16 & 0xFF));
                            writer.Write((byte)(absDist >> 8 & 0xFF));
                            writer.Write((byte)(absDist & 0xFF));
                        }
                        else {
                            //full 4 bytes (5 total, +25% increase)
                            writer.Write((byte)(code | (3 << 3)));
                            writer.Write((uint)absDist);
                        }
                    }
                }
                buffer.Enqueue(indices[i]);
            }
        }
        
        public static uint[] Decode32(BinaryReader reader)
        {
            var indices = new uint[reader.Read7BitEncodedInt()];
            if (indices.Length == 0)
                return indices;
            indices[0] = reader.ReadUInt32();
            var buffer = new CircularBuffer<uint>(16);
            buffer.Enqueue(indices[0]);
            for (int i = 1; i < indices.Length; i++)
            {
                var code = reader.ReadByte();
                if ((code & 0xC0) == 0x00)
                {
                    var dist = (code & 0x3) - 2;
                    var idx = (code >> 2) & 0xF;
                    indices[i] = (uint) (buffer[idx] + dist);
                }
                else if ((code & 0xC0) == 0x40)
                {
                    var dist = -((code & 0x3f) + 2);
                    indices[i] = (uint) (indices[i - 1] + dist);
                }
                else if ((code & 0xC0) == 0x80)
                {
                    var dist = (code & 0x3f) + 2;
                    indices[i] = (uint) (indices[i - 1] + dist);
                }
                else
                {
                    int type = (code >> 3) & 0x3;
                    long dist = 0;
                    if (type == 0) {
                        var code2 = reader.ReadByte();
                        dist = (code & 0x7) << 8 | code2;
                    }
                    else if (type == 1) {
                        var code2 = reader.ReadByte();
                        var code3 = reader.ReadByte();
                        dist = (code & 0x7) << 16 | code2 << 8 | code3;
                    }
                    else if (type == 2) {
                        var code2 = reader.ReadByte();
                        var code3 = reader.ReadByte();
                        var code4 = reader.ReadByte();
                        dist = (code & 0x7) << 24 | code2 << 16 | code3 << 8 | code4;
                    }
                    else
                    {
                        dist = reader.ReadUInt32();
                    }
                    dist += 2;
                    if ((code & 0x20) != 0x20) dist = -dist; //sign bit
                    indices[i] = (uint) (indices[i - 1] + dist);
                }
                buffer.Enqueue(indices[i]);
            }
            return indices;
        }
        
        //16-bit index coding format
        //top 2 bits of each byte indicate type
        //0 = cache +/- 1 
        //1 = negative value
        //2 = positive value
        
        // TYPE 2
        //3rd bit = sign
        //4th bit = 1 or 2 extra bytes
        public static void Encode16(ushort[] indices, BinaryWriter writer)
        {
            writer.Write7BitEncodedInt(indices.Length);
            if (indices.Length == 0)
                return;
            writer.Write(indices[0]);
            var buffer = new CircularBuffer<ushort>(16);
            buffer.Enqueue(indices[0]);
            //type 0x0 - cache item +/- 2
            //type 0x40 - small neg up to -65
            //type 0x80 - small pos +65
            //type 0xC0 - big number
            for (int i = 1; i < indices.Length; i++)
            {
                int idx = -1, dist = 0;
                for (int j = 0; j < buffer.Count; j++)
                {
                    dist = indices[i] - buffer[j];
                    if (dist >= -2 && dist <= 1)
                    {
                        idx = j;
                        break;
                    }
                }

                if (idx != -1)
                {
                    var b = (idx << 2) | (dist + 2);
                    writer.Write((byte) b);
                }
                else
                {
                    dist = indices[i] - indices[i - 1];
                    var absDist = Math.Abs(dist) - 2; //+-2 handled by cache
                    if (absDist <= 0x3f)
                    {
                        if (dist < 0)
                            writer.Write((byte) (absDist & 0x3f | 0x40));
                        else
                            writer.Write((byte) (absDist & 0x3f | 0x80));
                    }
                    else
                    {
                        var code = dist > 0 ? (byte) 0xE0 : (byte) 0xC0;
                        if (absDist <= 0xFFF)
                        {
                            code |= (byte) ((absDist >> 8) & 0xF);
                            writer.Write(code);
                            writer.Write((byte) (absDist & 0xFF));
                        }
                        else
                        {
                            writer.Write((byte)(code | 0x10));
                            writer.Write((ushort) absDist);
                        }
                    }
                }
                buffer.Enqueue(indices[i]);
            }
        }
        
        public static ushort[] Decode16(BinaryReader reader)
        {
            var indices = new ushort[reader.Read7BitEncodedInt()];
            if (indices.Length == 0)
                return indices;
            indices[0] = reader.ReadUInt16();
            var buffer = new CircularBuffer<ushort>(16);
            buffer.Enqueue(indices[0]);
            for (int i = 1; i < indices.Length; i++)
            {
                var code = reader.ReadByte();
                if ((code & 0xC0) == 0x00)
                {
                    var dist = (code & 0x3) - 2;
                    var idx = (code >> 2) & 0xF;
                    indices[i] = (ushort) (buffer[idx] + dist);
                }
                else if ((code & 0xC0) == 0x40)
                {
                    var dist = -((code & 0x3f) + 2);
                    indices[i] = (ushort) (indices[i - 1] + dist);
                }
                else if ((code & 0xC0) == 0x80)
                {
                    var dist = (code & 0x3f) + 2;
                    indices[i] = (ushort) (indices[i - 1] + dist);
                }
                else
                {
                    int dist = 0;
                    if ((code & 0x10) != 0x10)
                    {
                        var code2 = reader.ReadByte();
                        dist = (code & 0xF) << 8 | code2;
                    }
                    else
                    {
                        dist = reader.ReadUInt16();
                    }
                    dist += 2;
                    if ((code & 0x20) != 0x20) dist = -dist; //sign bit
                    indices[i] = (ushort) (indices[i - 1] + dist);
                }
                buffer.Enqueue(indices[i]);
            }
            return indices;
        }
        
    }
}