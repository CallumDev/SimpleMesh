using System.Collections.Generic;
using System.Numerics;
using System.Runtime.CompilerServices;

namespace SimpleMesh
{
    class VertexArrayBuilder
    {
        private IBuilderImpl impl;

        public int BaseVertex => impl.BaseVertex;

        public int Add(ref Vertex vert) => impl.Add(ref vert);
        
        public void Chunk() => impl.Chunk();
        
        public VertexArray Finish() => impl.Finish();
        
        public VertexAttributes Attributes { get; private set; }

        public VertexArrayBuilder(VertexAttributes attributes)
        {
            Attributes = attributes;
            impl = Attributes switch
            {
                VertexAttributes.Position => new PositionImpl(),
                VertexAttributes.Position | VertexAttributes.Normal => new PositionNormalImpl(),
                VertexAttributes.Position | VertexAttributes.Normal | VertexAttributes.Texture1 => new PositionNormalTex1Impl(),
                _ => new FullImpl(Attributes)
            };
        }

        class PositionImpl() : BuilderImpl<Vector3>(0)
        {
            protected override Vector3 FromVertex(ref Vertex vert) => vert.Position;

            protected override Vertex FromT(Vector3 t) =>
                new Vertex() { Position = t, Diffuse = LinearColor.White };
        }
        
        record struct PositionNormal(Vector3 Position, Vector3 Normal);

        class PositionNormalImpl() : 
            BuilderImpl<PositionNormal>(VertexAttributes.Position | VertexAttributes.Normal)
        {
            protected override PositionNormal FromVertex(ref Vertex vert) =>
                new(vert.Position, vert.Normal);

            protected override Vertex FromT(PositionNormal t) =>
                new() { Position = t.Position, Normal = t.Normal, Diffuse = LinearColor.White };
        }

        record struct PositionNormalTex1(Vector3 Position, Vector3 Normal, Vector2 Texture1);

        class PositionNormalTex1Impl(): 
            BuilderImpl<PositionNormalTex1>(VertexAttributes.Normal | VertexAttributes.Texture1)
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

        class FullImpl(VertexAttributes attributes) : BuilderImpl<Vertex>(attributes)
        {
            protected override Vertex FromVertex(ref Vertex vert) => vert;
            protected override Vertex FromT(Vertex vert) => vert;
        }

        interface IBuilderImpl
        {
            void Chunk();
            int Add(ref Vertex vert);
            VertexArray Finish();
            int BaseVertex { get; }
        }
        abstract class BuilderImpl<T> : IBuilderImpl where T : notnull
        {
            Dictionary<T, int> indices = new Dictionary<T, int>();
            private VertexArray array;
            private int vertexCount = 0;
            
            public int BaseVertex { get; private set; }

            protected BuilderImpl(VertexAttributes attributes)
            {
                array = new(attributes, 64);
            }

            public void Chunk()
            {
                indices = new Dictionary<T, int>();
                BaseVertex = array.Count;
            }
            
            public int Add(ref Vertex vert)
            {
                var v = FromVertex(ref vert);
                if (!indices.TryGetValue(v, out int idx))
                {
                    if (vertexCount + 1 >= array.Count) {
                        array.Resize(array.Count * 2);
                    }
                    idx = vertexCount;
                    array.Vertices[idx] = vert;
                    vertexCount++;
                    indices.Add(v, idx);
                }
                return idx;
            }

            public VertexArray Finish()
            {
                array.Resize(vertexCount);
                return array;
            }
            
            protected abstract T FromVertex(ref Vertex vert);
            protected abstract Vertex FromT(T t);
        }
    }
    
}