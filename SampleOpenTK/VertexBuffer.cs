using System;
using System.Collections.Generic;
using System.Linq;
using OpenTK.Graphics.OpenGL;
using SimpleMesh;

namespace SampleOpenTK;

/// <summary>
/// Class representing a SimpleMesh VertexArray uploaded to the GPU
/// </summary>
public class VertexBuffer : IDisposable
{
    public List<VertexArray> VertexArrays = new();
    public List<uint> Indices = new();
    public bool IsIndex32 = false;
    public int VAO;
    public int VBO;
    public int EBO;

    private int vertexCount = 0;

    public (int VertexOffset, int IndexOffset) Add(Geometry g)
    {
        var vo = vertexCount;
        var io = Indices.Count;
        VertexArrays.Add(g.Vertices);
        vertexCount += g.Vertices.Count;
        if (g.Indices.Indices32 != null)
        {
            Indices.AddRange(g.Indices.Indices32);
            IsIndex32 = true;
        }
        else
        {
            foreach (var i in g.Indices.Indices16)
                Indices.Add(i);
        }

        return (vo, io);
    }

    public void Create()
    {
        //Upload data to OpenGL
        VAO = GL.GenVertexArray();
        EBO = GL.GenBuffer();
        VBO = GL.GenBuffer();

        var merged = VertexArrays.Count == 1 ? VertexArrays[0] : VertexArray.Combine(VertexArrays);
        GL.BindVertexArray(VAO);
        GL.BindBuffer(BufferTarget.ArrayBuffer, VBO);
        GL.BufferData(BufferTarget.ArrayBuffer, merged.Buffer.Length, merged.Buffer, BufferUsageHint.StaticDraw);
        var d = merged.Descriptor;
        //position (always enabled)
        GL.EnableVertexAttribArray(0);
        GL.VertexAttribPointer(0, 3, VertexAttribPointerType.Float, false, d.Stride, 0);
        if (d.Normal > 0)
        {
            GL.EnableVertexAttribArray(1);
            GL.VertexAttribPointer(1, 3, VertexAttribPointerType.Float, false, d.Stride, d.Normal);
        }

        if (d.Diffuse > 0)
        {
            GL.EnableVertexAttribArray(2);
            GL.VertexAttribPointer(2, 4, VertexAttribPointerType.Float, false, d.Stride, d.Diffuse);
        }

        if (d.Tangent > 0)
        {
            GL.EnableVertexAttribArray(3);
            GL.VertexAttribPointer(3, 4, VertexAttribPointerType.Float, false, d.Stride, d.Tangent);
        }

        //texture1
        if (d.Texture1 > 0)
        {
            GL.EnableVertexAttribArray(4);
            GL.VertexAttribPointer(4, 2, VertexAttribPointerType.Float, false, d.Stride, d.Texture1);
        }

        //texture2
        if (d.Texture2 > 0)
        {
            GL.EnableVertexAttribArray(5);
            GL.VertexAttribPointer(5, 2, VertexAttribPointerType.Float, false, d.Stride, d.Texture2);
        }

        //texture3
        if (d.Texture3 > 0)
        {
            GL.EnableVertexAttribArray(6);
            GL.VertexAttribPointer(6, 2, VertexAttribPointerType.Float, false, d.Stride, d.Texture3);
        }

        //texture4
        if (d.Texture4 > 0)
        {
            GL.EnableVertexAttribArray(7);
            GL.VertexAttribPointer(7, 2, VertexAttribPointerType.Float, false, d.Stride, d.Texture4);
        }

        //joints
        if (d.JointIndices > 0)
        {
            GL.EnableVertexAttribArray(8);
            GL.VertexAttribIPointer(8, 4, VertexAttribIntegerType.UnsignedShort, d.Stride, d.JointIndices);
            GL.EnableVertexAttribArray(9);
            GL.VertexAttribPointer(9, 4, VertexAttribPointerType.Float, false, d.Stride, d.JointWeights);
        }

        GL.BindBuffer(BufferTarget.ElementArrayBuffer, EBO);
        if (IsIndex32)
        {
            var idx32 = Indices.ToArray();
            GL.BufferData(BufferTarget.ElementArrayBuffer, sizeof(uint) * idx32.Length, idx32,
                BufferUsageHint.StaticDraw);
        }
        else
        {
            var idx16 = Indices.Select(x => (ushort)x).ToArray();
            GL.BufferData(BufferTarget.ElementArrayBuffer, sizeof(ushort) * idx16.Length, idx16,
                BufferUsageHint.StaticDraw);
        }

        GL.BindVertexArray(0);
    }

    public void Dispose()
    {
        GL.DeleteVertexArray(VAO);
        GL.DeleteBuffer(EBO);
        GL.DeleteBuffer(VBO);
    }
}