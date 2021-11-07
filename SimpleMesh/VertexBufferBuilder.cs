using System.Collections.Generic;

namespace SimpleMesh
{
    class VertexBufferBuilder
    {
        public List<int> Hashes = new List<int>();
        public List<Vertex> Vertices = new List<Vertex>();

        public int Add(ref Vertex vert, int startIndex)
        {
            var hash = HashVert(ref vert);
            int idx = FindDuplicate(startIndex, ref vert, hash);
            if (idx == -1)
            {
                idx = Vertices.Count;
                Vertices.Add(vert);
                Hashes.Add(hash);
            }
            return idx;
        }
        
        int FindDuplicate(int startIndex, ref Vertex search, int hash)
        {
            for (int i = startIndex; i < Vertices.Count; i++) {
                if (Hashes[i] != hash) continue;
                if (Vertices[i].Position != search.Position) continue;
                if (Vertices[i].Normal != search.Normal) continue;
                if (Vertices[i].Texture1 != search.Texture1) continue;
                if (Vertices[i].Diffuse != search.Diffuse) continue;
                if (Vertices[i].Texture2 != search.Texture2) continue;
                return i;
            }
            return -1;
        }
        
        static int HashVert(ref Vertex vert)
        {
            unchecked {
                int hash = (int)2166136261;
                hash = hash * 16777619 ^ vert.Position.GetHashCode();
                hash = hash * 16777619 ^ vert.Normal.GetHashCode();
                hash = hash * 16777619 ^ vert.Texture1.GetHashCode();
                hash = hash * 16777619 ^ vert.Texture2.GetHashCode();
                hash = hash * 16777619 ^ vert.Diffuse.GetHashCode();
                return hash;
            }
        }
    }
}