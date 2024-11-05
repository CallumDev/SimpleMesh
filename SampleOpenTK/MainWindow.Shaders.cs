namespace SampleOpenTK;

partial class MainWindow
{
    private const string VERTEX_SHADER = @"
#version 120
uniform mat4 world;
uniform mat4 viewprojection;
uniform mat4 normalmat;

attribute vec3 v_position;
attribute vec3 v_normal;
attribute vec4 v_diffuse;
attribute vec4 v_tangent;
attribute vec2 v_texture1;
attribute vec2 v_texture2;
attribute vec2 v_texture3;
attribute vec2 v_texture4;

varying vec4 diffuse;
varying vec3 worldpos;
varying vec3 normal;
varying vec2 texcoord[4];
varying mat3 tbn;

uniform sampler2D Texture;

void main()
{
    diffuse = v_diffuse;
    texcoord[0] = v_texture1;
    texcoord[1] = v_texture2;
    texcoord[2] = v_texture3;
    texcoord[3] = v_texture4;
    mat4 mvp = (viewprojection * world);
    vec4 wp = (world * vec4(v_position, 1.0));
    worldpos = wp.xyz / wp.w;
    // Regular normals
    normal = (normalmat * vec4(v_normal, 0.0)).xyz;
    // tbn for normal mapping, in a regular shader this would be optimised out
    vec3 normalW = normalize(vec3(normalmat * vec4(v_normal.xyz, 0.0)));
    vec3 tangentW = normalize(vec3(normalmat * vec4(v_tangent.xyz, 0.0)));
    vec3 bitangentW = cross(normalW, tangentW) * v_tangent.w;
    tbn = mat3(tangentW, bitangentW, normalW);

    gl_Position = mvp * vec4(v_position, 1.0);
}
";

    private const string DIFFUSE_FRAGMENT_SHADER = @"
#version 120
varying vec4 diffuse;
varying vec3 worldpos;

varying vec3 normal;
varying mat3 tbn;
varying vec2 texcoord[4];

uniform vec4 mat_diffuse;
uniform vec3 mat_emissive;
uniform sampler2D mat_texture;
uniform sampler2D mat_emissiveTexture;
uniform sampler2D mat_normalTexture;
uniform int mat_normalMap;
uniform vec3 light_direction;
uniform int texcoord_diffuse;
uniform int texcoord_emissive;
uniform int texcoord_normal;

vec3 getNormal()
{
    if(mat_normalMap == 1) {
         vec3 n = texture2D(mat_normalTexture, texcoord[texcoord_normal]).rgb;
         return normalize(tbn * ((2.0 * n - 1.0)));
    } else {
        return normalize(normal);
    }
}

vec4 toSrgb(vec4 incol)
{
    vec3 srgb = pow(incol.rgb, vec3(1.0/2.2));
    return vec4(srgb, incol.a);
}

void main()
{
    vec3 norm = getNormal();
    vec4 baseColor = texture2D(mat_texture, texcoord[texcoord_diffuse]) * toSrgb(mat_diffuse) * toSrgb(diffuse);
    vec4 sampledEmissive = texture2D(mat_emissiveTexture, texcoord[texcoord_emissive]);
    vec3 emissive = mat_emissive * sampledEmissive.rgb;
    // old-style non-pbr lighting.
    float NdotL = clamp(dot(norm, -light_direction), 0.001, 1.0);
    vec3 col = emissive + NdotL * baseColor.rgb;
    gl_FragColor = vec4(col.rgb, baseColor.a);
}
";

    private const string PBR_FRAGMENT_SHADER = @"
#version 120
varying vec4 diffuse;
varying vec3 worldpos;
varying vec3 normal;
varying mat3 tbn;
varying vec2 texcoord[4];

uniform vec4 mat_diffuse;
uniform vec3 mat_emissive;
uniform sampler2D mat_texture;
uniform sampler2D mat_emissiveTexture;
uniform sampler2D mat_metallicRoughnessTexture;
uniform sampler2D mat_normalTexture;
uniform int mat_normalMap;
uniform float mat_roughness;
uniform float mat_metallic;
uniform int texcoord_diffuse;
uniform int texcoord_emissive;
uniform int texcoord_normal;
uniform int texcoord_metallicRoughness;

uniform vec3 light_direction;
uniform vec3 camera_pos;

vec3 getNormal()
{
    if(mat_normalMap == 1) {
         vec3 n = texture2D(mat_normalTexture, texcoord[texcoord_normal]).rgb;
         return normalize(tbn * ((2.0 * n - 1.0)));
    } else {
        return normalize(normal);
    }
}

// Encapsulate the various inputs used by the various functions in the shading equation
// We store values in this struct to simplify the integration of alternative implementations
// of the shading terms, outlined in the Readme.MD Appendix.
struct PBRInfo
{
    float NdotL;                  // cos angle between normal and light direction
    float NdotV;                  // cos angle between normal and view direction
    float NdotH;                  // cos angle between normal and half vector
    float LdotH;                  // cos angle between light direction and half vector
    float VdotH;                  // cos angle between view direction and half vector
    float perceptualRoughness;    // roughness value, as authored by the model creator (input to shader)
    float metalness;              // metallic value at the surface
    vec3 reflectance0;            // full reflectance color (normal incidence angle)
    vec3 reflectance90;           // reflectance color at grazing angle
    float alphaRoughness;         // roughness mapped to a more linear change in the roughness (proposed by [2])
    vec3 diffuseColor;            // color contribution from diffuse lighting
    vec3 specularColor;           // color contribution from specular lighting
};

const float M_PI = 3.141592653589793;
const float c_MinRoughness = 0.04;

// Basic Lambertian diffuse
// Implementation from Lambert's Photometria https://archive.org/details/lambertsphotome00lambgoog
// See also [1], Equation 1
vec3 diffuseLambert(PBRInfo pbrInputs)
{
    return pbrInputs.diffuseColor / M_PI;
}

// The following equation models the Fresnel reflectance term of the spec equation (aka F())
// Implementation of fresnel from [4], Equation 15
vec3 specularReflection(PBRInfo pbrInputs)
{
    return pbrInputs.reflectance0 + (pbrInputs.reflectance90 - pbrInputs.reflectance0) * pow(clamp(1.0 - pbrInputs.VdotH, 0.0, 1.0), 5.0);
}

// This calculates the specular geometric attenuation (aka G()),
// where rougher material will reflect less light back to the viewer.
// This implementation is based on [1] Equation 4, and we adopt their modifications to
// alphaRoughness as input as originally proposed in [2].
float geometricOcclusion(PBRInfo pbrInputs)
{
    float NdotL = pbrInputs.NdotL;
    float NdotV = pbrInputs.NdotV;
    float r = pbrInputs.alphaRoughness;

    float attenuationL = 2.0 * NdotL / (NdotL + sqrt(r * r + (1.0 - r * r) * (NdotL * NdotL)));
    float attenuationV = 2.0 * NdotV / (NdotV + sqrt(r * r + (1.0 - r * r) * (NdotV * NdotV)));
    return attenuationL * attenuationV;
}

// The following equation(s) model the distribution of microfacet normals across the area being drawn (aka D())
// Implementation from ""Average Irregularity Representation of a Roughened Surface for Ray Reflection"" by T. S. Trowbridge, and K. P. Reitz
// Follows the distribution function recommended in the SIGGRAPH 2013 course notes from EPIC Games [1], Equation 3.
float microfacetDistribution(PBRInfo pbrInputs)
{
    float roughnessSq = pbrInputs.alphaRoughness * pbrInputs.alphaRoughness;
    float f = (pbrInputs.NdotH * roughnessSq - pbrInputs.NdotH) * pbrInputs.NdotH + 1.0;
    return roughnessSq / (M_PI * f * f);
}

vec4 SRGBtoLinear(vec4 srgbIn)
{
    vec3 bLess = step(vec3(0.04045),srgbIn.xyz);
    vec3 linOut = mix( srgbIn.xyz/vec3(12.92), pow((srgbIn.xyz+vec3(0.055))/vec3(1.055),vec3(2.4)), bLess );
    return vec4(linOut, srgbIn.a);
}

void main()
{
    vec3 norm = getNormal();

    vec4 roughMap = texture2D(mat_metallicRoughnessTexture, texcoord[texcoord_metallicRoughness]);

    float perceptualRoughness = roughMap.g * mat_roughness;
    float metallic = roughMap.b * mat_metallic;

    perceptualRoughness = clamp(perceptualRoughness, c_MinRoughness, 1.0);
    metallic = clamp(metallic, 0.0, 1.0);
    float alphaRoughness = perceptualRoughness * perceptualRoughness;

    vec4 baseColor = SRGBtoLinear(texture2D(mat_texture, texcoord[texcoord_diffuse])) * mat_diffuse * diffuse;
    
    vec3 f0 = vec3(0.04);
    vec3 diffuseColor = baseColor.rgb * (vec3(1.0) - f0);
    diffuseColor *= 1.0 - metallic;
    vec3 specularColor = mix(f0, baseColor.rgb, metallic);

    // Compute reflectance.
    float reflectance = max(max(specularColor.r, specularColor.g), specularColor.b);

    // For typical incident reflectance range (between 4% to 100%) set the grazing reflectance to 100% for typical fresnel effect.
    // For very low reflectance range on highly diffuse objects (below 4%), incrementally reduce grazing reflecance to 0%.
    float reflectance90 = clamp(reflectance * 25.0, 0.0, 1.0);
    vec3 specularEnvironmentR0 = specularColor.rgb;
    vec3 specularEnvironmentR90 = vec3(1.0, 1.0, 1.0) * reflectance90;

    vec3 n = getNormal();                             // normal at surface point
    vec3 v = normalize(camera_pos - worldpos);        // Vector from surface point to camera
    vec3 l = normalize(light_direction);             // Vector from surface point to light
    vec3 h = normalize(l+v);                          // Half vector between both l and v
    vec3 reflection = -normalize(reflect(v, n));

    float NdotL = clamp(dot(n, l), 0.001, 1.0);
    float NdotV = clamp(abs(dot(n, v)), 0.001, 1.0);
    float NdotH = clamp(dot(n, h), 0.0, 1.0);
    float LdotH = clamp(dot(l, h), 0.0, 1.0);
    float VdotH = clamp(dot(v, h), 0.0, 1.0);

    PBRInfo pbrInputs = PBRInfo(
        NdotL,
        NdotV,
        NdotH,
        LdotH,
        VdotH,
        perceptualRoughness,
        metallic,
        specularEnvironmentR0,
        specularEnvironmentR90,
        alphaRoughness,
        diffuseColor,
        specularColor
    );

    // Calculate the shading terms for the microfacet specular shading model
    vec3 F = specularReflection(pbrInputs);
    float G = geometricOcclusion(pbrInputs);
    float D = microfacetDistribution(pbrInputs);

    // Calculation of analytical lighting contribution
    vec3 diffuseContrib = (1.0 - F) * diffuseLambert(pbrInputs);
    vec3 specContrib = F * G * D / (4.0 * NdotL * NdotV);
    vec3 color = NdotL * vec3(1.0) * (diffuseContrib + specContrib); //lightColor hardcode to 1.0

    vec4 sampledEmissive = SRGBtoLinear(texture2D(mat_emissiveTexture, texcoord[texcoord_emissive]));
    vec4 emissive = vec4(mat_emissive * sampledEmissive.rgb, 1.0);
    
    color += emissive.rgb;

    gl_FragColor = vec4(pow(color,vec3(1.0/2.2)), baseColor.a);
}
";

}