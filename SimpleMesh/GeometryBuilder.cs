using System;
using System.Collections.Generic;
using System.Numerics;

namespace SimpleMesh
{
    /// <summary>
    /// Progressively builds a Geometry class from provided vertices and group data.
    /// </summary>
    public class GeometryBuilder
    {
        public VertexAttributes Attributes { get; private set; }
        public int IndexCount => indices.Count;

        private IBuilderImpl impl;
        private List<uint> indices = new();
        private List<TriangleGroup> groups = new();
        private int startIndex = 0;
        private bool finished = false;

        public void Add(ref Vertex vert)
        {
            if (finished)
                throw new InvalidOperationException("GeometryBuilder already finished.");
            indices.Add((uint)(impl.Add(ref vert) - impl.BaseVertex));
        }


        public void AddGroup(Material material)
        {
            if (indices.Count == startIndex)
                return; //No empty groups
            if (finished)
                throw new InvalidOperationException("GeometryBuilder already finished.");
            groups.Add(new TriangleGroup(material)
            {
                BaseVertex = impl.BaseVertex,
                StartIndex = startIndex,
                IndexCount = indices.Count - startIndex
            });
            startIndex = indices.Count;
            impl.Chunk();
        }

        public Geometry Finish()
        {
            var geo = new Geometry(impl.Finish(), Indices.FromBuffer(indices.ToArray()));
            geo.Groups = groups.ToArray();
            finished = true;
            return geo;
        }

        public GeometryBuilder(VertexAttributes attributes)
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
        }

        record struct PositionNormal(Vector3 Position, Vector3 Normal);

        class PositionNormalImpl() :
            BuilderImpl<PositionNormal>(VertexAttributes.Position | VertexAttributes.Normal)
        {
            protected override PositionNormal FromVertex(ref Vertex vert) =>
                new(vert.Position, vert.Normal);
        }

        record struct PositionNormalTex1(Vector3 Position, Vector3 Normal, Vector2 Texture1);

        class PositionNormalTex1Impl():
            BuilderImpl<PositionNormalTex1>(VertexAttributes.Normal | VertexAttributes.Texture1)
        {
            protected override PositionNormalTex1 FromVertex(ref Vertex vert) =>
                new(vert.Position, vert.Normal, vert.Texture1);
        }

        class FullImpl(VertexAttributes attributes) : BuilderImpl<Vertex>(attributes)
        {
            protected override Vertex FromVertex(ref Vertex vert) => vert;
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
                BaseVertex = vertexCount;
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
        }
    }

}
