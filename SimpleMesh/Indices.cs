using System.Linq;

namespace SimpleMesh
{
    /// <summary>
    /// Represents either a 16-bit or 32-bit index array
    /// </summary>
    public class Indices
    {
        public uint[]? Indices32;
        public ushort[]? Indices16;

        public Indices(ushort[] indices16)
        {
            Indices16 = indices16;
        }
        public Indices(uint[] indices32)
        {
            Indices32 = indices32;
        }

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

            if (is16) return new Indices(source.Select(x => (ushort)x).ToArray());
            return new Indices(source);
        }

        public uint this[int i] => Indices16 == null ? Indices32![i] : Indices16[i];

        public int Length => Indices32?.Length ?? Indices16!.Length;

        internal Indices Clone()
        {
            if (Indices32 != null) return new(Indices32.ToArray());
            else if (Indices16 != null) return new(Indices16.ToArray());
            else return new Indices((ushort[])[]);
        }
    }
}
