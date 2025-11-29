using System.Collections.Generic;
using System.Numerics;

namespace SimpleMesh
{
    class VertexBufferBuilder
    {
        private IBuilderImpl impl;

        public int BaseVertex => impl.BaseVertex;

        public int Add(ref Vertex vert) => impl.Add(ref vert);
        
        public void Chunk() => impl.Chunk();
        
        public Vertex[] GetVertices() => impl.GetVertices();
        
        public VertexAttributes Attributes { get; private set; }

        public VertexBufferBuilder(VertexAttributes attributes)
        {
            Attributes = attributes;
            impl = Attributes switch
            {
                VertexAttributes.Position => new PositionImpl(),
                VertexAttributes.Position | VertexAttributes.Normal => new PositionNormalImpl(),
                VertexAttributes.Position | VertexAttributes.Normal | VertexAttributes.Texture1 => new PositionNormalTex1Impl(),
                _ => new FullImpl()
            };
        }

        class PositionImpl : BuilderImpl<Vector3>
        {
            protected override Vector3 FromVertex(ref Vertex vert) => vert.Position;

            protected override Vertex FromT(Vector3 t) =>
                new Vertex() { Position = t, Diffuse = LinearColor.White };
        }
        
        record struct PositionNormal(Vector3 Position, Vector3 Normal);

        class PositionNormalImpl : BuilderImpl<PositionNormal>
        {
            protected override PositionNormal FromVertex(ref Vertex vert) =>
                new(vert.Position, vert.Normal);

            protected override Vertex FromT(PositionNormal t) =>
                new() { Position = t.Position, Normal = t.Normal, Diffuse = LinearColor.White };
        }

        record struct PositionNormalTex1(Vector3 Position, Vector3 Normal, Vector2 Texture1);

        class PositionNormalTex1Impl : BuilderImpl<PositionNormalTex1>
        {
            protected override PositionNormalTex1 FromVertex(ref Vertex vert) =>
                new(vert.Position, vert.Normal, vert.Texture1);

            protected override Vertex FromT(PositionNormalTex1 t) =>
                new()
                {
                    Position = t.Position, 
                    Normal = t.Normal, 
                    Diffuse = LinearColor.White,
                    Texture1 = t.Texture1
                };
        }

        class FullImpl : BuilderImpl<Vertex>
        {
            protected override Vertex FromVertex(ref Vertex vert) => vert;
            protected override Vertex FromT(Vertex vert) => vert;
        }

        interface IBuilderImpl
        {
            void Chunk();
            int Add(ref Vertex vert);
            Vertex[] GetVertices();
            int BaseVertex { get; }
        }
        abstract class BuilderImpl<T> : IBuilderImpl
        {
            Dictionary<T, int> indices = new Dictionary<T, int>();
            List<T> vertices = new();
            public int BaseVertex { get; private set; }

            public void Chunk()
            {
                indices = new Dictionary<T, int>();
                BaseVertex = vertices.Count;
            }
            
            public int Add(ref Vertex vert)
            {
                var v = FromVertex(ref vert);
                if (!indices.TryGetValue(v, out int idx))
                {
                    idx = vertices.Count;
                    vertices.Add(v);
                    indices.Add(v, idx);
                }
                return idx;
            }

            public Vertex[] GetVertices()
            {
                var v = new Vertex[vertices.Count];
                for (int i = 0; i < v.Length; i++)
                {
                    v[i] = FromT(vertices[i]);
                }
                return v;
            }
            protected abstract T FromVertex(ref Vertex vert);
            protected abstract Vertex FromT(T t);
        }
    }
    
}