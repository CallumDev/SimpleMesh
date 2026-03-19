using System;
using System.Collections.Generic;
using System.Numerics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace SimpleMesh;

/// <summary>
/// A class containing a set of vertices, only storing position + the fields referred to in their VertexAttributes
/// </summary>
public class VertexArray
{
    public readonly struct VertexDescriptor
    {
        public readonly VertexAttributes Attributes;
        public readonly int Stride;
        public readonly int Normal;
        public readonly int Diffuse;
        public readonly int Tangent;
        public readonly int Texture1;
        public readonly int Texture2;
        public readonly int Texture3;
        public readonly int Texture4;
        public readonly int JointIndices;
        public readonly int JointWeights;

        public VertexDescriptor(VertexAttributes attributes)
        {
            Attributes = attributes;
            Stride = 12;
            Normal = -1;
            if ((attributes & VertexAttributes.Normal) == VertexAttributes.Normal)
            {
                Normal = Stride;
                Stride += 12;
            }
            Diffuse = -1;
            if ((attributes & VertexAttributes.Diffuse) == VertexAttributes.Diffuse)
            {
                Diffuse = Stride;
                Stride += 16;
            }
            Tangent = -1;
            if ((attributes & VertexAttributes.Tangent) == VertexAttributes.Tangent)
            {
                Tangent = Stride;
                Stride += 16;
            }
            Texture1 = -1;
            if ((attributes & VertexAttributes.Texture1) == VertexAttributes.Texture1)
            {
                Texture1 = Stride;
                Stride += 8;
            }
            Texture2 = -1;
            if ((attributes & VertexAttributes.Texture2) == VertexAttributes.Texture2)
            {
                Texture2 = Stride;
                Stride += 8;
            }
            Texture3 = -1;
            if ((attributes & VertexAttributes.Texture3) == VertexAttributes.Texture3)
            {
                Texture3 = Stride;
                Stride += 8;
            }
            Texture4 = -1;
            if ((attributes & VertexAttributes.Texture4) == VertexAttributes.Texture4)
            {
                Texture4 = Stride;
                Stride += 8;
            }
            JointIndices = -1;
            JointWeights = -1;
            if ((attributes & VertexAttributes.Joints) == VertexAttributes.Joints)
            {
                JointIndices = Stride;
                Stride += 8;
                JointWeights = Stride;
                Stride += 16;
            }
        }
    }

    public readonly ref struct Accessor<T>(VertexArray array, int component) where T : unmanaged
    {
        public ref T this[int index] =>
            ref Reference<T>(array.Buffer, array.Descriptor.Stride, component, index);

        public int Count => array.Count;
    }

    public sealed class VertexAccessor
    {
        private VertexArray array;

        internal VertexAccessor(VertexArray array)
        {
            this.array = array;
        }
        
        public Vertex this[int index]
        {
            get
            {
                var vtx = new Vertex() { Diffuse = LinearColor.White };
                vtx.Position = array.Position[index];
                if (array.Has(VertexAttributes.Normal))
                    vtx.Normal = array.Normal[index];
                if (array.Has(VertexAttributes.Diffuse))
                    vtx.Diffuse = array.Diffuse[index];
                if (array.Has(VertexAttributes.Tangent))
                    vtx.Tangent = array.Tangent[index];
                if (array.Has(VertexAttributes.Texture1))
                    vtx.Texture1 = array.Texture1[index];
                if (array.Has(VertexAttributes.Texture2))
                    vtx.Texture2 = array.Texture2[index];
                if (array.Has(VertexAttributes.Texture3))
                    vtx.Texture3 = array.Texture3[index];
                if (array.Has(VertexAttributes.Texture4))
                    vtx.Texture4 = array.Texture4[index];
                if (array.Has(VertexAttributes.Joints))
                {
                    vtx.JointIndices = array.JointIndices[index];
                    vtx.JointWeights = array.JointWeights[index];
                }
                return vtx;
            }
            set
            {
                array.Position[index] = value.Position;
                if(array.Has(VertexAttributes.Normal))
                    array.Normal[index] = value.Normal;
                if(array.Has(VertexAttributes.Diffuse))
                    array.Diffuse[index] = value.Diffuse;
                if (array.Has(VertexAttributes.Tangent))
                    array.Tangent[index] = value.Tangent;
                if (array.Has(VertexAttributes.Texture1))
                    array.Texture1[index] = value.Texture1;
                if (array.Has(VertexAttributes.Texture2))
                    array.Texture2[index] = value.Texture2;
                if (array.Has(VertexAttributes.Texture3))
                    array.Texture3[index] = value.Texture3;
                if(array.Has(VertexAttributes.Texture4))
                    array.Texture4[index] =  value.Texture4;
                if (array.Has(VertexAttributes.Joints))
                {
                    array.JointIndices[index] = value.JointIndices;
                    array.JointWeights[index] = value.JointWeights;
                }
            }
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    bool Has(VertexAttributes attr) => (Descriptor.Attributes & attr) == attr;

    public Accessor<Vector3> Position => new(this, 0);
    public Accessor<Vector3> Normal => new(this, Descriptor.Normal);
    public Accessor<LinearColor> Diffuse => new(this, Descriptor.Diffuse);
    public Accessor<Vector4> Tangent => new(this, Descriptor.Tangent);
    public Accessor<Vector2> Texture1 => new(this, Descriptor.Texture1);
    public Accessor<Vector2> Texture2 => new(this, Descriptor.Texture2);
    public Accessor<Vector2> Texture3 => new(this, Descriptor.Texture3);
    public Accessor<Vector2> Texture4 => new(this, Descriptor.Texture4);
    public Accessor<Point4<ushort>> JointIndices => new(this, Descriptor.JointIndices);
    public Accessor<Vector4> JointWeights => new(this, Descriptor.JointWeights);
    
    public VertexAccessor Vertices { get; private set; }
    public VertexDescriptor Descriptor { get; private set; }
    public byte[] Buffer { get; private set; }
    public int Count { get; private set; }

    public void Resize(int newCount)
    {
        if (Count == newCount)
            return;
        var b = Buffer;
        Array.Resize(ref b, newCount * Descriptor.Stride);
        Buffer = b;
        Count = newCount;
    }

    public void ChangeAttributes(VertexAttributes newAttributes)
    {
        if (newAttributes == Descriptor.Attributes)
            return;
        var newArr = new VertexArray(newAttributes, Count);
        for (int i = 0; i < Count; i++)
        {
            newArr.Vertices[i] = Vertices[i];
        }
        Buffer = newArr.Buffer;
        Descriptor = newArr.Descriptor;
    }

    public VertexArray(VertexAttributes attributes, int count)
    {
        Descriptor = new(attributes);
        Count = count;
        Buffer = new byte[Count * Descriptor.Stride];
        Vertices = new(this);
    }

    internal VertexArray(VertexDescriptor descriptor, int count, byte[] buffer)
    {
        Buffer = buffer;
        Count = count;
        Descriptor = descriptor;
        Vertices = new(this);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    static unsafe ref T Reference<T>(byte[] buffer, int stride, int offset, int index) where T : unmanaged
    {
        if (offset < 0)
            throw new InvalidOperationException();
        var pos = index * stride + offset; //IndexOutOfRange handled by AsSpan()
        return ref MemoryMarshal.AsRef<T>(buffer.AsSpan(pos, sizeof(T)));
    }

    internal VertexArray Clone()
    {
        var b = new byte[Buffer.Length];
        System.Buffer.BlockCopy(Buffer, 0, b, 0, Buffer.Length);
        return new(Descriptor, Count, b);
    }

    /// <summary>
    /// Creates a new VertexArray containing the contents of all the provided VertexArrays
    /// VertexArrays must have the same attributes
    /// </summary>
    /// <param name="arrays">A list of VertexArrays to merge</param>
    /// <returns>A merged VertexArray</returns>
    /// <exception cref="InvalidOperationException">Not all VertexArrays provided have the same type</exception>
    public static VertexArray Combine(params VertexArray[] arrays) => Combine((IList<VertexArray>)arrays);

    /// <summary>
    /// Creates a new VertexArray containing the contents of all the provided VertexArrays
    /// VertexArrays must have the same attributes
    /// </summary>
    /// <param name="arrays">A list of VertexArrays to merge</param>
    /// <returns>A merged VertexArray</returns>
    /// <exception cref="InvalidOperationException">Not all VertexArrays provided have the same type</exception>
    public static VertexArray Combine(IList<VertexArray> arrays)
    {
        if (arrays.Count == 1)
            return arrays[0].Clone();
        var attr = arrays[0].Descriptor.Attributes;
        int count = 0;
        for (int i = 0; i < arrays.Count; i++)
        {
            count += arrays[i].Count;
            if (arrays[i].Descriptor.Attributes != attr)
                throw new InvalidOperationException("VertexArray.Combine can only be used on arrays of the same type.");
        }
        var dst = new VertexArray(attr, count);
        int j = 0;
        for (int i = 0; i < arrays.Count; i++)
        {
            System.Buffer.BlockCopy(arrays[i].Buffer, 0, dst.Buffer, j, arrays[i].Buffer.Length);
            j += arrays[i].Buffer.Length;
        }
        return dst;
    }
}