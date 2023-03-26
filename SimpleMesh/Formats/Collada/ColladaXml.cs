using System.Xml.Serialization;
using SimpleMesh.Formats.Collada.Schema;

namespace SimpleMesh.Formats.Collada;

static class ColladaXml
{
    public static XmlSerializer Xml = new XmlSerializer(typeof(COLLADA));
}