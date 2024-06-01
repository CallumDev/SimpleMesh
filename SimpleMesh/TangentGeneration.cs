using System.Numerics;

namespace SimpleMesh;

public interface ITangentGeometry
{
    int GetNumFaces();
    int GetNumVerticesOfFace(int index);
    Vector3 GetPosition(int faceIndex, int faceVertex);
    Vector3 GetNormal(int faceIndex, int faceVertex);
    Vector2 GetTexCoord(int faceIndex, int faceVertex);
    void SetTangent(Vector4 tangent, int faceIndex, int faceVertex);
}

public static class TangentGeneration
{
    class MikkInterfaceImpl : SMikkTSpaceInterface
    {
        public ITangentGeometry G;
        public int GetNumFaces() => G.GetNumFaces();

        public int GetNumVerticesOfFace(int iFace) => G.GetNumVerticesOfFace(iFace);

        public unsafe void GetPosition(float* fvPosOut, int iFace, int iVert)
        {
            var v = G.GetPosition(iFace, iVert);
            fvPosOut[0] = v.X;
            fvPosOut[1] = v.Y;
            fvPosOut[2] = v.Z;
        }

        public unsafe void GetNormal(float* fvNormOut, int iFace, int iVert)
        {
            var v = G.GetNormal(iFace, iVert);
            fvNormOut[0] = v.X;
            fvNormOut[1] = v.Y;
            fvNormOut[2] = v.Z;
        }

        public unsafe void GetTexCoord(float* fvTexcOut, int iFace, int iVert)
        {
            var v = G.GetTexCoord(iFace, iVert);
            fvTexcOut[0] = v.X;
            fvTexcOut[1] = v.Y;
        }

        public unsafe void SetTSpaceBasic(float* fvTangent, float fSign, int iFace, int iVert)
        {
            var tangent = new Vector4(fvTangent[0], fvTangent[1], fvTangent[2], fSign);
            G.SetTangent(tangent, iFace, iVert);
        }
    }
    
    public static unsafe void GenerateMikkTSpace(ITangentGeometry geometry)
    {
        var context = new SMikkTSpaceContext();
        context.Interface = new MikkInterfaceImpl() { G = geometry };
        MikkTSpace.genTangSpaceDefault(&context);
    }
}