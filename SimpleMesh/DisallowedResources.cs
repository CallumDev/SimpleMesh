using System;
using System.IO;

namespace SimpleMesh;

public sealed class DisallowedResources : IExternalResources
{
    public Stream OpenStream(string filename)
    {
        throw new ModelLoadException($"Opening external resources not allowed ({filename})");
    }
}