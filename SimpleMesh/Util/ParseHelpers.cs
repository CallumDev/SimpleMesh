using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Numerics;
using System.Threading;

namespace SimpleMesh.Util
{
    static class ParseHelpers
    {
        public record struct SplitElement(int Start, int Length);
        
        static (int start, int len) TrimIndex(ReadOnlySpan<char> span)
        {
            int num = 0;
            while (num < span.Length && char.IsWhiteSpace(span[num]))
                ++num;
            int index = span.Length - 1;
            while (index > num && char.IsWhiteSpace(span[index]))
                --index;
            return (num, index - num + 1);
        }
        
        public static int FixedSplit(ReadOnlySpan<char> source, Span<SplitElement> elements)
        {
            if (source.Length == 0 || source.IsWhiteSpace())
                return 0;
            if (source.Length == 1)
            {
                elements[0] = new(0, 1);
                return 1;
            }
            int start = 0;
            int elem = 0;
            while (start < source.Length)
            {
                var end = source.Slice(start).IndexOfAny(' ', '\t');
                if (end == 0)
                {
                    start++;
                    continue;
                }
                if (end == -1)
                    end = source.Length;
                else
                    end = start + end;
                if (end == start)
                    start++;
                else
                {
                    var (trimStart, trimLen) = TrimIndex(source.Slice(start, end - start));
                    if (trimLen != 0)
                    {
                        if (elem >= elements.Length)
                            return int.MaxValue;
                        elements[elem++] = new(start + trimStart, trimLen);
                    }
                    start = end;
                }
            }
            return elem;
        }

        public static int FloatSpan(ReadOnlySpan<char> source, Span<float> floats)
        {
            if (source.Length == 0 || source.IsWhiteSpace() || floats.Length == 0)
                return 0;
            if (source.Length == 1)
            {
                if (float.TryParse(source, CultureInfo.InvariantCulture, out floats[0]))
                    return 1;
                return -1;
            }
            int start = 0;
            int elem = 0;
            while (start < source.Length)
            {
                var end = source.Slice(start).IndexOfAny(' ', '\t');
                if (end == 0)
                {
                    start++;
                    continue;
                }
                if (end == -1)
                    end = source.Length;
                else
                    end = start + end;
                if (end == start)
                    start++;
                else
                {
                    var trimmed = source.Slice(start, end - start).TrimStart();
                    if (trimmed.Length != 0)
                    {
                        if (elem >= floats.Length)
                            return int.MaxValue;
                        if (!float.TryParse(trimmed, out floats[elem++]))
                            return -1;
                    }
                    start = end;
                }
            }
            return elem;
        }
        
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