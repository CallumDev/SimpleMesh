using System;
using System.Diagnostics;

namespace SimpleMesh.Convex.Quickhull;

static class QHDebug
{
    [Conditional("QHDEBUG")]
    public static void debug(string str) => Console.WriteLine(str);

    #if QHDEBUG
    public const bool IsDebug = true;
    #else
    public const bool IsDebug = false;
    #endif
}
