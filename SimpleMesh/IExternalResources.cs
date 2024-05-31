using System.IO;

namespace SimpleMesh;

public interface IExternalResources
{
     Stream OpenStream(string filename);
}