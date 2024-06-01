using System.Numerics;

namespace SimpleMesh
{
    public record TextureInfo(string Name, int CoordinateIndex);
    
    public class Material
    {
        public string Name;
        public Vector4 DiffuseColor;
        public Vector3 EmissiveColor;
        public TextureInfo DiffuseTexture;
        public TextureInfo EmissiveTexture;
        public TextureInfo NormalTexture;
        public bool MetallicRoughness;
        public float MetallicFactor;
        public float RoughnessFactor;
        public TextureInfo MetallicRoughnessTexture;
        

        internal Material Clone() => new()
        {
            Name = Name,
            DiffuseColor = DiffuseColor,
            DiffuseTexture = DiffuseTexture,
            NormalTexture = NormalTexture,
            EmissiveColor = EmissiveColor,
            EmissiveTexture =  EmissiveTexture,
            MetallicRoughness = MetallicRoughness,
            MetallicFactor =  MetallicFactor,
            RoughnessFactor =  RoughnessFactor,
            MetallicRoughnessTexture = MetallicRoughnessTexture,
        };
    }
}