Shader "EchoWorld/VoxelTerrain"
{
    Properties
    {
        _Color ("Tint", Color) = (1, 1, 1, 1)
        _Ambient ("Ambient floor", Range(0, 0.025)) = 0.018
        _EmissionStrength ("Structure glow", Range(0, 1)) = 0.12
        _TextureScale ("Texture repeats per voxel", Float) = 0.5
        _GrassTex ("Grass", 2D) = "white" {}
        _RockTex ("Rock", 2D) = "white" {}
        _SnowTex ("Snow", 2D) = "white" {}
        _BarkTex ("Bark", 2D) = "white" {}
        _MetalTex ("Metal", 2D) = "white" {}
        _CrateTex ("Crate", 2D) = "white" {}
        [HideInInspector] _TerrainTextures ("Available grass/rock/snow/bark", Vector) = (0, 0, 0, 0)
        [HideInInspector] _DetailTextures ("Available metal/crate", Vector) = (0, 0, 0, 0)
        [HideInInspector] _EchoTextureOffset ("Integer column texture phase", Vector) = (0, 0, 0, 0)
    }
    SubShader
    {
        Tags { "RenderType" = "Opaque" "Queue" = "Geometry" "DisableBatching" = "True" }
        Cull Back
        ZWrite On
        Pass
        {
            Tags { "LightMode" = "ForwardBase" }
            CGPROGRAM
            #pragma target 3.0
            #pragma vertex EchoTerrainVert
            #pragma fragment EchoTerrainBase
            #pragma multi_compile_fwdbase nolightmap nodirlightmap nodynlightmap
            #pragma multi_compile_fog
            #include "EchoTerrain.cginc"
            ENDCG
        }
        Pass
        {
            Tags { "LightMode" = "ForwardAdd" }
            Blend One One
            ZWrite Off
            CGPROGRAM
            #pragma target 3.0
            #pragma vertex EchoTerrainVert
            #pragma fragment EchoTerrainAdd
            #pragma multi_compile_fwdadd_fullshadows
            #pragma multi_compile_fog
            #include "EchoTerrain.cginc"
            ENDCG
        }
        Pass
        {
            Tags { "LightMode" = "ShadowCaster" }
            CGPROGRAM
            #pragma target 3.0
            #pragma vertex vertShadow
            #pragma fragment fragShadow
            #pragma multi_compile_shadowcaster
            #include "UnityCG.cginc"
            struct v2fShadow { V2F_SHADOW_CASTER; };
            v2fShadow vertShadow(appdata_base v)
            {
                v2fShadow o;
                TRANSFER_SHADOW_CASTER_NORMALOFFSET(o);
                return o;
            }
            float4 fragShadow(v2fShadow i) : SV_Target
            {
                SHADOW_CASTER_FRAGMENT(i)
            }
            ENDCG
        }
    }
    Fallback Off
}
