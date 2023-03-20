using System.Linq;

namespace SimpleMesh
{
    public class Indices
    {
        public uint[] Indices32;
        public ushort[] Indices16;

        public static Indices FromBuffer(uint[] source)
        {
            bool is16 = true;
            for (int i = 0; i < source.Length; i++) {
                if (source[i] > 65535)
                {
                    is16 = false;
                    break;
                }      
            }
            if (is16) return new Indices() {Indices16 = source.Select(x => (ushort) x).ToArray()};
            return new Indices() {Indices32 = source};
        }

        public int Length => Indices32?.Length ?? Indices16.Length;
        
        internal Indices Clone()
        {
            if (Indices32 != null) return new Indices() {Indices32 = Indices32.ToArray()};
            else if (Indices16 != null) return new Indices() {Indices16 = Indices16.ToArray()};
            else return new Indices();
        }
    }
}