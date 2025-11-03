namespace ConvexHullSample;

partial class MainWindow
{
    private const string MeshVertexShader = @"
#version 120
uniform mat4 world;
uniform mat4 viewprojection;
uniform mat4 normalmat;

attribute vec3 v_position;
attribute vec3 v_normal;

varying vec4 diffuse;
varying vec3 worldpos;
varying vec3 normal;

void main()
{
    diffuse = vec4(1.0);
    mat4 mvp = (viewprojection * world);
    vec4 wp = (world * vec4(v_position, 1.0));
    worldpos = wp.xyz / wp.w;
    // Regular normals
    normal = (normalmat * vec4(v_normal, 0.0)).xyz;
    gl_Position = mvp * vec4(v_position, 1.0);
}
";

    private const string MeshFragmentShader = @"
#version 120
varying vec4 diffuse;
varying vec3 worldpos;
varying vec3 normal;
uniform vec3 light_direction;
uniform vec3 light_direction_2;


void main()
{
    vec3 norm = normalize(normal);
    vec4 baseColor = diffuse;
    // old-style non-pbr lighting.
    float NdotL = clamp(dot(norm, -light_direction), 0.001, 1.0);
    vec3 col = NdotL * baseColor.rgb;
    col += vec3(0.2) * baseColor.rgb; //ambient term

    gl_FragColor = vec4(col.rgb, baseColor.a);
}
";

}