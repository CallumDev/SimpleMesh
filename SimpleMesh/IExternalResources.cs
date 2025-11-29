using System.IO;

namespace SimpleMesh;

public interface IExternalResources
{
     bool CanLoadResources { get; }
     Stream OpenStream(string filename);
}