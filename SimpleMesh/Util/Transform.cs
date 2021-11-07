using System;
using System.Numerics;

namespace SimpleMesh.Util
{
    static class Transform
    {
        public static Matrix4x4 ToYUp(UpAxis ax, Matrix4x4 input)
        {
            if (ax == UpAxis.XUp) throw new NotImplementedException("X UP");
            if (ax == UpAxis.ZUp)
            {
                var translation = input.Translation;
                translation = new Vector3(translation.X, translation.Z, translation.Y) * new Vector3(1, 1, -1);
                Matrix4x4.Decompose(input, out _, out Quaternion rotq, out _);
                var rot = Matrix4x4.CreateFromQuaternion(
                    new Quaternion(rotq.X,rotq.Z, -rotq.Y, rotq.W)
                );
                return rot * Matrix4x4.CreateTranslation(translation);
            }
            return input;
        }

        public static Vector3 ToYUp(UpAxis ax, Vector3 input)
        {
            if (ax == UpAxis.XUp) throw new NotImplementedException("X UP");
            if (ax == UpAxis.ZUp)
            {
                return new Vector3(input.X, input.Z, input.Y) * new Vector3(1, 1, -1);
            }
            return input;
        }
        
    }
}