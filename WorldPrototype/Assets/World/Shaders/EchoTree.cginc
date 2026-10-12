#ifndef ECHO_TREE_INCLUDED
#define ECHO_TREE_INCLUDED
#include "UnityCG.cginc"
#include "Lighting.cginc"
#include "AutoLight.cginc"
#include "EchoCutaway.cginc"
sampler2D _MainTex;
half _Cutoff, _Ambient;

struct EchoTreeVertex
{
    float4 vertex : POSITION;
    float2 uv : TEXCOORD0;
    float2 offset : TEXCOORD1;
    float3 normal : NORMAL;
};

void EchoBillboard(inout EchoTreeVertex v, out float3 worldPosition, out half3 worldNormal)
{
    float3 anchor = mul(unity_ObjectToWorld, v.vertex).xyz;
    float3 towardCamera = _EchoCamera.xyz - anchor;
    towardCamera.y = 0;
    if (dot(towardCamera, towardCamera) < 0.0001) towardCamera = float3(0, 0, 1);
    towardCamera = normalize(towardCamera);
    float3 right = float3(towardCamera.z, 0, -towardCamera.x);
    worldPosition = anchor + right * v.offset.x + float3(0, v.offset.y, 0);
    worldNormal = normalize(towardCamera + float3(0, 0.35, 0));
    v.vertex = mul(unity_WorldToObject, float4(worldPosition, 1));
    v.normal = mul((float3x3)unity_WorldToObject, worldNormal);
}

struct EchoTreeFragment
{
    float4 pos : SV_POSITION;
    float2 uv : TEXCOORD0;
    float3 worldPosition : TEXCOORD1;
    half3 worldNormal : TEXCOORD2;
    UNITY_FOG_COORDS(3)
    UNITY_SHADOW_COORDS(4)
    half3 vertexLight : TEXCOORD5;
};
EchoTreeFragment EchoTreeVert(EchoTreeVertex v)
{
    EchoTreeFragment o;
    UNITY_INITIALIZE_OUTPUT(EchoTreeFragment, o);
    EchoBillboard(v, o.worldPosition, o.worldNormal);
    o.pos = UnityObjectToClipPos(v.vertex);
    o.uv = v.uv;
    #ifdef VERTEXLIGHT_ON
    o.vertexLight = Shade4PointLights(unity_4LightPosX0, unity_4LightPosY0, unity_4LightPosZ0,
        unity_LightColor[0].rgb, unity_LightColor[1].rgb, unity_LightColor[2].rgb, unity_LightColor[3].rgb,
        unity_4LightAtten0, o.worldPosition, o.worldNormal);
    #endif
    UNITY_TRANSFER_FOG(o, o.pos);
    UNITY_TRANSFER_SHADOW(o, float2(0, 0));
    return o;
}
fixed4 EchoTreeBase(EchoTreeFragment i) : SV_Target
{
    fixed4 leaf = tex2D(_MainTex, i.uv);
    clip(leaf.a - _Cutoff);
    EchoCutaway(i.worldPosition, i.pos.xy);
    half3 normal = normalize(i.worldNormal);
    UNITY_LIGHT_ATTENUATION(attenuation, i, i.worldPosition);
    half diffuse = 0.2 + 0.8 * abs(dot(normal, normalize(UnityWorldSpaceLightDir(i.worldPosition))));
    half floorLight = min(_Ambient, 0.025) * lerp(0.5, 1.0, saturate(_EchoDaylight));
    half3 ambient = max(ShadeSH9(half4(normal, 1)), half3(floorLight, floorLight, floorLight));
    fixed4 result = fixed4(leaf.rgb * (ambient + i.vertexLight + _LightColor0.rgb * diffuse * attenuation), 1);
    UNITY_APPLY_FOG(i.fogCoord, result);
    return result;
}
fixed4 EchoTreeAdd(EchoTreeFragment i) : SV_Target
{
    fixed4 leaf = tex2D(_MainTex, i.uv);
    clip(leaf.a - _Cutoff);
    EchoCutaway(i.worldPosition, i.pos.xy);
    UNITY_LIGHT_ATTENUATION(attenuation, i, i.worldPosition);
    half diffuse = abs(dot(normalize(i.worldNormal), normalize(UnityWorldSpaceLightDir(i.worldPosition))));
    fixed4 result = fixed4(leaf.rgb * _LightColor0.rgb * diffuse * attenuation, 0);
    UNITY_APPLY_FOG_COLOR(i.fogCoord, result, fixed4(0, 0, 0, 0));
    return result;
}
struct EchoTreeShadow { V2F_SHADOW_CASTER; float2 uv : TEXCOORD1; };
EchoTreeShadow EchoTreeShadowVert(EchoTreeVertex v)
{
    EchoTreeShadow o;
    float3 worldPosition;
    half3 worldNormal;
    EchoBillboard(v, worldPosition, worldNormal);
    o.uv = v.uv;
    TRANSFER_SHADOW_CASTER_NORMALOFFSET(o);
    return o;
}
float4 EchoTreeShadowFrag(EchoTreeShadow i) : SV_Target
{
    clip(tex2D(_MainTex, i.uv).a - _Cutoff);
    SHADOW_CASTER_FRAGMENT(i)
}
#endif
