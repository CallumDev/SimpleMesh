using System.Numerics;

namespace SimpleMesh
{
    public class Material
    {
        public string Name;
        public Vector4 DiffuseColor;
        public string DiffuseTexture;

        internal Material Clone() => new()
        {
            Name = Name,
            DiffuseColor = DiffuseColor,
            DiffuseTexture = DiffuseTexture
        };
    }
}