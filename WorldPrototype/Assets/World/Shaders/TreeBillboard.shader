Shader "EchoWorld/TreeBillboard"
{
    Properties
    {
        _MainTex ("Original tree atlas", 2D) = "white" {}
        _Cutoff ("Leaf alpha cutoff", Range(0, 1)) = 0.4
        _Ambient ("Ambient floor", Range(0, 0.025)) = 0.018
    }
    SubShader
    {
        Tags { "Queue" = "AlphaTest" "RenderType" = "TransparentCutout" "DisableBatching" = "True" }
        Cull Off
        ZWrite On
        Pass
        {
            Tags { "LightMode" = "ForwardBase" }
            CGPROGRAM
            #pragma target 3.0
            #pragma vertex EchoTreeVert
            #pragma fragment EchoTreeBase
            #pragma multi_compile_fwdbase nolightmap nodirlightmap nodynlightmap
            #pragma multi_compile_fog
            #include "EchoTree.cginc"
            ENDCG
        }
        Pass
        {
            Tags { "LightMode" = "ForwardAdd" }
            Blend One One
            ZWrite Off
            CGPROGRAM
            #pragma target 3.0
            #pragma vertex EchoTreeVert
            #pragma fragment EchoTreeAdd
            #pragma multi_compile_fwdadd_fullshadows
            #pragma multi_compile_fog
            #include "EchoTree.cginc"
            ENDCG
        }
        Pass
        {
            Tags { "LightMode" = "ShadowCaster" }
            CGPROGRAM
            #pragma target 3.0
            #pragma vertex EchoTreeShadowVert
            #pragma fragment EchoTreeShadowFrag
            #pragma multi_compile_shadowcaster
            #include "EchoTree.cginc"
            ENDCG
        }
    }
    Fallback Off
}
