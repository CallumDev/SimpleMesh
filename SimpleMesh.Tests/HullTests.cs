using System.Numerics;
using System.Text.Json;
using SimpleMesh.Convex;

namespace SimpleMesh.Tests;

public class HullTests
{
    static Geometry LoadShape(string path)
    {
        var file = Model.FromFile(path);
        return file.Roots[0].Geometry;
    }
    
    [Fact]
    public void WatertightSanity()
    {
        // Watertight
        var x = LoadShape("Models/cube.obj");
        Assert.True(Hull.FromGeometry(x).IsWatertight);
        // Not watertight
        var y = LoadShape("Models/5sidebox.obj");
        Assert.False(Hull.FromGeometry(y).IsWatertight);
    }

    [Fact]
    public void NonConvexGeometry()
    {
        var hull = Hull.FromGeometry(LoadShape("Models/concave1.obj"));
        Assert.True(hull.IsWatertight);
        Assert.Equal(AppliedRepairs.None, hull.Repairs);
        Assert.False(hull.DegenerateMesh);
        Assert.False(hull.IsConvex);
    }

    private static readonly Vector3[] cubeVertices = new Vector3[]
    {
        new( 1,  1, -1),
        new( 1, -1, -1),
        new( 1,  1,  1),
        new( 1, -1,  1),
        new(-1,  1, -1),
        new(-1, -1, -1),
        new(-1,  1,  1),
        new(-1, -1,  1)
    };
    private static readonly int[] cubeIndices = new int[] {
        0, 4, 6, 3, 2, 6, 7, 6, 4, 5, 1, 3, 1, 0, 2, 5, 4, 0, 6, 2, 0,
        6, 7, 3, 4, 5, 7, 3, 7, 5, 2, 3, 1, 0, 1, 5
    };

    [Fact]
    public void ConvexCubeFromArrays()
    {
        var hull = Hull.FromTriangles(cubeVertices, cubeIndices, false);
        Assert.True(hull.IsWatertight);
        Assert.True(hull.IsConvex);
    }

    [Fact]
    public void FixWinding()
    {
        // Flip triangles 3 + 4
        var flippedIndices = new int[] {
            0, 4, 6, 3, 2, 6, 4, 6, 7, 3, 1, 5, 1, 0, 2, 5, 4, 0, 6, 2, 0,
            6, 7, 3, 4, 5, 7, 3, 7, 5, 2, 3, 1, 0, 1, 5
        };
        // After fixing
        var hull = Hull.FromTriangles(cubeVertices, flippedIndices, false);
        Assert.Equal(cubeIndices, hull.Indices);
        Assert.True(hull.IsWatertight);
        Assert.True(hull.IsConvex);
        Assert.Equal(AppliedRepairs.FixedWinding, hull.Repairs);
    }

    [Fact]
    public void FixInverted()
    {
        // Flip all
        var flippedIndices = new int[] {
            6, 4, 0, 6, 2, 3, 4, 6, 7, 3, 1, 5, 2, 0, 1, 0, 4, 5, 0, 2, 6,
            3, 7, 6, 7, 5, 4, 5, 7, 3, 1, 3, 2, 5, 1, 0
        };
        // After fixing
        var hull = Hull.FromTriangles(cubeVertices, flippedIndices, false);
        Assert.Equal(cubeIndices, hull.Indices);
        Assert.True(hull.IsWatertight);
        Assert.True(hull.IsConvex);
        Assert.Equal(AppliedRepairs.FlippedNormals, hull.Repairs);
    }

    [Fact]
    public void MakeConvexSuzanne()
    {
        var sh = LoadShape("Models/convexsuzanne.obj");
        var h = Hull.FromGeometry(sh);

        h.MakeConvex();
        Assert.True(h.IsConvex);
    }
    
    
    public static IEnumerable<object[]> GetTestCases()
    {
        return Directory.GetFiles("Models", "*.json", SearchOption.AllDirectories)
            .Select(x => new object[] { x });
    }

    record TestCase(bool convex, bool fixedwinding, bool multibody, string file);

    [Theory]
    [MemberData(nameof(GetTestCases))]
    public void ConfirmTestModels(string test)
    {
        var testCase = JsonSerializer.Deserialize<TestCase>(File.ReadAllText(test));
        var shape = LoadShape($"Models/{testCase.file}");
        var hull = Hull.FromGeometry(shape);
        Assert.Equal(testCase.convex, hull.IsConvex);
        Assert.Equal(testCase.multibody, hull.Multibody);
        Assert.Equal(testCase.fixedwinding,
            (hull.Repairs & AppliedRepairs.FixedWinding) == AppliedRepairs.FixedWinding);
    }
}