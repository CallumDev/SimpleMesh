using System;
using System.Globalization;
using System.Linq;
using System.Numerics;

namespace SimpleMesh
{
    public struct PropertyValue
    {
        public object Value { get; }
        public PropertyValue(string value) => Value = value;
        public PropertyValue(bool value) => Value = value;
        public PropertyValue(int value) => Value = value;
        public PropertyValue(float value) => Value = value;
        public PropertyValue(int[] value) => Value = value;
        public PropertyValue(float[] value) => Value = value;
        public PropertyValue(Vector3 value) => Value = value;

        public override string ToString() => Value.ToString();

        public bool AsString(out string v)
        {
            v = ToString();
            return true;
        }

        public bool AsBoolean()
        {
            switch (Value)
            {
                case float f:
                    return f != 0;
                case int i:
                    return i != 0;
                case bool b:
                    return b;
                case float[]:
                case int[]:
                case Vector3:
                    return true;
                case string s:
                    return !string.IsNullOrWhiteSpace(s) &&
                           !s.Trim().Equals("false", StringComparison.OrdinalIgnoreCase) &&
                           !s.Trim().Equals("no", StringComparison.OrdinalIgnoreCase);
                default:
                    return false;
            }
        }
        
        public bool AsInt32(out int v)
        {
            v = 0;
            switch (Value)
            {
                case float f:
                    v = (int)f;
                    return true;
                case bool b:
                    v = b ? 1 : 0;
                    return true;
                case int i:
                    v = i;
                    return true;
                case float[] fa:
                    v = (int)fa[0];
                    return true;
                case int[] ia:
                    v = ia[0];
                    return true;
                case Vector3 v3:
                    v = (int) v3.X;
                    return true;
                case string s:
                    return int.TryParse(s, NumberStyles.Integer ,CultureInfo.InvariantCulture, out v);
                default:
                    return false;
            }
        }

        public bool AsSingle(out float v)
        {
            v = 0;
            switch (Value)
            {
                case float f:
                    v = f;
                    return true;
                case int i:
                    v = i;
                    return true;
                case bool b:
                    v = b ? 1 : 0;
                    return true;
                case float[] fa:
                    v = fa[0];
                    return true;
                case int[] ia:
                    v = ia[0];
                    return true;
                case Vector3 v3:
                    v = v3.X;
                    return true;
                case string s:
                    return float.TryParse(s, NumberStyles.Float ,CultureInfo.InvariantCulture, out v);
                default:
                    return false;
            }
        }

        public bool AsIntArray(out int[] v)
        {
            v = null;
            switch (Value)
            {
                case float f:
                    v = new[] {(int) f};
                    return true;
                case int i:
                    v = new[] {i};
                    return true;
                case int[] ia:
                    v = ia;
                    return true;
                case float[] fa:
                    v = fa.Select(x => (int) x).ToArray();
                    return true;
                case Vector3 v3:
                    v = new[] {(int) v3.X, (int) v3.Y, (int) v3.Z};
                    return true;
                default:
                    return false;
            }
        }

        public bool AsFloatArray(out float[] v)
        {
            v = null;
            switch (Value)
            {
                case float f:
                    v = new[] { f};
                    return true;
                case int i:
                    v = new[] { (float)i };
                    return true;
                case int[] ia:
                    v = ia.Select(x => (float) x).ToArray();
                    return true;
                case float[] fa:
                    v = fa;
                    return true;
                case Vector3 v3:
                    v = new[] {v3.X, v3.Y, v3.Z};
                    return true;
                default:
                    return false;
            }
        }

        public bool AsVector3(out Vector3 v)
        {
            if (Value is Vector3 vf) {
                v = vf;
                return true;
            }
            if (AsFloatArray(out var floats) && floats.Length >= 3) {
                v = new Vector3(floats[0], floats[1], floats[2]);
                return true;
            }
            v = Vector3.Zero;
            return false;
        }
    }
}