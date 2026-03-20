using System.Collections.Generic;

namespace SimpleMesh
{
    public class ModelLoadContext(List<string> warnings)
    {
        public List<string> Warnings = warnings;
        public IExternalResources ExternalResources = new DisallowedResources();

        internal void Warn(string cat, string msg)
        {
            Warnings.Add($"[{cat}] {msg}");
        }
    }
}
