using System.IO;
using System.Runtime.CompilerServices;

namespace SimpleMesh.Formats.SMesh;

class FloatBuffer
{
    private int byteChannels = 0;
    private int count;
    private byte[] buffer;
    public FloatBuffer(int channels, int count)
    {
        byteChannels = channels * 4;
        buffer = new byte[byteChannels * count];
        this.count = count;
    }

    public void Read(BinaryReader reader)
    {
        reader.BaseStream.ReadExactly(buffer);
        for (int i = 1; i < buffer.Length; i++)
        {
            buffer[i] += buffer[i - 1];
        }
    }

    public void Write(BinaryWriter writer)
    {
        writer.Write(buffer[0]);
        for (int i = 1; i < buffer.Length; i++)
        {
            var diff = buffer[i] - buffer[i - 1];
            writer.Write((byte)diff);
        }
    }

    public float this[int channel, int c]
    {
        get => GetFloat(channel, c);
        set => SetFloat(value, channel, c);
    }

    public void SetFloat(float f, int channel, int c)
    {
        var byteChannel = channel * 4;
        uint bits = Unsafe.BitCast<float, uint>(f);
        buffer[byteChannel * count + c] = (byte)bits;
        buffer[(byteChannel + 1) * count + c] = (byte)(bits >> 8);
        buffer[(byteChannel + 2) * count + c] = (byte)(bits >> 16);
        buffer[(byteChannel + 3) * count + c] = (byte)(bits >> 24);
    }
    
    public float GetFloat(int channel, int c)
    {
        var byteChannel = channel * 4;
        uint bits =
            (uint)buffer[byteChannel * count + c] |
            ((uint)buffer[(byteChannel + 1) * count + c] << 8) |
            ((uint)buffer[(byteChannel + 2) * count + c] << 16) |
            ((uint)buffer[(byteChannel + 3) * count + c] << 24);
        return Unsafe.BitCast<uint, float>(bits);
    }
}