using System;
using System.Collections.Generic;
using System.Linq;

namespace SimpleMesh.Passes
{
    static class MergeTriangleGroups
    {
        public static void Apply(Predicate<Material> canMerge, Geometry g)
        {
            //check if necessary
            HashSet<string> matNames = new HashSet<string>();
            bool required = false;
            foreach (var tg in g.Groups)
            {
                if (canMerge != null && !canMerge(tg.Material)) continue;
                if (matNames.Contains(tg.Material.Name))
                {
                    required = true;
                    break;
                }
                matNames.Add(tg.Material.Name);
            }
            if (!required) return;
            //group by material
            List<TriangleGroupList> byMaterial = new List<TriangleGroupList>();
            foreach (var tg in g.Groups)
            {
                var m = byMaterial.FirstOrDefault(x => x.Material == tg.Material);
                if (m == null)
                {
                    m = new TriangleGroupList() { Material = tg.Material };
                    byMaterial.Add(m);
                }
                m.Groups.Add(tg);
            }

            //Build new index buffer
            List<uint> newIndices = new List<uint>();
            List<TriangleGroup> newGroups = new List<TriangleGroup>();
            //Copy verbatim
            void CopyGroup(TriangleGroup src)
            {
                newGroups.Add(new TriangleGroup()
                {
                    BaseVertex = src.BaseVertex, StartIndex = newIndices.Count,
                    IndexCount = src.IndexCount, Material = src.Material
                });
                for (int i = 0; i < src.IndexCount; i++)
                {
                    if(g.Indices.Indices32 != null)
                        newIndices.Add(g.Indices.Indices32[src.StartIndex + i]);
                    else
                        newIndices.Add(g.Indices.Indices16[src.StartIndex + i]);    
                }
            }
            
            foreach (var m in byMaterial)
            {
                //copy those that don't have merge applied
                if (m.Groups.Count == 1 || (canMerge != null && !canMerge(m.Material)))
                {
                    foreach(var tg in m.Groups) CopyGroup(tg);
                    continue;
                }

                int baseVertex = int.MaxValue;
                foreach (var tg in m.Groups)
                    baseVertex = Math.Min(tg.BaseVertex, baseVertex);
                uint maxVal = g.Indices.Indices16 != null ? ushort.MaxValue : uint.MaxValue;
                bool possible = true;
                int indexCount = 0;
                //Check that merging doesn't require us to change index format
                foreach (var tg in m.Groups)
                {
                    indexCount += tg.IndexCount;
                    for (int i = 0; i < tg.IndexCount; i++)
                    {
                        var sample = g.Indices.Indices16 != null
                            ? (uint) g.Indices.Indices16[tg.StartIndex + i]
                            : g.Indices.Indices32[tg.StartIndex + i];
                        var newIndex = (sample + tg.BaseVertex) - baseVertex;
                        if (newIndex > maxVal)
                        {
                            possible = false;
                            break;
                        }
                    }
                    if (!possible) break;
                }
                if (!possible) {
                    foreach(var tg in m.Groups) CopyGroup(tg);
                }
                else
                {
                    //merge groups
                    int startIndex = newIndices.Count;
                    foreach (var tg in m.Groups) {
                        for (int i = 0; i < tg.IndexCount; i++)
                        {
                            var sample = g.Indices.Indices16 != null
                                ? (uint) g.Indices.Indices16[tg.StartIndex + i]
                                : g.Indices.Indices32[tg.StartIndex + i];
                            var newIndex = (sample + tg.BaseVertex) - baseVertex;
                            newIndices.Add((uint)newIndex);
                        }
                    }
                    newGroups.Add(new TriangleGroup() { BaseVertex = baseVertex, IndexCount = indexCount, Material = m.Material, StartIndex = startIndex });
                }
            }
            
            g.Indices = Indices.FromBuffer(newIndices.ToArray());
            g.Groups = newGroups.ToArray();
        }
        
        class TriangleGroupList
        {
            public Material Material;
            public List<TriangleGroup> Groups = new List<TriangleGroup>();
        }
    }
}