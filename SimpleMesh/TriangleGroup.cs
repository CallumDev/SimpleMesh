namespace SimpleMesh
{
    public class TriangleGroup
    {
        public int StartIndex;
        public int BaseVertex;
        public int IndexCount;
        public Material Material;

        internal TriangleGroup Clone(Model model)
        {
            Material m = null;
            if (Material != null)
                model.Materials.TryGetValue(Material.Name, out m);
            return new TriangleGroup()
            {
                StartIndex = StartIndex,
                BaseVertex = BaseVertex,
                IndexCount = IndexCount,
                Material = m,
            };
        }
    }
}