using System.Numerics;
using System.Text.Json;
using System.Text.Json.Serialization;
using SimpleMesh.Convex;

namespace SimpleMesh.Tests;

public class QuickhullTests
{
    public static IEnumerable<object[]> GetTestCases()
    {
        return Directory.GetFiles("Quickhull", "*.json", SearchOption.AllDirectories)
            .Select(x => new object[] { x });
    }

    public class Vector3Json : JsonConverter<Vector3>
    {
        public override Vector3 Read(
            ref Utf8JsonReader reader,
            Type typeToConvert,
            JsonSerializerOptions options)

        {
            Vector3 v = Vector3.Zero;
            int i = 0;
            if (reader.TokenType != JsonTokenType.StartArray)
            {
                throw new JsonException();
            }
            reader.Read();
            while (reader.TokenType == JsonTokenType.Number)
            {
                v[i++] = reader.GetSingle();
                reader.Read();
            }
            if (reader.TokenType != JsonTokenType.EndArray ||
                i != 3)
            {
                throw new JsonException();
            }
            return v;
        }

        public override void Write(
            Utf8JsonWriter writer,
            Vector3 vector,
            JsonSerializerOptions options)
        {
            writer.WriteStartArray();
            writer.WriteNumberValue(vector.X);
            writer.WriteNumberValue(vector.Y);
            writer.WriteNumberValue(vector.Z);
            writer.WriteEndArray();
        }

    }
    public static Vector3[] ReadTestHull(string path)
    {
        var serializeOptions = new JsonSerializerOptions
        {
            WriteIndented = true,
            Converters =
            {
                new Vector3Json()
            }
        };
        return JsonSerializer.Deserialize<Vector3[]>(File.ReadAllText(path), serializeOptions)!;
    }

    [Theory]
    [MemberData(nameof(GetTestCases))]
    public void TestHullValidity(string test)
    {
        var input = ReadTestHull(test);
        Assert.True(Hull.TryQuickhull(input, out var result));
        Assert.True(result.IsWatertight);
        Assert.True(result.IsConvex);
        Assert.Equal(AppliedRepairs.None, result.Repairs);
        if (File.Exists(Path.ChangeExtension(test, ".txt")))
        {
            var count = int.Parse(File.ReadAllText(Path.ChangeExtension(test, ".txt")).Trim());
            Assert.Equal(count, result.Indices.Count);
        }
    }
}