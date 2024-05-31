using System.Numerics;

namespace SimpleMesh
{
    public class Material
    {
        public string Name;
        public Vector4 DiffuseColor;
        public Vector3 EmissiveColor;
        public string DiffuseTexture;
        public string EmissiveTexture;

        internal Material Clone() => new()
        {
            Name = Name,
            DiffuseColor = DiffuseColor,
            DiffuseTexture = DiffuseTexture,
            EmissiveColor = EmissiveColor,
            EmissiveTexture =  EmissiveTexture
        };
    }
}