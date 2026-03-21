namespace SimpleMesh.Tests;

public class TangentGenerationTests
{
    [Fact]
    public void TestTangentGeneration()
    {
        var file = Model.FromFile("Models/cube_small.glb");
        file.CalculateTangents(true, false);

        // Tangents generated with glTF-Transform
        var expected = Model.FromFile("Models/cube_small_tangents.glb");
        Assert.True(file.Geometries[0].Vertices.Descriptor.Tangent > 0);
        Assert.True(expected.Geometries[0].Vertices.Descriptor.Tangent > 0);

        var gActual = file.Geometries[0];
        var gExpected = expected.Geometries[0];

        Assert.Equal(gExpected.Vertices.Count, gActual.Vertices.Count);
        for (int i = 0; i < gActual.Vertices.Count; i++)
        {
            Assert.Equal(gActual.Vertices.Position[i], gExpected.Vertices.Position[i]);
            Assert.Equal(gActual.Vertices.Tangent[i], gExpected.Vertices.Tangent[i]);
        }
    }
}
