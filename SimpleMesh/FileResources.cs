using System.IO;

namespace SimpleMesh;

public class FileResources : IExternalResources
{
    private string directory;
    public FileResources(string filename)
    {
        directory = Path.GetDirectoryName(filename);
    }

    public Stream OpenStream(string filename)
    {
        try
        {
            return File.OpenRead(Path.Combine(directory, filename));
        }
        catch
        {
            return null;
        }
    }
}