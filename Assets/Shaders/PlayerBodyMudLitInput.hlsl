#ifndef RQ_PLAYER_BODY_MUD_LIT_INPUT_INCLUDED
#define RQ_PLAYER_BODY_MUD_LIT_INPUT_INCLUDED

CBUFFER_START(UnityPerMaterial)
    float4 _BaseMap_ST;
    half4 _BaseColor;
    half _BumpScale;
    half _Metallic;
    half _Smoothness;
    half _MudAmount;
    half4 _MudColor;
    half _MudSmoothness;
    half _MudEdgeSoftness;
CBUFFER_END

#endif
