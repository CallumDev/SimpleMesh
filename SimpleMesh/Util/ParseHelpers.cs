using System;
using System.Globalization;
using System.Linq;
using System.Numerics;

namespace SimpleMesh.Util
{
    static class ParseHelpers
    {
        public static string[] Tokens(string s) => s.Split((char[])null, StringSplitOptions.RemoveEmptyEntries);
        public static float[] FloatArray(string s) => Tokens(s).Select((x) => float.Parse(x, CultureInfo.InvariantCulture)).ToArray();
        public static int[] IntArray(string s) => Tokens(s).Select((x) => int.Parse(x, CultureInfo.InvariantCulture)).ToArray();
        
        public static bool TryParseColor(string s, out Vector4 col)
        {
            col = Vector4.One;
            try
            {
                var floats = FloatArray(s);
                if (floats.Length != 3 && floats.Length != 4) return false;
                col.X= floats[0];
                col.Y = floats[1];
                col.Z = floats[2];
                if (floats.Length > 3) col.W = floats[3];
                return true;
            }
            catch (Exception)
            {
                return false;
            }
        }
    }
}