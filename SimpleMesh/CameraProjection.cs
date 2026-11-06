using System;
using System.Numerics;

namespace SimpleMesh;

public class CameraProjection
{
    /// <summary>
    /// Specifies if the projection is Perspective or Orthographic.
    /// </summary>
    public ProjectionKind Kind;
    /// <summary>
    /// Orthographic only: The floating-point horizontal magnification of the view. (xmag)
    /// </summary>
    public float X;
    /// <summary>
    /// Perspective: The floating-point vertical field of view in radians.
    /// Orthographic: The floating-point vertical magnification of the view. (ymag)
    /// </summary>
    public float Y;
    /// <summary>
    /// Perspective: The floating-point aspect ratio of the field of view.  When this is undefined (0), the aspect ratio of the canvas is used.
    /// </summary>
    public float AspectRatio;
    /// <summary>
    /// The floating-point distance to the near clipping plane.  zfar must be greater than znear.
    /// </summary>
    public float ZNear;
    /// <summary>
    /// The floating-point distance to the far clipping plane.  zfar must be greater than znear.
    /// </summary>
    public float ZFar;

    public CameraProjection Clone() => (CameraProjection)MemberwiseClone();


    /// <summary>
    /// Creates a matrix from this camera info
    /// </summary>
    /// <param name="width">The viewport width, used when AspectRatio is not provided.</param>
    /// <param name="height">The viewport height, used when AspectRatio is not provided.</param>
    /// <returns></returns>
    public Matrix4x4 CreateProjectionMatrix(float width, float height)
    {
        if (Kind == ProjectionKind.Perspective)
        {
            return CreatePerspectiveMatrix(
                AspectRatio == 0 ? width / height : AspectRatio,
                Y, ZNear, ZFar
            );
        }
        else
        {
            return CreateOrthographicMatrix(X, Y, ZNear, ZFar);
        }
    }

    static Matrix4x4 CreateOrthographicMatrix(float xmag, float ymag, float znear, float zfar)
    {
        var d = znear - zfar;

        var x = 1 / xmag;
        var y = 1 / ymag;
        var z = 2 / d;
        var w = (zfar + znear) / d;

        return new Matrix4x4
        (
            x, 0, 0, 0,
            0, y, 0, 0,
            0, 0, z, 0,
            0, 0, w, 1
        );
    }

    static Matrix4x4 CreatePerspectiveMatrix(float aspectRatio, float yfov, float znear,
        float zfar = float.PositiveInfinity)
    {
       var v = (float)Math.Tan(0.5 * yfov);
       var h = aspectRatio * v;

       var x = 1 / h;
       var y = 1 / v;
       var z = -1f;
       var w = -2 * znear;

       if (!float.IsPositiveInfinity(zfar))
       {
           var d = znear - zfar;
           z = (zfar + znear) / d;
           w = (2f * zfar * znear) / d;
       }

       return new Matrix4x4
       (
           x, 0, 0, 0,
           0, y, 0, 0,
           0, 0, z, -1,
           0, 0, w, 0
       );
    }
}

public enum ProjectionKind
{
    Perspective,
    Orthographic
}
