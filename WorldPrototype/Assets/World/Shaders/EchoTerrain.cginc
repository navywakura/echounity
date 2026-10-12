#ifndef ECHO_TERRAIN_INCLUDED
#define ECHO_TERRAIN_INCLUDED
#include "UnityCG.cginc"
#include "Lighting.cginc"
#include "AutoLight.cginc"
#include "EchoCutaway.cginc"

sampler2D _GrassTex, _RockTex, _SnowTex, _BarkTex, _MetalTex, _CrateTex;
float4 _TerrainTextures, _DetailTextures, _EchoTextureOffset;
float _TextureScale;
fixed4 _Color;
half _Ambient, _EmissionStrength;
struct EchoTerrainVertex
{
    float4 vertex : POSITION;
    float3 normal : NORMAL;
    fixed4 color : COLOR;
    float2 material : TEXCOORD0;
};
struct EchoTerrainFragment
{
    float4 pos : SV_POSITION;
    float3 worldPosition : TEXCOORD0;
    half3 worldNormal : TEXCOORD1;
    float3 texturePosition : TEXCOORD2;
    float2 material : TEXCOORD3;
    fixed4 color : COLOR0;
    UNITY_FOG_COORDS(4)
    UNITY_SHADOW_COORDS(5)
    half3 vertexLight : TEXCOORD6;
};
EchoTerrainFragment EchoTerrainVert(EchoTerrainVertex v)
{
    EchoTerrainFragment o;
    UNITY_INITIALIZE_OUTPUT(EchoTerrainFragment, o);
    o.pos = UnityObjectToClipPos(v.vertex);
    o.worldPosition = mul(unity_ObjectToWorld, v.vertex).xyz;
    o.worldNormal = UnityObjectToWorldNormal(v.normal);
    // Integer column phase: triplanar textures do not swim when the world rebases.
    o.texturePosition = v.vertex.xyz + _EchoTextureOffset.xyz;
    o.material = v.material;
    o.color = v.color;
    #ifdef VERTEXLIGHT_ON
    o.vertexLight = Shade4PointLights(unity_4LightPosX0, unity_4LightPosY0, unity_4LightPosZ0,
        unity_LightColor[0].rgb, unity_LightColor[1].rgb, unity_LightColor[2].rgb, unity_LightColor[3].rgb,
        unity_4LightAtten0, o.worldPosition, o.worldNormal);
    #endif
    UNITY_TRANSFER_FOG(o, o.pos);
    UNITY_TRANSFER_SHADOW(o, float2(0, 0));
    return o;
}
half3 EchoTriplanar(sampler2D source, float3 position, half3 weights)
{
    float3 p = position * _TextureScale;
    return tex2D(source, p.zy).rgb * weights.x + tex2D(source, p.xz).rgb * weights.y + tex2D(source, p.xy).rgb * weights.z;
}
half3 EchoTerrainAlbedo(EchoTerrainFragment i)
{
    half3 weights = pow(abs(normalize(i.worldNormal)), 4);
    weights /= max(weights.x + weights.y + weights.z, 0.0001);
    half id = i.material.x;
    half3 albedo = i.color.rgb;
    // Missing resources retain earthy vertex colors rather than white placeholders.
    if (id < 1.5 && _TerrainTextures.x > 0.5)
        albedo = EchoTriplanar(_GrassTex, i.texturePosition, weights) * half3(0.76, 0.84, 0.67) * i.material.y;
    else if (id > 1.5 && id < 2.5 && _TerrainTextures.y > 0.5)
        albedo = EchoTriplanar(_RockTex, i.texturePosition, weights) * 0.8 * i.material.y;
    else if (id > 2.5 && id < 3.5 && _DetailTextures.x > 0.5)
        albedo = EchoTriplanar(_MetalTex, i.texturePosition, weights) * half3(0.70, 0.76, 0.8) * i.material.y;
    else if (id > 5.5 && id < 6.5 && _TerrainTextures.w > 0.5)
        albedo = EchoTriplanar(_BarkTex, i.texturePosition, weights) * 0.8 * i.material.y;
    else if (id > 6.5 && id < 7.5 && _TerrainTextures.x > 0.5)
        albedo = EchoTriplanar(_GrassTex, i.texturePosition, weights) * half3(0.50, 0.68, 0.43) * i.material.y;
    else if (id > 7.5 && id < 8.5 && _TerrainTextures.y > 0.5)
        albedo = EchoTriplanar(_RockTex, i.texturePosition, weights) * half3(1.02, 0.87, 0.57) * i.material.y;
    else if (id > 8.5 && id < 9.5 && _TerrainTextures.z > 0.5)
        albedo = EchoTriplanar(_SnowTex, i.texturePosition, weights) * 0.92 * i.material.y;
    else if (id > 9.5 && id < 10.5 && _DetailTextures.y > 0.5)
        albedo = EchoTriplanar(_CrateTex, i.texturePosition, weights) * 0.8 * i.material.y;
    return albedo * _Color.rgb;
}
fixed4 EchoTerrainBase(EchoTerrainFragment i) : SV_Target
{
    EchoCutaway(i.worldPosition, i.pos.xy);
    half3 normal = normalize(i.worldNormal);
    half3 lightDirection = normalize(UnityWorldSpaceLightDir(i.worldPosition));
    UNITY_LIGHT_ATTENUATION(attenuation, i, i.worldPosition);
    half floorLight = min(_Ambient, 0.025) * lerp(0.5, 1.0, saturate(_EchoDaylight));
    half3 ambient = max(ShadeSH9(half4(normal, 1)), half3(floorLight, floorLight, floorLight));
    half3 albedo = EchoTerrainAlbedo(i);
    half3 lighting = ambient + i.vertexLight + _LightColor0.rgb * saturate(dot(normal, lightDirection)) * attenuation;
    fixed4 result = fixed4(albedo * lighting + albedo * i.color.a * _EmissionStrength, 1);
    UNITY_APPLY_FOG(i.fogCoord, result);
    return result;
}
fixed4 EchoTerrainAdd(EchoTerrainFragment i) : SV_Target
{
    EchoCutaway(i.worldPosition, i.pos.xy);
    half3 normal = normalize(i.worldNormal);
    UNITY_LIGHT_ATTENUATION(attenuation, i, i.worldPosition);
    half diffuse = saturate(dot(normal, normalize(UnityWorldSpaceLightDir(i.worldPosition))));
    fixed4 result = fixed4(EchoTerrainAlbedo(i) * _LightColor0.rgb * diffuse * attenuation, 0);
    UNITY_APPLY_FOG_COLOR(i.fogCoord, result, fixed4(0, 0, 0, 0));
    return result;
}
#endif
