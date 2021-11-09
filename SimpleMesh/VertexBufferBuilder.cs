using System.Collections.Generic;

namespace SimpleMesh
{
    class VertexBufferBuilder
    {
        public List<Vertex> Vertices = new List<Vertex>();

        public int BaseVertex { get; private set; }

        private Dictionary<Vertex, int> indices = new Dictionary<Vertex, int>();
        public void Chunk()
        {
            indices = new Dictionary<Vertex, int>();
            BaseVertex = Vertices.Count;
        }
        public int Add(ref Vertex vert)
        {
            if (!indices.TryGetValue(vert, out int idx))
            {
                idx = Vertices.Count;
                Vertices.Add(vert);
                indices.Add(vert, idx);
            }
            return idx;
        }
    }
}