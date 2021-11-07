using System.Collections.Generic;

namespace SimpleMesh
{
    public class ModelLoadContext
    {
        public List<string> Warnings = new List<string>();

        internal void Warn(string cat, string msg)
        {
            Warnings.Add($"[{cat}] {msg}");
        }
    }
}