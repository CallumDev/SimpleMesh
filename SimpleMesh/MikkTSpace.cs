/*
 *  Copyright (C) 2011 by Morten S. Mikkelsen
 *
 *  This software is provided 'as-is', without any express or implied
 *  warranty.  In no event will the authors be held liable for any damages
 *  arising from the use of this software.
 *
 *  Permission is granted to anyone to use this software for any purpose,
 *  including commercial applications, and to alter it and redistribute it
 *  freely, subject to the following restrictions:
 *
 *  1. The origin of this software must not be misrepresented; you must not
 *     claim that you wrote the original software. If you use this software
 *     in a product, an acknowledgment in the product documentation would be
 *     appreciated but is not required.
 *  2. Altered source versions must be plainly marked as such, and must not be
 *     misrepresented as being the original software.
 *  3. This notice may not be removed or altered from any source distribution.
 */

using System;
using System.Diagnostics;
using System.Numerics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

// ReSharper disable CompareOfFloatsByEqualityOperator

namespace SimpleMesh;

unsafe interface SMikkTSpaceInterface
{
    int GetNumFaces();
    int GetNumVerticesOfFace(int iFace);
    void GetPosition(float* fvPosOut, int iFace, int iVert);
    void GetNormal(float* fvNormOut, int iFace, int iVert);
    void GetTexCoord(float* fvTexcOut, int iFace, int iVert);
    void SetTSpaceBasic(float* fvTangent, float fSign, int iFace, int iVert);
}

struct SMikkTSpaceContext
{
    public SMikkTSpaceInterface Interface;
}

unsafe static class MikkTSpace
{
    // Helpers for porting
    static void* malloc(nint size) => (void*)Marshal.AllocHGlobal(size);
    static void free(void* pointer) => Marshal.FreeHGlobal((IntPtr)pointer);

    static void memset<T>(T* pointer, byte value, int size) where T : unmanaged
    {
        byte* ptr = (byte*)pointer;
        for (int i = 0; i < size; i++)
            ptr[i] = value;
    }

    static void memcpy<T, T2>(T* dest, T2* src, int len)
        where T : unmanaged
        where T2 : unmanaged
    {
        Buffer.MemoryCopy((void*)src, (void*)dest, len, len);
    }

    struct PObj3<T> where T : unmanaged //3 pointers
    {
        private T* _0;
        private T* _1;
        private T* _2;

        public ref T* this[int index]
        {
            get
            {
                if (index == 0) return ref _0;
                if (index == 1) return ref _1;
                if (index == 2) return ref _2;
                throw new IndexOutOfRangeException();
            }
        }
    }

    //Ported source code

    private const int INTERNAL_RND_SORT_SEED = 39871946;


    static bool veq(Vector3 v1, Vector3 v2)
    {
        return (v1.X == v2.X) && (v1.Y == v2.Y) && (v1.Z == v2.Z);
    }

    static Vector3 vadd(Vector3 v1, Vector3 v2)
    {
        Vector3 vRes;

        vRes.X = v1.X + v2.X;
        vRes.Y = v1.Y + v2.Y;
        vRes.Z = v1.Z + v2.Z;

        return vRes;
    }


    static Vector3 vsub(Vector3 v1, Vector3 v2)
    {
        Vector3 vRes;

        vRes.X = v1.X - v2.X;
        vRes.Y = v1.Y - v2.Y;
        vRes.Z = v1.Z - v2.Z;

        return vRes;
    }

    static Vector3 vscale(float fS, Vector3 v)
    {
        Vector3 vRes;

        vRes.X = fS * v.X;
        vRes.Y = fS * v.Y;
        vRes.Z = fS * v.Z;

        return vRes;
    }

    static float LengthSquared(Vector3 v)
    {
        return v.X * v.X + v.Y * v.Y + v.Z * v.Z;
    }

    static float Length(Vector3 v)
    {
        return MathF.Sqrt(LengthSquared(v));
    }

    static Vector3 Normalize(Vector3 v)
    {
        return vscale(1 / Length(v), v);
    }

    static float vdot(Vector3 v1, Vector3 v2)
    {
        return v1.X * v2.X + v1.Y * v2.Y + v1.Z * v2.Z;
    }


    static bool NotZero(float fX)
    {
        // could possibly use FLT_EPSILON instead
        return MathF.Abs(fX) > float.Epsilon;
    }

    static bool VNotZero(Vector3 v)
    {
        // might change this to an epsilon based test
        return NotZero(v.X) || NotZero(v.Y) || NotZero(v.Z);
    }


    struct SSubGroup
    {
        public int iNrFaces;
        public int* pTriMembers;
    }

    struct SGroup
    {
        public int iNrFaces;
        public int* pFaceIndices;
        public int iVertexRepresentitive;
        public bool bOrientPreservering;
    }

// 
    private const int MARK_DEGENERATE = 1;
    private const int QUAD_ONE_DEGEN_TRI = 2;
    private const int GROUP_WITH_ANY = 4;
    private const int ORIENT_PRESERVING = 8;


    struct STriInfo
    {
        public fixed int FaceNeighbors[3];
        public PObj3<SGroup> AssignedGroup;

        // normalized first order face derivatives
        public Vector3 vOs, vOt;
        public float fMagS, fMagT; // original magnitudes

        // determines if the current and the next triangle are a quad.
        public int iOrgFaceNumber;
        public int iFlag, iTSpacesOffs;
        public fixed byte vert_num[4];
    }

    struct STSpace
    {
        public Vector3 vOs;
        public float fMagS;
        public Vector3 vOt;
        public float fMagT;
        public int iCounter; // this is to average back into quads.
        public bool bOrient;
    }

    static int MakeIndex(int iFace, int iVert)
    {
        Debug.Assert(iVert >= 0 && iVert < 4 && iFace >= 0);
        return (iFace << 2) | (iVert & 0x3);
    }

    static void IndexToData(int* piFace, int* piVert, int iIndexIn)
    {
        piVert[0] = iIndexIn & 0x3;
        piFace[0] = iIndexIn >> 2;
    }

    static STSpace AvgTSpace(STSpace* pTS0, STSpace* pTS1)
    {
        STSpace ts_res = new STSpace();

        // this if is important. Due to floating point precision
        // averaging when ts0==ts1 will cause a slight difference
        // which results in tangent space splits later on
        if (pTS0->fMagS == pTS1->fMagS && pTS0->fMagT == pTS1->fMagT &&
            veq(pTS0->vOs, pTS1->vOs) && veq(pTS0->vOt, pTS1->vOt))
        {
            ts_res.fMagS = pTS0->fMagS;
            ts_res.fMagT = pTS0->fMagT;
            ts_res.vOs = pTS0->vOs;
            ts_res.vOt = pTS0->vOt;
        }
        else
        {
            ts_res.fMagS = 0.5f * (pTS0->fMagS + pTS1->fMagS);
            ts_res.fMagT = 0.5f * (pTS0->fMagT + pTS1->fMagT);
            ts_res.vOs = vadd(pTS0->vOs, pTS1->vOs);
            ts_res.vOt = vadd(pTS0->vOt, pTS1->vOt);
            if (VNotZero(ts_res.vOs)) ts_res.vOs = Normalize(ts_res.vOs);
            if (VNotZero(ts_res.vOt)) ts_res.vOt = Normalize(ts_res.vOt);
        }

        return ts_res;
    }


    public static bool genTangSpaceDefault(SMikkTSpaceContext* pContext)
    {
        return genTangSpace(pContext, 180.0f);
    }

    public static bool genTangSpace(SMikkTSpaceContext* pContext, float fAngularThreshold)
    {
        // count nr_triangles
        int* piTriListIn = null, piGroupTrianglesBuffer = null;
        STriInfo* pTriInfos = null;
        SGroup* pGroups = null;
        STSpace* psTspace = null;
        int iNrTrianglesIn = 0, f = 0, t = 0, i = 0;
        int iNrTSPaces = 0, iTotTris = 0, iDegenTriangles = 0, iNrMaxGroups = 0;
        int iNrActiveGroups = 0, index = 0;
        int iNrFaces = pContext->Interface.GetNumFaces();
        bool bRes = false;
        float fThresCos = (float)MathF.Cos((fAngularThreshold * (float)MathF.PI) / 180.0f);

        // verify all call-backs have been set
        if (pContext->Interface == null)
            return false;

        // count triangles on supported faces
        for (f = 0; f < iNrFaces; f++)
        {
            int verts = pContext->Interface.GetNumVerticesOfFace(f);
            if (verts == 3) ++iNrTrianglesIn;
            else if (verts == 4) iNrTrianglesIn += 2;
        }

        if (iNrTrianglesIn <= 0) return false;

        // allocate memory for an index list
        piTriListIn = (int*)malloc(sizeof(int) * 3 * iNrTrianglesIn);
        pTriInfos = (STriInfo*)malloc(sizeof(STriInfo) * iNrTrianglesIn);
        if (piTriListIn == null || pTriInfos == null)
        {
            if (piTriListIn != null) free(piTriListIn);
            if (pTriInfos != null) free(pTriInfos);
            return false;
        }

        // make an initial triangle --> face index list
        iNrTSPaces = GenerateInitialVerticesIndexList(pTriInfos, piTriListIn, pContext, iNrTrianglesIn);

        // make a welded index list of identical positions and attributes (pos, norm, texc)
        //printf("gen welded index list begin\n");
        GenerateSharedVerticesIndexList(piTriListIn, pContext, iNrTrianglesIn);
        //printf("gen welded index list end\n");

        // Mark all degenerate triangles
        iTotTris = iNrTrianglesIn;
        iDegenTriangles = 0;
        for (t = 0; t < iTotTris; t++)
        {
            int i0 = piTriListIn[t * 3 + 0];
            int i1 = piTriListIn[t * 3 + 1];
            int i2 = piTriListIn[t * 3 + 2];
            Vector3 p0 = GetPosition(pContext, i0);
            Vector3 p1 = GetPosition(pContext, i1);
            Vector3 p2 = GetPosition(pContext, i2);
            if (veq(p0, p1) || veq(p0, p2) || veq(p1, p2)) // degenerate
            {
                pTriInfos[t].iFlag |= MARK_DEGENERATE;
                ++iDegenTriangles;
            }
        }

        iNrTrianglesIn = iTotTris - iDegenTriangles;

        // mark all triangle pairs that belong to a quad with only one
        // good triangle. These need special treatment in DegenEpilogue().
        // Additionally, move all good triangles to the start of
        // pTriInfos[] and piTriListIn[] without changing order and
        // put the degenerate triangles last.
        DegenPrologue(pTriInfos, piTriListIn, iNrTrianglesIn, iTotTris);


        // evaluate triangle level attributes and neighbor list
        //printf("gen neighbors list begin\n");
        InitTriInfo(pTriInfos, piTriListIn, pContext, iNrTrianglesIn);
        //printf("gen neighbors list end\n");


        // based on the 4 rules, identify groups based on connectivity
        iNrMaxGroups = iNrTrianglesIn * 3;
        pGroups = (SGroup*)malloc(sizeof(SGroup) * iNrMaxGroups);
        piGroupTrianglesBuffer = (int*)malloc(sizeof(int) * iNrTrianglesIn * 3);
        if (pGroups == null || piGroupTrianglesBuffer == null)
        {
            if (pGroups != null) free(pGroups);
            if (piGroupTrianglesBuffer != null) free(piGroupTrianglesBuffer);
            free(piTriListIn);
            free(pTriInfos);
            return false;
        }

        //printf("gen 4rule groups begin\n");
        iNrActiveGroups =
            Build4RuleGroups(pTriInfos, pGroups, piGroupTrianglesBuffer, piTriListIn, iNrTrianglesIn);
        //printf("gen 4rule groups end\n");

        //

        psTspace = (STSpace*)malloc(sizeof(STSpace) * iNrTSPaces);
        if (psTspace == null)
        {
            free(piTriListIn);
            free(pTriInfos);
            free(pGroups);
            free(piGroupTrianglesBuffer);
            return false;
        }

        memset(psTspace, 0, sizeof(STSpace) * iNrTSPaces);
        for (t = 0; t < iNrTSPaces; t++)
        {
            psTspace[t].vOs.X = 1.0f;
            psTspace[t].vOs.Y = 0.0f;
            psTspace[t].vOs.Z = 0.0f;
            psTspace[t].fMagS = 1.0f;
            psTspace[t].vOt.X = 0.0f;
            psTspace[t].vOt.Y = 1.0f;
            psTspace[t].vOt.Z = 0.0f;
            psTspace[t].fMagT = 1.0f;
        }

        // make tspaces, each group is split up into subgroups if necessary
        // based on fAngularThreshold. Finally a tangent space is made for
        // every resulting subgroup
        //printf("gen tspaces begin\n");
        bRes = GenerateTSpaces(psTspace, pTriInfos, pGroups, iNrActiveGroups, piTriListIn, fThresCos, pContext);
        //printf("gen tspaces end\n");

        // clean up
        free(pGroups);
        free(piGroupTrianglesBuffer);

        if (!bRes) // if an allocation in GenerateTSpaces() failed
        {
            // clean up and return false
            free(pTriInfos);
            free(piTriListIn);
            free(psTspace);
            return false;
        }


        // degenerate quads with one good triangle will be fixed by copying a space from
        // the good triangle to the coinciding vertex.
        // all other degenerate triangles will just copy a space from any good triangle
        // with the same welded index in piTriListIn[].
        DegenEpilogue(psTspace, pTriInfos, piTriListIn, pContext, iNrTrianglesIn, iTotTris);

        free(pTriInfos);
        free(piTriListIn);

        index = 0;
        for (f = 0; f < iNrFaces; f++)
        {
            int verts = pContext->Interface.GetNumVerticesOfFace(f);
            if (verts != 3 && verts != 4) continue;


            // I've decided to let degenerate triangles and group-with-anythings
            // vary between left/right hand coordinate systems at the vertices.
            // All healthy triangles on the other hand are built to always be either or.

            /*// force the coordinate system orientation to be uniform for every face.
            // (this is already the case for good triangles but not for
            // degenerate ones and those with bGroupWithAnything==true)
            bool bOrient = psTspace[index].bOrient;
            if (psTspace[index].iCounter == 0)	// tspace was not derived from a group
            {
                // look for a space created in GenerateTSpaces() by iCounter>0
                bool bNotFound = true;
                int i=1;
                while (i<verts && bNotFound)
                {
                    if (psTspace[index+i].iCounter > 0) bNotFound=false;
                    else ++i;
                }
                if (!bNotFound) bOrient = psTspace[index+i].bOrient;
            }*/

            // set data
            for (i = 0; i < verts; i++)
            {
                STSpace* pTSpace = &psTspace[index];
                Vector3 tang = pTSpace->vOs;
                //Vector3 bitang = pTSpace->vOt;
                //Only using setTSpaceBasic for now
                /*float tang[] =  {
                    pTSpace->vOs.X, pTSpace->vOs.Y, pTSpace->vOs.Z
                }
                ;
                float bitang[] =  {
                    pTSpace->vOt.X, pTSpace->vOt.Y, pTSpace->vOt.Z
                }
                ;
                if (pContext->m_pInterface->m_setTSpace != null)
                    pContext->m_pInterface->m_setTSpace(pContext, tang, bitang, pTSpace->fMagS, pTSpace->fMagT,
                        pTSpace->bOrient, f, i);*/
                pContext->Interface.SetTSpaceBasic((float*)&tang, pTSpace->bOrient == true ? 1.0f : (-1.0f),
                    f, i);
                /*if (pContext->m_pInterface->m_setTSpaceBasic != null)
                    pContext->m_pInterface->m_setTSpaceBasic(pContext, tang, pTSpace->bOrient == true ? 1.0f : (-1.0f),
                        f, i);*/

                ++index;
            }
        }

        free(psTspace);


        return true;
    }

///////////////////////////////////////////////////////////////////////////////////////////////////////////////////
    struct STmpVert
    {
        public fixed float vert[3];
        public int index;
    }

    static int g_iCells = 2048;

// it is IMPORTANT that this function is called to evaluate the hash since
// inlining could potentially reorder instructions and generate different
// results for the same effective input value fVal.
    [MethodImpl(MethodImplOptions.NoInlining)]
    static int FindGridCell(float fMin, float fMax, float fVal)
    {
        float fIndex = g_iCells * ((fVal - fMin) / (fMax - fMin));
        int iIndex = (int)fIndex;
        return iIndex < g_iCells ? (iIndex >= 0 ? iIndex : 0) : (g_iCells - 1);
    }


    static void GenerateSharedVerticesIndexList(int* piTriList_in_and_out, SMikkTSpaceContext* pContext,
        int iNrTrianglesIn)

    {
        // Generate bounding box
        int* piHashTable = null, piHashCount = null, piHashOffsets = null, piHashCount2 = null;
        STmpVert* pTmpVert = null;
        int i = 0, iChannel = 0, k = 0, e = 0;
        int iMaxCount = 0;
        Vector3 vMin = GetPosition(pContext, 0), vMax = vMin, vDim;
        float fMin, fMax;
        for (i = 1; i < (iNrTrianglesIn * 3); i++)
        {
            int index = piTriList_in_and_out[i];

            Vector3 vP = GetPosition(pContext, index);
            if (vMin.X > vP.X) vMin.X = vP.X;
            else if (vMax.X < vP.X) vMax.X = vP.X;
            if (vMin.Y > vP.Y) vMin.Y = vP.Y;
            else if (vMax.Y < vP.Y) vMax.Y = vP.Y;
            if (vMin.Z > vP.Z) vMin.Z = vP.Z;
            else if (vMax.Z < vP.Z) vMax.Z = vP.Z;
        }

        vDim = vsub(vMax, vMin);
        iChannel = 0;
        fMin = vMin.X;
        fMax = vMax.X;
        if (vDim.Y > vDim.X && vDim.Y > vDim.Z)
        {
            iChannel = 1;
            fMin = vMin.Y;
            fMax = vMax.Y;
        }
        else if (vDim.Z > vDim.X)
        {
            iChannel = 2;
            fMin = vMin.Z;
            fMax = vMax.Z;
        }

        // make allocations
        piHashTable = (int*)malloc(sizeof(int) * iNrTrianglesIn * 3);
        piHashCount = (int*)malloc(sizeof(int) * g_iCells);
        piHashOffsets = (int*)malloc(sizeof(int) * g_iCells);
        piHashCount2 = (int*)malloc(sizeof(int) * g_iCells);

        if (piHashTable == null || piHashCount == null || piHashOffsets == null || piHashCount2 == null)
        {
            if (piHashTable != null) free(piHashTable);
            if (piHashCount != null) free(piHashCount);
            if (piHashOffsets != null) free(piHashOffsets);
            if (piHashCount2 != null) free(piHashCount2);
            GenerateSharedVerticesIndexListSlow(piTriList_in_and_out, pContext, iNrTrianglesIn);
            return;
        }

        memset(piHashCount, 0, sizeof(int) * g_iCells);
        memset(piHashCount2, 0, sizeof(int) * g_iCells);

        // count amount of elements in each cell unit
        for (i = 0; i < (iNrTrianglesIn * 3); i++)
        {
            int index = piTriList_in_and_out[i];
            Vector3 vP = GetPosition(pContext, index);
            float fVal = iChannel == 0 ? vP.X : (iChannel == 1 ? vP.Y : vP.Z);
            int iCell = FindGridCell(fMin, fMax, fVal);
            ++piHashCount[iCell];
        }

        // evaluate start index of each cell.
        piHashOffsets[0] = 0;
        for (k = 1; k < g_iCells; k++)
            piHashOffsets[k] = piHashOffsets[k - 1] + piHashCount[k - 1];

        // insert vertices
        for (i = 0; i < (iNrTrianglesIn * 3); i++)
        {
            int index = piTriList_in_and_out[i];
            Vector3 vP = GetPosition(pContext, index);
            float fVal = iChannel == 0 ? vP.X : (iChannel == 1 ? vP.Y : vP.Z);
            int iCell = FindGridCell(fMin, fMax, fVal);
            int* pTable = null;

            Debug.Assert(piHashCount2[iCell] < piHashCount[iCell]);
            pTable = &piHashTable[piHashOffsets[iCell]];
            pTable[piHashCount2[iCell]] = i; // vertex i has been inserted.
            ++piHashCount2[iCell];
        }

        for (k = 0; k < g_iCells; k++)
            Debug.Assert(piHashCount2[k] == piHashCount[k]); // verify the count
        free(piHashCount2);

        // find maximum amount of entries in any hash entry
        iMaxCount = piHashCount[0];
        for (k = 1; k < g_iCells; k++)
            if (iMaxCount < piHashCount[k])
                iMaxCount = piHashCount[k];
        pTmpVert = (STmpVert*)malloc(sizeof(STmpVert) * iMaxCount);


        // complete the merge
        for (k = 0; k < g_iCells; k++)
        {
            // extract table of cell k and amount of entries in it
            int* pTable = &piHashTable[piHashOffsets[k]];
            int iEntries = piHashCount[k];
            if (iEntries < 2) continue;

            if (pTmpVert != null)
            {
                for (e = 0; e < iEntries; e++)
                {
                    i = pTable[e];
                    Vector3 vP = GetPosition(pContext, piTriList_in_and_out[i]);
                    pTmpVert[e].vert[0] = vP.X;
                    pTmpVert[e].vert[1] = vP.Y;
                    pTmpVert[e].vert[2] = vP.Z;
                    pTmpVert[e].index = i;
                }

                MergeVertsFast(piTriList_in_and_out, pTmpVert, pContext, 0, iEntries - 1);
            }
            else
                MergeVertsSlow(piTriList_in_and_out, pContext, pTable, iEntries);
        }

        if (pTmpVert != null)
        {
            free(pTmpVert);
        }

        free(piHashTable);
        free(piHashCount);
        free(piHashOffsets);
    }

    static void MergeVertsFast(int* piTriList_in_and_out, STmpVert* pTmpVert, SMikkTSpaceContext*
        pContext, int iL_in, int iR_in)
    {
        // make bbox
        int c = 0, l = 0, channel = 0;
        float* fvMin = stackalloc float[3];
        float* fvMax = stackalloc float[3];
        float dx = 0, dy = 0, dz = 0, fSep = 0;
        for (c = 0; c < 3; c++)
        {
            fvMin[c] = pTmpVert[iL_in].vert[c];
            fvMax[c] = fvMin[c];
        }

        for (l = (iL_in + 1); l <= iR_in; l++)
        {
            for (c = 0; c < 3; c++)
            {
                if (fvMin[c] > pTmpVert[l].vert[c]) fvMin[c] = pTmpVert[l].vert[c];
                if (fvMax[c] < pTmpVert[l].vert[c]) fvMax[c] = pTmpVert[l].vert[c];
            }
        }

        dx = fvMax[0] - fvMin[0];
        dy = fvMax[1] - fvMin[1];
        dz = fvMax[2] - fvMin[2];

        channel = 0;
        if (dy > dx && dy > dz) channel = 1;
        else if (dz > dx) channel = 2;

        fSep = 0.5f * (fvMax[channel] + fvMin[channel]);

        // stop if all vertices are NaNs
        if (!float.IsFinite(fSep))
            return;

        // terminate recursion when the separation/average value
        // is no longer strictly between fMin and fMax values.
        if (fSep >= fvMax[channel] || fSep <= fvMin[channel])
        {
            // complete the weld
            for (l = iL_in; l <= iR_in; l++)
            {
                int i = pTmpVert[l].index;
                int index = piTriList_in_and_out[i];
                Vector3 vP = GetPosition(pContext, index);
                Vector3 vN = GetNormal(pContext, index);
                Vector3 vT = GetTexCoord(pContext, index);

                bool bNotFound = true;
                int l2 = iL_in, i2rec = -1;
                while (l2 < l && bNotFound)
                {
                    int i2 = pTmpVert[l2].index;
                    int index2 = piTriList_in_and_out[i2];
                    Vector3 vP2 = GetPosition(pContext, index2);
                    Vector3 vN2 = GetNormal(pContext, index2);
                    Vector3 vT2 = GetTexCoord(pContext, index2);
                    i2rec = i2;

                    //if (vP==vP2 && vN==vN2 && vT==vT2)
                    if (vP.X == vP2.X && vP.Y == vP2.Y && vP.Z == vP2.Z &&
                        vN.X == vN2.X && vN.Y == vN2.Y && vN.Z == vN2.Z &&
                        vT.X == vT2.X && vT.Y == vT2.Y && vT.Z == vT2.Z)
                        bNotFound = false;
                    else
                        ++l2;
                }

                // merge if previously found
                if (!bNotFound)
                    piTriList_in_and_out[i] = piTriList_in_and_out[i2rec];
            }
        }
        else
        {
            int iL = iL_in, iR = iR_in;
            Debug.Assert((iR_in - iL_in) > 0); // at least 2 entries

            // separate (by fSep) all points between iL_in and iR_in in pTmpVert[]
            while (iL < iR)
            {
                bool bReadyLeftSwap = false, bReadyRightSwap = false;
                while ((!bReadyLeftSwap) && iL < iR)
                {
                    Debug.Assert(iL >= iL_in && iL <= iR_in);
                    bReadyLeftSwap = !(pTmpVert[iL].vert[channel] < fSep);
                    if (!bReadyLeftSwap) ++iL;
                }

                while ((!bReadyRightSwap) && iL < iR)
                {
                    Debug.Assert(iR >= iL_in && iR <= iR_in);
                    bReadyRightSwap = pTmpVert[iR].vert[channel] < fSep;
                    if (!bReadyRightSwap) --iR;
                }

                Debug.Assert((iL < iR) || !(bReadyLeftSwap && bReadyRightSwap));

                if (bReadyLeftSwap && bReadyRightSwap)
                {
                    STmpVert sTmp = pTmpVert[iL];
                    Debug.Assert(iL < iR);
                    pTmpVert[iL] = pTmpVert[iR];
                    pTmpVert[iR] = sTmp;
                    ++iL;
                    --iR;
                }
            }

            Debug.Assert(iL == (iR + 1) || (iL == iR));
            if (iL == iR)
            {
                bool bReadyRightSwap = pTmpVert[iR].vert[channel] < fSep;
                if (bReadyRightSwap) ++iL;
                else --iR;
            }

            // only need to weld when there is more than 1 instance of the (x,y,z)
            if (iL_in < iR)
                MergeVertsFast(piTriList_in_and_out, pTmpVert, pContext, iL_in, iR); // weld all left of fSep
            if (iL < iR_in)
                MergeVertsFast(piTriList_in_and_out, pTmpVert, pContext, iL,
                    iR_in); // weld all right of (or equal to) fSep
        }
    }

    static void MergeVertsSlow(int* piTriList_in_and_out, SMikkTSpaceContext* pContext, int*
        pTable, int iEntries)
    {
        // this can be optimized further using a tree structure or more hashing.
        int e = 0;
        for (e = 0; e < iEntries; e++)
        {
            int i = pTable[e];
            int index = piTriList_in_and_out[i];
            Vector3 vP = GetPosition(pContext, index);
            Vector3 vN = GetNormal(pContext, index);
            Vector3 vT = GetTexCoord(pContext, index);

            bool bNotFound = true;
            int e2 = 0, i2rec = -1;
            while (e2 < e && bNotFound)
            {
                int i2 = pTable[e2];
                int index2 = piTriList_in_and_out[i2];
                Vector3 vP2 = GetPosition(pContext, index2);
                Vector3 vN2 = GetNormal(pContext, index2);
                Vector3 vT2 = GetTexCoord(pContext, index2);
                i2rec = i2;

                if (veq(vP, vP2) && veq(vN, vN2) && veq(vT, vT2))
                    bNotFound = false;
                else
                    ++e2;
            }

            // merge if previously found
            if (!bNotFound)
                piTriList_in_and_out[i] = piTriList_in_and_out[i2rec];
        }
    }

    static void GenerateSharedVerticesIndexListSlow(int* piTriList_in_and_out, SMikkTSpaceContext*
        pContext, int iNrTrianglesIn)
    {
        int iNumUniqueVerts = 0, t = 0, i = 0;
        for (t = 0; t < iNrTrianglesIn; t++)
        {
            for (i = 0; i < 3; i++)
            {
                int offs = t * 3 + i;
                int index = piTriList_in_and_out[offs];

                Vector3 vP = GetPosition(pContext, index);
                Vector3 vN = GetNormal(pContext, index);
                Vector3 vT = GetTexCoord(pContext, index);

                bool bFound = false;
                int t2 = 0, index2rec = -1;
                while (!bFound && t2 <= t)
                {
                    int j = 0;
                    while (!bFound && j < 3)
                    {
                        int index2 = piTriList_in_and_out[t2 * 3 + j];
                        Vector3 vP2 = GetPosition(pContext, index2);
                        Vector3 vN2 = GetNormal(pContext, index2);
                        Vector3 vT2 = GetTexCoord(pContext, index2);

                        if (veq(vP, vP2) && veq(vN, vN2) && veq(vT, vT2))
                            bFound = true;
                        else
                            ++j;
                    }

                    if (!bFound) ++t2;
                }

                Debug.Assert(bFound);
                // if we found our own
                if (index2rec == index)
                {
                    ++iNumUniqueVerts;
                }

                piTriList_in_and_out[offs] = index2rec;
            }
        }
    }

    static int GenerateInitialVerticesIndexList(STriInfo* pTriInfos, int* piTriList_out, SMikkTSpaceContext*
        pContext, int iNrTrianglesIn)
    {
        int iTSpacesOffs = 0, f = 0, t = 0;
        int iDstTriIndex = 0;
        for (f = 0; f < pContext->Interface.GetNumFaces(); f++)
        {
            int verts = pContext->Interface.GetNumVerticesOfFace(f);
            if (verts != 3 && verts != 4) continue;

            pTriInfos[iDstTriIndex].iOrgFaceNumber = f;
            pTriInfos[iDstTriIndex].iTSpacesOffs = iTSpacesOffs;

            if (verts == 3)
            {
                byte* pVerts = pTriInfos[iDstTriIndex].vert_num;
                pVerts[0] = 0;
                pVerts[1] = 1;
                pVerts[2] = 2;
                piTriList_out[iDstTriIndex * 3 + 0] = MakeIndex(f, 0);
                piTriList_out[iDstTriIndex * 3 + 1] = MakeIndex(f, 1);
                piTriList_out[iDstTriIndex * 3 + 2] = MakeIndex(f, 2);
                ++iDstTriIndex; // next
            }
            else
            {
                {
                    pTriInfos[iDstTriIndex + 1].iOrgFaceNumber = f;
                    pTriInfos[iDstTriIndex + 1].iTSpacesOffs = iTSpacesOffs;
                }

                {
                    // need an order independent way to evaluate
                    // tspace on quads. This is done by splitting
                    // along the shortest diagonal.
                    int i0 = MakeIndex(f, 0);
                    int i1 = MakeIndex(f, 1);
                    int i2 = MakeIndex(f, 2);
                    int i3 = MakeIndex(f, 3);
                    Vector3 T0 = GetTexCoord(pContext, i0);
                    Vector3 T1 = GetTexCoord(pContext, i1);
                    Vector3 T2 = GetTexCoord(pContext, i2);
                    Vector3 T3 = GetTexCoord(pContext, i3);
                    float distSQ_02 = LengthSquared(vsub(T2, T0));
                    float distSQ_13 = LengthSquared(vsub(T3, T1));
                    bool bQuadDiagIs_02;
                    if (distSQ_02 < distSQ_13)
                        bQuadDiagIs_02 = true;
                    else if (distSQ_13 < distSQ_02)
                        bQuadDiagIs_02 = false;
                    else
                    {
                        Vector3 P0 = GetPosition(pContext, i0);
                        Vector3 P1 = GetPosition(pContext, i1);
                        Vector3 P2 = GetPosition(pContext, i2);
                        Vector3 P3 = GetPosition(pContext, i3);
                        distSQ_02 = LengthSquared(vsub(P2, P0));
                        distSQ_13 = LengthSquared(vsub(P3, P1));

                        bQuadDiagIs_02 = distSQ_13 < distSQ_02 ? false : true;
                    }

                    if (bQuadDiagIs_02)
                    {
                        {
                            byte* pVerts_A = pTriInfos[iDstTriIndex].vert_num;
                            pVerts_A[0] = 0;
                            pVerts_A[1] = 1;
                            pVerts_A[2] = 2;
                        }
                        piTriList_out[iDstTriIndex * 3 + 0] = i0;
                        piTriList_out[iDstTriIndex * 3 + 1] = i1;
                        piTriList_out[iDstTriIndex * 3 + 2] = i2;
                        ++iDstTriIndex; // next
                        {
                            byte* pVerts_B = pTriInfos[iDstTriIndex].vert_num;
                            pVerts_B[0] = 0;
                            pVerts_B[1] = 2;
                            pVerts_B[2] = 3;
                        }
                        piTriList_out[iDstTriIndex * 3 + 0] = i0;
                        piTriList_out[iDstTriIndex * 3 + 1] = i2;
                        piTriList_out[iDstTriIndex * 3 + 2] = i3;
                        ++iDstTriIndex; // next
                    }
                    else
                    {
                        {
                            byte* pVerts_A = pTriInfos[iDstTriIndex].vert_num;
                            pVerts_A[0] = 0;
                            pVerts_A[1] = 1;
                            pVerts_A[2] = 3;
                        }
                        piTriList_out[iDstTriIndex * 3 + 0] = i0;
                        piTriList_out[iDstTriIndex * 3 + 1] = i1;
                        piTriList_out[iDstTriIndex * 3 + 2] = i3;
                        ++iDstTriIndex; // next
                        {
                            byte* pVerts_B = pTriInfos[iDstTriIndex].vert_num;
                            pVerts_B[0] = 1;
                            pVerts_B[1] = 2;
                            pVerts_B[2] = 3;
                        }
                        piTriList_out[iDstTriIndex * 3 + 0] = i1;
                        piTriList_out[iDstTriIndex * 3 + 1] = i2;
                        piTriList_out[iDstTriIndex * 3 + 2] = i3;
                        ++iDstTriIndex; // next
                    }
                }
            }

            iTSpacesOffs += verts;
            Debug.Assert(iDstTriIndex <= iNrTrianglesIn);
        }

        for (t = 0; t < iNrTrianglesIn; t++)
            pTriInfos[t].iFlag = 0;

        // return total amount of tspaces
        return iTSpacesOffs;
    }

    static Vector3 GetPosition(SMikkTSpaceContext* pContext, int index)
    {
        int iF, iI;
        Vector3 res;

        float* pos = stackalloc float[3];
        IndexToData(&iF, &iI, index);
        pContext->Interface.GetPosition(pos, iF, iI);
        res.X = pos[0];
        res.Y = pos[1];
        res.Z = pos[2];
        return res;
    }

    static Vector3 GetNormal(SMikkTSpaceContext* pContext, int index)
    {
        int iF, iI;
        Vector3 res;
        float* norm = stackalloc float[3];
        IndexToData(&iF, &iI, index);
        pContext->Interface.GetNormal(norm, iF, iI);
        res.X = norm[0];
        res.Y = norm[1];
        res.Z = norm[2];
        return res;
    }

    static Vector3 GetTexCoord(SMikkTSpaceContext* pContext, int index)
    {
        int iF, iI;
        Vector3 res;
        float* texc = stackalloc float[2];
        IndexToData(&iF, &iI, index);
        pContext->Interface.GetTexCoord(texc, iF, iI);
        res.X = texc[0];
        res.Y = texc[1];
        res.Z = 1.0f;
        return res;
    }

/////////////////////////////////////////////////////////////////////////////////////////////////////
/////////////////////////////////////////////////////////////////////////////////////////////////////

    [StructLayout(LayoutKind.Explicit)]
    struct SEdge

    {
        [FieldOffset(0)] public fixed int array[3];
        [FieldOffset(0)] public int i0;
        [FieldOffset(4)] public int i1;
        [FieldOffset(8)] public int f;
    }


// returns the texture area times 2
    static float CalcTexArea(SMikkTSpaceContext* pContext, int* indices)
    {
        Vector3 t1 = GetTexCoord(pContext, indices[0]);
        Vector3 t2 = GetTexCoord(pContext, indices[1]);
        Vector3 t3 = GetTexCoord(pContext, indices[2]);

        float t21x = t2.X - t1.X;
        float t21y = t2.Y - t1.Y;
        float t31x = t3.X - t1.X;
        float t31y = t3.Y - t1.Y;

        float fSignedAreaSTx2 = t21x * t31y - t21y * t31x;

        return fSignedAreaSTx2 < 0 ? (-fSignedAreaSTx2) : fSignedAreaSTx2;
    }

    static void InitTriInfo(STriInfo* pTriInfos, int* piTriListIn, SMikkTSpaceContext* pContext, int iNrTrianglesIn)
    {
        int f = 0, i = 0, t = 0;
        // pTriInfos[f].iFlag is cleared in GenerateInitialVerticesIndexList() which is called before this function.

        // generate neighbor info list
        for (f = 0; f < iNrTrianglesIn; f++)
        for (i = 0; i < 3; i++)
        {
            pTriInfos[f].FaceNeighbors[i] = -1;
            pTriInfos[f].AssignedGroup[i] = null;

            pTriInfos[f].vOs.X = 0.0f;
            pTriInfos[f].vOs.Y = 0.0f;
            pTriInfos[f].vOs.Z = 0.0f;
            pTriInfos[f].vOt.X = 0.0f;
            pTriInfos[f].vOt.Y = 0.0f;
            pTriInfos[f].vOt.Z = 0.0f;
            pTriInfos[f].fMagS = 0;
            pTriInfos[f].fMagT = 0;

            // assumed bad
            pTriInfos[f].iFlag |= GROUP_WITH_ANY;
        }

        // evaluate first order derivatives
        for (f = 0; f < iNrTrianglesIn; f++)
        {
            // initial values
            Vector3 v1 = GetPosition(pContext, piTriListIn[f * 3 + 0]);
            Vector3 v2 = GetPosition(pContext, piTriListIn[f * 3 + 1]);
            Vector3 v3 = GetPosition(pContext, piTriListIn[f * 3 + 2]);
            Vector3 t1 = GetTexCoord(pContext, piTriListIn[f * 3 + 0]);
            Vector3 t2 = GetTexCoord(pContext, piTriListIn[f * 3 + 1]);
            Vector3 t3 = GetTexCoord(pContext, piTriListIn[f * 3 + 2]);

            float t21x = t2.X - t1.X;
            float t21y = t2.Y - t1.Y;
            float t31x = t3.X - t1.X;
            float t31y = t3.Y - t1.Y;
            Vector3 d1 = vsub(v2, v1);
            Vector3 d2 = vsub(v3, v1);

            float fSignedAreaSTx2 = t21x * t31y - t21y * t31x;
            //Debug.Assert(fSignedAreaSTx2!=0);
            Vector3 vOs = vsub(vscale(t31y, d1), vscale(t21y, d2)); // eq 18
            Vector3 vOt = vadd(vscale(-t31x, d1), vscale(t21x, d2)); // eq 19

            pTriInfos[f].iFlag |= (fSignedAreaSTx2 > 0 ? ORIENT_PRESERVING : 0);

            if (NotZero(fSignedAreaSTx2))
            {
                float fAbsArea = MathF.Abs(fSignedAreaSTx2);
                float fLenOs = Length(vOs);
                float fLenOt = Length(vOt);
                float fS = (pTriInfos[f].iFlag & ORIENT_PRESERVING) == 0 ? (-1.0f) : 1.0f;
                if (NotZero(fLenOs)) pTriInfos[f].vOs = vscale(fS / fLenOs, vOs);
                if (NotZero(fLenOt)) pTriInfos[f].vOt = vscale(fS / fLenOt, vOt);

                // evaluate magnitudes prior to normalization of vOs and vOt
                pTriInfos[f].fMagS = fLenOs / fAbsArea;
                pTriInfos[f].fMagT = fLenOt / fAbsArea;

                // if this is a good triangle
                if (NotZero(pTriInfos[f].fMagS) && NotZero(pTriInfos[f].fMagT))
                    pTriInfos[f].iFlag &= (~GROUP_WITH_ANY);
            }
        }

        // force otherwise healthy quads to a fixed orientation
        while (t < (iNrTrianglesIn - 1))
        {
            int iFO_a = pTriInfos[t].iOrgFaceNumber;
            int iFO_b = pTriInfos[t + 1].iOrgFaceNumber;
            if (iFO_a == iFO_b) // this is a quad
            {
                bool bIsDeg_a = (pTriInfos[t].iFlag & MARK_DEGENERATE) != 0 ? true : false;
                bool bIsDeg_b = (pTriInfos[t + 1].iFlag & MARK_DEGENERATE) != 0 ? true : false;

                // bad triangles should already have been removed by
                // DegenPrologue(), but just in case check bIsDeg_a and bIsDeg_a are false
                if ((bIsDeg_a || bIsDeg_b) == false)
                {
                    bool bOrientA = (pTriInfos[t].iFlag & ORIENT_PRESERVING) != 0 ? true : false;
                    bool bOrientB = (pTriInfos[t + 1].iFlag & ORIENT_PRESERVING) != 0 ? true : false;
                    // if this happens the quad has extremely bad mapping!!
                    if (bOrientA != bOrientB)
                    {
                        //printf("found quad with bad mapping\n");
                        bool bChooseOrientFirstTri = false;
                        if ((pTriInfos[t + 1].iFlag & GROUP_WITH_ANY) != 0) bChooseOrientFirstTri = true;
                        else if (CalcTexArea(pContext, &piTriListIn[t * 3 + 0]) >=
                                 CalcTexArea(pContext, &piTriListIn[(t + 1) * 3 + 0]))
                            bChooseOrientFirstTri = true;

                        // force match
                        {
                            int t0 = bChooseOrientFirstTri ? t : (t + 1);
                            int t1 = bChooseOrientFirstTri ? (t + 1) : t;
                            pTriInfos[t1].iFlag &= (~ORIENT_PRESERVING); // clear first
                            pTriInfos[t1].iFlag |= (pTriInfos[t0].iFlag & ORIENT_PRESERVING); // copy bit
                        }
                    }
                }

                t += 2;
            }
            else
                ++t;
        }

        // match up edge pairs
        {
            SEdge* pEdges = (SEdge*)malloc(sizeof(SEdge) * iNrTrianglesIn * 3);
            if (pEdges == null)
                BuildNeighborsSlow(pTriInfos, piTriListIn, iNrTrianglesIn);
            else
            {
                BuildNeighborsFast(pTriInfos, pEdges, piTriListIn, iNrTrianglesIn);

                free(pEdges);
            }
        }
    }

/////////////////////////////////////////////////////////////////////////////////////////////////////
/////////////////////////////////////////////////////////////////////////////////////////////////////

    static int Build4RuleGroups(STriInfo* pTriInfos
        , SGroup* pGroups, int* piGroupTrianglesBuffer, int* piTriListIn
        , int iNrTrianglesIn)
    {
        int iNrMaxGroups = iNrTrianglesIn * 3;
        int iNrActiveGroups = 0;
        int iOffset = 0, f = 0, i = 0;
        //(void)iNrMaxGroups; /* quiet warnings in non debug mode */
        for (f = 0; f < iNrTrianglesIn; f++)
        {
            for (i = 0; i < 3; i++)
            {
                // if not assigned to a group
                if ((pTriInfos[f].iFlag & GROUP_WITH_ANY) == 0 && pTriInfos[f].AssignedGroup[i] == null)
                {
                    bool bOrPre;
                    int neigh_indexL, neigh_indexR;
                    int vert_index = piTriListIn[f * 3 + i];
                    Debug.Assert(iNrActiveGroups < iNrMaxGroups);
                    pTriInfos[f].AssignedGroup[i] = &pGroups[iNrActiveGroups];
                    pTriInfos[f].AssignedGroup[i]->iVertexRepresentitive = vert_index;
                    pTriInfos[f].AssignedGroup[i]->bOrientPreservering = (pTriInfos[f].iFlag & ORIENT_PRESERVING) != 0;
                    pTriInfos[f].AssignedGroup[i]->iNrFaces = 0;
                    pTriInfos[f].AssignedGroup[i]->pFaceIndices = &piGroupTrianglesBuffer[iOffset];
                    ++iNrActiveGroups;

                    AddTriToGroup(pTriInfos[f].AssignedGroup[i], f);
                    bOrPre = (pTriInfos[f].iFlag & ORIENT_PRESERVING) != 0 ? true : false;
                    neigh_indexL = pTriInfos[f].FaceNeighbors[i];
                    neigh_indexR = pTriInfos[f].FaceNeighbors[i > 0 ? (i - 1) : 2];
                    if (neigh_indexL >= 0) // neighbor
                    {
                        bool bAnswer =
                            AssignRecur(piTriListIn, pTriInfos, neigh_indexL,
                                pTriInfos[f].AssignedGroup[i]);

                        bool bOrPre2 = (pTriInfos[neigh_indexL].iFlag & ORIENT_PRESERVING) != 0 ? true : false;
                        bool bDiff = bOrPre != bOrPre2 ? true : false;
                        Debug.Assert(bAnswer || bDiff);
                        //(void)bAnswer, (void)bDiff; /* quiet warnings in non debug mode */
                    }

                    if (neigh_indexR >= 0) // neighbor
                    {
                        bool bAnswer =
                            AssignRecur(piTriListIn, pTriInfos, neigh_indexR,
                                pTriInfos[f].AssignedGroup[i]);

                        bool bOrPre2 = (pTriInfos[neigh_indexR].iFlag & ORIENT_PRESERVING) != 0 ? true : false;
                        bool bDiff = bOrPre != bOrPre2 ? true : false;
                        Debug.Assert(bAnswer || bDiff);
                        //(void)bAnswer, (void)bDiff; /* quiet warnings in non debug mode */
                    }

                    // update offset
                    iOffset += pTriInfos[f].AssignedGroup[i]->iNrFaces;
                    // since the groups are disjoint a triangle can never
                    // belong to more than 3 groups. Subsequently something
                    // is completely screwed if this assertion ever hits.
                    Debug.Assert(iOffset <= iNrMaxGroups);
                }
            }
        }

        return iNrActiveGroups;
    }

    static void AddTriToGroup(SGroup* pGroup,
        int iTriIndex)
    {
        pGroup->pFaceIndices[pGroup->iNrFaces] = iTriIndex;
        ++pGroup->iNrFaces;
    }

    static bool AssignRecur(
        int* piTriListIn
        , STriInfo* psTriInfos,
        int iMyTriIndex, SGroup* pGroup)
    {
        STriInfo* pMyTriInfo = &psTriInfos[iMyTriIndex];

        // track down vertex
        int iVertRep = pGroup->iVertexRepresentitive;
        int* pVerts = &piTriListIn[3 * iMyTriIndex + 0];
        int i = -1;
        if (pVerts[0] == iVertRep) i = 0;
        else if (pVerts[1] == iVertRep) i = 1;
        else if (pVerts[2] == iVertRep) i = 2;
        Debug.Assert(i >= 0 && i < 3);

        // early out
        if (pMyTriInfo->AssignedGroup[i] == pGroup) return true;
        else if (pMyTriInfo->AssignedGroup[i] != null) return false;
        if ((pMyTriInfo->iFlag & GROUP_WITH_ANY) != 0)
        {
            // first to group with a group-with-anything triangle
            // determines it's orientation.
            // This is the only existing order dependency in the code!!
            if (pMyTriInfo->AssignedGroup[0] == null &&
                pMyTriInfo->AssignedGroup[1] == null &&
                pMyTriInfo->AssignedGroup[2] == null)
            {
                pMyTriInfo->iFlag &= (~ORIENT_PRESERVING);
                pMyTriInfo->iFlag |= (pGroup->bOrientPreservering ? ORIENT_PRESERVING : 0);
            }
        }

        {
            bool bOrient = (pMyTriInfo->iFlag & ORIENT_PRESERVING) != 0 ? true : false;
            if (bOrient != pGroup->bOrientPreservering) return false;
        }

        AddTriToGroup(pGroup, iMyTriIndex);
        pMyTriInfo->AssignedGroup[i] = pGroup;

        {
            int neigh_indexL = pMyTriInfo->FaceNeighbors[i];
            int neigh_indexR = pMyTriInfo->FaceNeighbors[i > 0 ? (i - 1) : 2];
            if (neigh_indexL >= 0)
                AssignRecur(piTriListIn, psTriInfos, neigh_indexL, pGroup);
            if (neigh_indexR >= 0)
                AssignRecur(piTriListIn, psTriInfos, neigh_indexR, pGroup);
        }


        return true;
    }

/////////////////////////////////////////////////////////////////////////////////////////////////////
/////////////////////////////////////////////////////////////////////////////////////////////////////

    static bool GenerateTSpaces(STSpace* psTspace
        , STriInfo* pTriInfos
        , SGroup* pGroups
        ,
        int iNrActiveGroups, int* piTriListIn
        , float fThresCos,
        SMikkTSpaceContext* pContext)
    {
        STSpace* pSubGroupTspace = null;
        SSubGroup* pUniSubGroups = null;
        int* pTmpMembers = null;
        int iMaxNrFaces = 0, iUniqueTspaces = 0, g = 0, i = 0;
        for (g = 0; g < iNrActiveGroups; g++)
            if (iMaxNrFaces < pGroups[g].iNrFaces)
                iMaxNrFaces = pGroups[g].iNrFaces;

        if (iMaxNrFaces == 0) return true;

        // make initial allocations
        pSubGroupTspace = (STSpace*)malloc(sizeof(STSpace) * iMaxNrFaces);
        pUniSubGroups = (SSubGroup*)malloc(sizeof(SSubGroup) * iMaxNrFaces);
        pTmpMembers = (int*)malloc(sizeof(int) * iMaxNrFaces);
        if (pSubGroupTspace == null || pUniSubGroups == null || pTmpMembers == null)
        {
            if (pSubGroupTspace != null) free(pSubGroupTspace);
            if (pUniSubGroups != null) free(pUniSubGroups);
            if (pTmpMembers != null) free(pTmpMembers);
            return false;
        }


        iUniqueTspaces = 0;
        for (g = 0; g < iNrActiveGroups; g++)
        {
            SGroup* pGroup = &pGroups[g];
            int iUniqueSubGroups = 0, s = 0;

            for (i = 0; i < pGroup->iNrFaces; i++) // triangles
            {
                int f = pGroup->pFaceIndices[i]; // triangle number
                int index = -1, iVertIndex = -1, iOF_1 = -1, iMembers = 0, j = 0, l = 0;
                SSubGroup tmp_group;
                bool bFound;
                Vector3 n, vOs, vOt;
                if (pTriInfos[f].AssignedGroup[0] == pGroup) index = 0;
                else if (pTriInfos[f].AssignedGroup[1] == pGroup) index = 1;
                else if (pTriInfos[f].AssignedGroup[2] == pGroup) index = 2;
                Debug.Assert(index >= 0 && index < 3);

                iVertIndex = piTriListIn[f * 3 + index];
                Debug.Assert(iVertIndex == pGroup->iVertexRepresentitive);

                // is normalized already
                n = GetNormal(pContext, iVertIndex);

                // project
                vOs = vsub(pTriInfos[f].vOs, vscale(vdot(n, pTriInfos[f].vOs), n));
                vOt = vsub(pTriInfos[f].vOt, vscale(vdot(n, pTriInfos[f].vOt), n));
                if (VNotZero(vOs)) vOs = Normalize(vOs);
                if (VNotZero(vOt)) vOt = Normalize(vOt);

                // original face number
                iOF_1 = pTriInfos[f].iOrgFaceNumber;

                iMembers = 0;
                for (j = 0; j < pGroup->iNrFaces; j++)
                {
                    int t = pGroup->pFaceIndices[j]; // triangle number
                    int iOF_2 = pTriInfos[t].iOrgFaceNumber;

                    // project
                    Vector3 vOs2 = vsub(pTriInfos[t].vOs, vscale(vdot(n, pTriInfos[t].vOs), n));
                    Vector3 vOt2 = vsub(pTriInfos[t].vOt, vscale(vdot(n, pTriInfos[t].vOt), n));
                    if (VNotZero(vOs2)) vOs2 = Normalize(vOs2);
                    if (VNotZero(vOt2)) vOt2 = Normalize(vOt2);

                    {
                        bool bAny = ((pTriInfos[f].iFlag | pTriInfos[t].iFlag) & GROUP_WITH_ANY) != 0 ? true : false;
                        // make sure triangles which belong to the same quad are joined.
                        bool bSameOrgFace = iOF_1 == iOF_2 ? true : false;

                        float fCosS = vdot(vOs, vOs2);
                        float fCosT = vdot(vOt, vOt2);

                        Debug.Assert(f != t || bSameOrgFace); // sanity check
                        if (bAny || bSameOrgFace || (fCosS > fThresCos && fCosT > fThresCos))
                            pTmpMembers[iMembers++] = t;
                    }
                }

                // sort pTmpMembers
                tmp_group.iNrFaces = iMembers;
                tmp_group.pTriMembers = pTmpMembers;
                if (iMembers > 1)
                {
                    uint uSeed = INTERNAL_RND_SORT_SEED; // could replace with a random seed?
                    QuickSort(pTmpMembers, 0, iMembers - 1, uSeed);
                }

                // look for an existing match
                bFound = false;
                l = 0;
                while (l < iUniqueSubGroups && !bFound)
                {
                    bFound = CompareSubGroups(&tmp_group, &pUniSubGroups[l]);
                    if (!bFound) ++l;
                }

                // assign tangent space index
                Debug.Assert(bFound || l == iUniqueSubGroups);
                //piTempTangIndices[f*3+index] = iUniqueTspaces+l;

                // if no match was found we allocate a new subgroup
                if (!bFound)
                {
                    // insert new subgroup
                    int* pIndices = (int*)malloc(sizeof(int) * iMembers);
                    if (pIndices == null)
                    {
                        // clean up and return false
                        for (s = 0; s < iUniqueSubGroups; s++)
                            free(pUniSubGroups[s].pTriMembers);
                        free(pUniSubGroups);
                        free(pTmpMembers);
                        free(pSubGroupTspace);
                        return false;
                    }

                    pUniSubGroups[iUniqueSubGroups].iNrFaces = iMembers;
                    pUniSubGroups[iUniqueSubGroups].pTriMembers = pIndices;
                    memcpy(pIndices, tmp_group.pTriMembers, iMembers * sizeof(int));
                    pSubGroupTspace[iUniqueSubGroups] =
                        EvalTspace(tmp_group.pTriMembers, iMembers, piTriListIn, pTriInfos, pContext,
                            pGroup->iVertexRepresentitive);
                    ++iUniqueSubGroups;
                }

                // output tspace
                {
                    int iOffs = pTriInfos[f].iTSpacesOffs;
                    int iVert = pTriInfos[f].vert_num[index];
                    STSpace* pTS_out = &psTspace[iOffs + iVert];
                    Debug.Assert(pTS_out->iCounter < 2);
                    Debug.Assert(((pTriInfos[f].iFlag & ORIENT_PRESERVING) != 0) == pGroup->bOrientPreservering);
                    if (pTS_out->iCounter == 1)
                    {
                        *pTS_out = AvgTSpace(pTS_out, &pSubGroupTspace[l]);
                        pTS_out->iCounter = 2; // update counter
                        pTS_out->bOrient = pGroup->bOrientPreservering;
                    }
                    else
                    {
                        Debug.Assert(pTS_out->iCounter == 0);
                        *pTS_out = pSubGroupTspace[l];
                        pTS_out->iCounter = 1; // update counter
                        pTS_out->bOrient = pGroup->bOrientPreservering;
                    }
                }
            }

            // clean up and offset iUniqueTspaces
            for (s = 0; s < iUniqueSubGroups; s++)
                free(pUniSubGroups[s].pTriMembers);
            iUniqueTspaces += iUniqueSubGroups;
        }

        // clean up
        free(pUniSubGroups);
        free(pTmpMembers);
        free(pSubGroupTspace);

        return true;
    }

    static STSpace EvalTspace(int* face_indices
        , int iFaces, int* piTriListIn
        , STriInfo* pTriInfos
        ,
        SMikkTSpaceContext* pContext, int iVertexRepresentitive)
    {
        STSpace res = new STSpace();
        float fAngleSum = 0;
        int face = 0;
        res.vOs.X = 0.0f;
        res.vOs.Y = 0.0f;
        res.vOs.Z = 0.0f;
        res.vOt.X = 0.0f;
        res.vOt.Y = 0.0f;
        res.vOt.Z = 0.0f;
        res.fMagS = 0;
        res.fMagT = 0;

        for (face = 0; face < iFaces; face++)
        {
            int f = face_indices[face];

            // only valid triangles get to add their contribution
            if ((pTriInfos[f].iFlag & GROUP_WITH_ANY) == 0)
            {
                Vector3 n, vOs, vOt, p0, p1, p2, v1, v2;
                float fCos, fAngle, fMagS, fMagT;
                int i = -1, index = -1, i0 = -1, i1 = -1, i2 = -1;
                if (piTriListIn[3 * f + 0] == iVertexRepresentitive) i = 0;
                else if (piTriListIn[3 * f + 1] == iVertexRepresentitive) i = 1;
                else if (piTriListIn[3 * f + 2] == iVertexRepresentitive) i = 2;
                Debug.Assert(i >= 0 && i < 3);

                // project
                index = piTriListIn[3 * f + i];
                n = GetNormal(pContext, index);
                vOs = vsub(pTriInfos[f].vOs, vscale(vdot(n, pTriInfos[f].vOs), n));
                vOt = vsub(pTriInfos[f].vOt, vscale(vdot(n, pTriInfos[f].vOt), n));
                if (VNotZero(vOs)) vOs = Normalize(vOs);
                if (VNotZero(vOt)) vOt = Normalize(vOt);

                i2 = piTriListIn[3 * f + (i < 2 ? (i + 1) : 0)];
                i1 = piTriListIn[3 * f + i];
                i0 = piTriListIn[3 * f + (i > 0 ? (i - 1) : 2)];

                p0 = GetPosition(pContext, i0);
                p1 = GetPosition(pContext, i1);
                p2 = GetPosition(pContext, i2);
                v1 = vsub(p0, p1);
                v2 = vsub(p2, p1);

                // project
                v1 = vsub(v1, vscale(vdot(n, v1), n));
                if (VNotZero(v1)) v1 = Normalize(v1);
                v2 = vsub(v2, vscale(vdot(n, v2), n));
                if (VNotZero(v2)) v2 = Normalize(v2);

                // weight contribution by the angle
                // between the two edge vectors
                fCos = vdot(v1, v2);
                fCos = fCos > 1 ? 1 : (fCos < (-1) ? (-1) : fCos);
                fAngle = (float)MathF.Acos(fCos);
                fMagS = pTriInfos[f].fMagS;
                fMagT = pTriInfos[f].fMagT;

                res.vOs = vadd(res.vOs, vscale(fAngle, vOs));
                res.vOt = vadd(res.vOt, vscale(fAngle, vOt));
                res.fMagS += (fAngle * fMagS);
                res.fMagT += (fAngle * fMagT);
                fAngleSum += fAngle;
            }
        }

        // normalize
        if (VNotZero(res.vOs)) res.vOs = Normalize(res.vOs);
        if (VNotZero(res.vOt)) res.vOt = Normalize(res.vOt);
        if (fAngleSum > 0)
        {
            res.fMagS /= fAngleSum;
            res.fMagT /= fAngleSum;
        }

        return res;
    }

    static bool CompareSubGroups(
        SSubGroup* pg1, SSubGroup* pg2)
    {
        bool bStillSame = true;
        int i = 0;
        if (pg1->iNrFaces != pg2->iNrFaces) return false;
        while (i < pg1->iNrFaces && bStillSame)
        {
            bStillSame = pg1->pTriMembers[i] == pg2->pTriMembers[i] ? true : false;
            if (bStillSame) ++i;
        }

        return bStillSame;
    }

    static void QuickSort(int* pSortBuffer, int iLeft, int iRight, uint uSeed)
    {
        int iL, iR, n, index, iMid, iTmp;

        // Random
        uint t = uSeed & 31;
        t = (uSeed << (int)t) | (uSeed >> (int)(32 - t));
        uSeed = uSeed + t + 3;
        // Random end

        iL = iLeft;
        iR = iRight;
        n = (iR - iL) + 1;
        Debug.Assert(n >= 0);
        index = (int)(uSeed % n);

        iMid = pSortBuffer[index + iL];


        do
        {
            while (pSortBuffer[iL] < iMid)
                ++iL;
            while (pSortBuffer[iR] > iMid)
                --iR;

            if (iL <= iR)
            {
                iTmp = pSortBuffer[iL];
                pSortBuffer[iL] = pSortBuffer[iR];
                pSortBuffer[iR] = iTmp;
                ++iL;
                --iR;
            }
        } while (iL <= iR);

        if (iLeft < iR)
            QuickSort(pSortBuffer, iLeft, iR, uSeed);
        if (iL < iRight)
            QuickSort(pSortBuffer, iL, iRight, uSeed);
    }

/////////////////////////////////////////////////////////////////////////////////////////////
/////////////////////////////////////////////////////////////////////////////////////////////

    static void BuildNeighborsFast(STriInfo* pTriInfos, SEdge* pEdges, int* piTriListIn, int iNrTrianglesIn)
    {
        // build array of edges
        uint uSeed = INTERNAL_RND_SORT_SEED; // could replace with a random seed?
        int iEntries = 0, iCurStartIndex = -1, f = 0, i = 0;
        for (f = 0; f < iNrTrianglesIn; f++)
        for (i = 0; i < 3; i++)
        {
            int i0 = piTriListIn[f * 3 + i];
            int i1 = piTriListIn[f * 3 + (i < 2 ? (i + 1) : 0)];
            pEdges[f * 3 + i].i0 = i0 < i1 ? i0 : i1; // put minimum index in i0
            pEdges[f * 3 + i].i1 = !(i0 < i1) ? i0 : i1; // put maximum index in i1
            pEdges[f * 3 + i].f = f; // record face number
        }

        // sort over all edges by i0, this is the pricy one.
        QuickSortEdges(pEdges, 0, iNrTrianglesIn * 3 - 1, 0, uSeed); // sort channel 0 which is i0

        // sub sort over i1, should be fast.
        // could replace this with a 64 bit int sort over (i0,i1)
        // with i0 as msb in the quicksort call above.
        iEntries = iNrTrianglesIn * 3;
        iCurStartIndex = 0;
        for (i = 1; i < iEntries; i++)
        {
            if (pEdges[iCurStartIndex].i0 != pEdges[i].i0)
            {
                int iL = iCurStartIndex;
                int iR = i - 1;
                // int iElems = i-iL;
                iCurStartIndex = i;
                QuickSortEdges(pEdges, iL, iR, 1, uSeed); // sort channel 1 which is i1
            }
        }

        // sub sort over f, which should be fast.
        // this step is to remain compliant with BuildNeighborsSlow() when
        // more than 2 triangles use the same edge (such as a butterfly topology).
        iCurStartIndex = 0;
        for (i = 1; i < iEntries; i++)
        {
            if (pEdges[iCurStartIndex].i0 != pEdges[i].i0 || pEdges[iCurStartIndex].i1 != pEdges[i].i1)
            {
                int iL = iCurStartIndex;
                int iR = i - 1;
                // int iElems = i-iL;
                iCurStartIndex = i;
                QuickSortEdges(pEdges, iL, iR, 2, uSeed); // sort channel 2 which is f
            }
        }

        // pair up, adjacent triangles
        for (i = 0; i < iEntries; i++)
        {
            int i0 = pEdges[i].i0;
            int i1 = pEdges[i].i1;
            f = pEdges[i].f;
            bool bUnassigned_A;

            int i0_A, i1_A;
            int edgenum_A, edgenum_B = 0; // 0,1 or 2
            GetEdge(&i0_A, &i1_A, &edgenum_A, &piTriListIn[f * 3], i0, i1); // resolve index ordering and edge_num
            bUnassigned_A = pTriInfos[f].FaceNeighbors[edgenum_A] == -1 ? true : false;

            if (bUnassigned_A)
            {
                // get true index ordering
                int j = i + 1, t;
                bool bNotFound = true;
                while (j < iEntries && i0 == pEdges[j].i0 && i1 == pEdges[j].i1 && bNotFound)
                {
                    bool bUnassigned_B;
                    int i0_B, i1_B;
                    t = pEdges[j].f;
                    // flip i0_B and i1_B
                    GetEdge(&i1_B, &i0_B, &edgenum_B, &piTriListIn[t * 3], pEdges[j].i0,
                        pEdges[j].i1); // resolve index ordering and edge_num
                    //Debug.Assert(!(i0_A==i1_B && i1_A==i0_B));
                    bUnassigned_B = pTriInfos[t].FaceNeighbors[edgenum_B] == -1 ? true : false;
                    if (i0_A == i0_B && i1_A == i1_B && bUnassigned_B)
                        bNotFound = false;
                    else
                        ++j;
                }

                if (!bNotFound)
                {
                    t = pEdges[j].f;
                    pTriInfos[f].FaceNeighbors[edgenum_A] = t;
                    //Debug.Assert(pTriInfos[t].FaceNeighbors[edgenum_B]==-1);
                    pTriInfos[t].FaceNeighbors[edgenum_B] = f;
                }
            }
        }
    }

    static void BuildNeighborsSlow(STriInfo* pTriInfos
        , int* piTriListIn
        , int iNrTrianglesIn)
    {
        int f = 0, i = 0;
        for (f = 0; f < iNrTrianglesIn; f++)
        {
            for (i = 0; i < 3; i++)
            {
                // if unassigned
                if (pTriInfos[f].FaceNeighbors[i] == -1)
                {
                    int i0_A = piTriListIn[f * 3 + i];
                    int i1_A = piTriListIn[f * 3 + (i < 2 ? (i + 1) : 0)];

                    // search for a neighbor
                    bool bFound = false;
                    int t = 0, j = 0;
                    while (!bFound && t < iNrTrianglesIn)
                    {
                        if (t != f)
                        {
                            j = 0;
                            while (!bFound && j < 3)
                            {
                                // in rev order
                                int i1_B = piTriListIn[t * 3 + j];
                                int i0_B = piTriListIn[t * 3 + (j < 2 ? (j + 1) : 0)];
                                //Debug.Assert(!(i0_A==i1_B && i1_A==i0_B));
                                if (i0_A == i0_B && i1_A == i1_B)
                                    bFound = true;
                                else
                                    ++j;
                            }
                        }

                        if (!bFound) ++t;
                    }

                    // assign neighbors
                    if (bFound)
                    {
                        pTriInfos[f].FaceNeighbors[i] = t;
                        //Debug.Assert(pTriInfos[t].FaceNeighbors[j]==-1);
                        pTriInfos[t].FaceNeighbors[j] = f;
                    }
                }
            }
        }
    }

    static void QuickSortEdges(SEdge* pSortBuffer, int iLeft, int iRight, int channel, uint uSeed)
    {
        uint t;
        int iL, iR, n, index, iMid;

        // early out
        SEdge sTmp;
        int iElems = iRight - iLeft + 1;
        if (iElems < 2) return;
        else if (iElems == 2)
        {
            if (pSortBuffer[iLeft].array[channel] > pSortBuffer[iRight].array[channel])
            {
                sTmp = pSortBuffer[iLeft];
                pSortBuffer[iLeft] = pSortBuffer[iRight];
                pSortBuffer[iRight] = sTmp;
            }

            return;
        }

        // Random
        t = uSeed & 31;
        t = (uSeed << (int)t) | (uSeed >> (int)(32 - t));
        uSeed = uSeed + t + 3;
        // Random end

        iL = iLeft;
        iR = iRight;
        n = (iR - iL) + 1;
        Debug.Assert(n >= 0);
        index = (int)(uSeed % n);

        iMid = pSortBuffer[index + iL].array[channel];

        do
        {
            while (pSortBuffer[iL].array[channel] < iMid)
                ++iL;
            while (pSortBuffer[iR].array[channel] > iMid)
                --iR;

            if (iL <= iR)
            {
                sTmp = pSortBuffer[iL];
                pSortBuffer[iL] = pSortBuffer[iR];
                pSortBuffer[iR] = sTmp;
                ++iL;
                --iR;
            }
        } while (iL <= iR);

        if (iLeft < iR)
            QuickSortEdges(pSortBuffer, iLeft, iR, channel, uSeed);
        if (iL < iRight)
            QuickSortEdges(pSortBuffer, iL, iRight, channel, uSeed);
    }

// resolve ordering and edge number
    static void GetEdge(int* i0_out, int* i1_out, int* edgenum_out,
        int* indices, int i0_in, int i1_in)
    {
        *edgenum_out = -1;

        // test if first index is on the edge
        if (indices[0] == i0_in || indices[0] == i1_in)
        {
            // test if second index is on the edge
            if (indices[1] == i0_in || indices[1] == i1_in)
            {
                edgenum_out[0] = 0; // first edge
                i0_out[0] = indices[0];
                i1_out[0] = indices[1];
            }
            else
            {
                edgenum_out[0] = 2; // third edge
                i0_out[0] = indices[2];
                i1_out[0] = indices[0];
            }
        }
        else
        {
            // only second and third index is on the edge
            edgenum_out[0] = 1; // second edge
            i0_out[0] = indices[1];
            i1_out[0] = indices[2];
        }
    }


/////////////////////////////////////////////////////////////////////////////////////////////
/////////////////////////////////// Degenerate triangles ////////////////////////////////////

    static void DegenPrologue(STriInfo* pTriInfos, int* piTriList_out, int iNrTrianglesIn, int iTotTris)
    {
        int iNextGoodTriangleSearchIndex = -1;
        bool bStillFindingGoodOnes;

        // locate quads with only one good triangle
        int t = 0;
        while (t < (iTotTris - 1))
        {
            int iFO_a = pTriInfos[t].iOrgFaceNumber;
            int iFO_b = pTriInfos[t + 1].iOrgFaceNumber;
            if (iFO_a == iFO_b) // this is a quad
            {
                bool bIsDeg_a = (pTriInfos[t].iFlag & MARK_DEGENERATE) != 0 ? true : false;
                bool bIsDeg_b = (pTriInfos[t + 1].iFlag & MARK_DEGENERATE) != 0 ? true : false;
                if ((bIsDeg_a ^ bIsDeg_b) != false)
                {
                    pTriInfos[t].iFlag |= QUAD_ONE_DEGEN_TRI;
                    pTriInfos[t + 1].iFlag |= QUAD_ONE_DEGEN_TRI;
                }

                t += 2;
            }
            else
                ++t;
        }

        // reorder list so all degen triangles are moved to the back
        // without reordering the good triangles
        iNextGoodTriangleSearchIndex = 1;
        t = 0;
        bStillFindingGoodOnes = true;
        while (t < iNrTrianglesIn && bStillFindingGoodOnes)
        {
            bool bIsGood = (pTriInfos[t].iFlag & MARK_DEGENERATE) == 0 ? true : false;
            if (bIsGood)
            {
                if (iNextGoodTriangleSearchIndex < (t + 2))
                    iNextGoodTriangleSearchIndex = t + 2;
            }
            else
            {
                int t0, t1;
                // search for the first good triangle.
                bool bJustADegenerate = true;
                while (bJustADegenerate && iNextGoodTriangleSearchIndex < iTotTris)
                {
                    bool bIsGood2 = (pTriInfos[iNextGoodTriangleSearchIndex].iFlag & MARK_DEGENERATE) == 0
                        ? true
                        : false;
                    if (bIsGood2) bJustADegenerate = false;
                    else ++iNextGoodTriangleSearchIndex;
                }

                t0 = t;
                t1 = iNextGoodTriangleSearchIndex;
                ++iNextGoodTriangleSearchIndex;
                Debug.Assert(iNextGoodTriangleSearchIndex > (t + 1));

                // swap triangle t0 and t1
                if (!bJustADegenerate)
                {
                    int i = 0;
                    for (i = 0; i < 3; i++)
                    {
                        int index = piTriList_out[t0 * 3 + i];
                        piTriList_out[t0 * 3 + i] = piTriList_out[t1 * 3 + i];
                        piTriList_out[t1 * 3 + i] = index;
                    }

                    {
                        STriInfo tri_info = pTriInfos[t0];
                        pTriInfos[t0] = pTriInfos[t1];
                        pTriInfos[t1] = tri_info;
                    }
                }
                else
                    bStillFindingGoodOnes = false; // this is not supposed to happen
            }

            if (bStillFindingGoodOnes) ++t;
        }

        Debug.Assert(bStillFindingGoodOnes); // code will still work.
        Debug.Assert(iNrTrianglesIn == t);
    }

    static void DegenEpilogue(STSpace* psTspace, STriInfo* pTriInfos, int* piTriListIn, SMikkTSpaceContext* pContext,
        int iNrTrianglesIn, int iTotTris)
    {
        int t = 0, i = 0;
        // deal with degenerate triangles
        // punishment for degenerate triangles is O(N^2)
        for (t = iNrTrianglesIn; t < iTotTris; t++)
        {
            // degenerate triangles on a quad with one good triangle are skipped
            // here but processed in the next loop
            bool bSkip = (pTriInfos[t].iFlag & QUAD_ONE_DEGEN_TRI) != 0 ? true : false;

            if (!bSkip)
            {
                for (i = 0; i < 3; i++)
                {
                    int index1 = piTriListIn[t * 3 + i];
                    // search through the good triangles
                    bool bNotFound = true;
                    int j = 0;
                    while (bNotFound && j < (3 * iNrTrianglesIn))
                    {
                        int index2 = piTriListIn[j];
                        if (index1 == index2) bNotFound = false;
                        else ++j;
                    }

                    if (!bNotFound)
                    {
                        int iTri = j / 3;
                        int iVert = j % 3;
                        int iSrcVert = pTriInfos[iTri].vert_num[iVert];
                        int iSrcOffs = pTriInfos[iTri].iTSpacesOffs;
                        int iDstVert = pTriInfos[t].vert_num[i];
                        int iDstOffs = pTriInfos[t].iTSpacesOffs;

                        // copy tspace
                        psTspace[iDstOffs + iDstVert] = psTspace[iSrcOffs + iSrcVert];
                    }
                }
            }
        }

        // deal with degenerate quads with one good triangle
        for (t = 0; t < iNrTrianglesIn; t++)
        {
            // this triangle belongs to a quad where the
            // other triangle is degenerate
            if ((pTriInfos[t].iFlag & QUAD_ONE_DEGEN_TRI) != 0)
            {
                Vector3 vDstP;
                int iOrgF = -1;
                bool bNotFound;
                byte* pV = pTriInfos[t].vert_num;
                int iFlag = (1 << pV[0]) | (1 << pV[1]) | (1 << pV[2]);
                int iMissingIndex = 0;
                if ((iFlag & 2) == 0) iMissingIndex = 1;
                else if ((iFlag & 4) == 0) iMissingIndex = 2;
                else if ((iFlag & 8) == 0) iMissingIndex = 3;

                iOrgF = pTriInfos[t].iOrgFaceNumber;
                vDstP = GetPosition(pContext, MakeIndex(iOrgF, iMissingIndex));
                bNotFound = true;
                i = 0;
                while (bNotFound && i < 3)
                {
                    int iVert = pV[i];
                    Vector3 vSrcP = GetPosition(pContext, MakeIndex(iOrgF, iVert));
                    if (veq(vSrcP, vDstP) == true)
                    {
                        int iOffs = pTriInfos[t].iTSpacesOffs;
                        psTspace[iOffs + iMissingIndex] = psTspace[iOffs + iVert];
                        bNotFound = false;
                    }
                    else
                        ++i;
                }

                Debug.Assert(!bNotFound);
            }
        }
    }
}