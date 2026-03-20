using System;
using System.Collections.Generic;

namespace SimpleMesh
{
    public class TriangleGroup(Material material)
    {
        public int StartIndex;
        public int BaseVertex;
        public int IndexCount;
        public Material Material = material;

        internal TriangleGroup Clone(Model model)
        {
            if (!model.Materials.TryGetValue(Material.Name, out var material))
                throw new KeyNotFoundException("Matching material not found");
            return new TriangleGroup(material)
            {
                StartIndex = StartIndex,
                BaseVertex = BaseVertex,
                IndexCount = IndexCount,
            };
        }
    }
}