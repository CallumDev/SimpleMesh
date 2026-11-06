using System;
using System.Collections.Generic;
using System.Numerics;

namespace SimpleMesh
{
    /// <summary>
    /// Progressively builds a Geometry class from provided vertices and group data.
    /// </summary>
    public partial class GeometryBuilder
    {
        public VertexAttributes Attributes { get; private set; }
        public int IndexCount => indices.Count;

        private IBuilderImpl impl;
        private List<uint> indices = new();
        private List<TriangleGroup> groups = new();
        private int startIndex = 0;
        private bool finished = false;

        private bool fastAdd = true;
        private HashSet<int> hashes = new();
        private Dictionary<uint, uint> remapped = new();

        /// <summary>
        /// Adds unindexed vertex data, building an index buffer.
        /// </summary>
        public void Add(ref Vertex vert)
        {
            if (finished)
                throw new InvalidOperationException("GeometryBuilder already finished.");
            indices.Add((uint)(impl.Add(ref vert) - impl.BaseVertex));
        }

        /// <summary>
        /// Adds data using a source index as a cache key, remapping the index buffer.
        /// Optimistically assumes vertex data is not duplicated
        /// </summary>
        public void AddIndexed(uint srcIndex, ref Vertex vert)
        {
            if (finished)
                throw new InvalidOperationException("GeometryBuilder already finished.");
            indices.Add(AddFast(srcIndex, ref vert));
        }

        uint AddFast(uint srcIndex, ref Vertex vert)
        {
            if (!remapped.TryGetValue(srcIndex, out var mapped))
            {
                if (fastAdd)
                {
                    if (!hashes.Add(vert.GetHashCode()))
                    {
                        // potential of needing to dedup, switch to slow path
                        fastAdd = false;
                        impl.BuildIndexLookupFromDirect();
                        mapped = (uint)(impl.Add(ref vert) - impl.BaseVertex);
                        remapped[srcIndex] = mapped;
                    }
                    else
                    {
                        // no dedup needed yet, add to array directly.
                        mapped = (uint)(impl.VertexCount - impl.BaseVertex);
                        impl.AddDirect(ref vert);
                        remapped[srcIndex] = mapped;
                    }
                }
                else
                {
                    mapped = (uint)(impl.Add(ref vert) - impl.BaseVertex);
                    remapped[srcIndex] = mapped;
                }
            }

            return mapped;
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
            fastAdd = true;
            hashes = new();
            remapped = new();
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
            impl = CreateImpl(attributes);
        }


        class PositionImpl() : BuilderImpl<Vector3>(0)
        {
            protected override Vector3 FromVertex(ref Vertex vert) => vert.Position;
        }

        class FullImpl(VertexAttributes attributes) : BuilderImpl<Vertex>(attributes)
        {
            protected override Vertex FromVertex(ref Vertex vert) => vert;
        }

        interface IBuilderImpl
        {
            void AddDirect(ref Vertex vert);
            void BuildIndexLookupFromDirect();
            void Chunk();
            int Add(ref Vertex vert);
            VertexArray Finish();
            int BaseVertex { get; }
            int VertexCount { get; }
        }

        abstract class BuilderImpl<T> : IBuilderImpl where T : notnull
        {
            Dictionary<T, int> indices = new Dictionary<T, int>();
            private VertexArray array;
            private int vertexCount = 0;

            public int BaseVertex { get; private set; }
            public int VertexCount => vertexCount;

            protected BuilderImpl(VertexAttributes attributes)
            {
                array = new(attributes, 64);
            }

            public void Chunk()
            {
                indices = new Dictionary<T, int>();
                BaseVertex = vertexCount;
            }

            public void AddDirect(ref Vertex vert)
            {
                if (vertexCount + 1 >= array.Count) {
                    array.Resize(array.Count * 2);
                }
                array.Vertices[vertexCount++] = vert;
            }

            public void BuildIndexLookupFromDirect()
            {
                for (int i = BaseVertex; i < vertexCount; i++)
                {
                    var v = array.Vertices[i];
                    var lookup = FromVertex(ref v);
                    indices[lookup] = i;
                }
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
