Shader "Phase0/Spine/Sprite/Unlit Hatch Overlay"
{
	Properties
	{
		_MainTex ("Main Texture", 2D) = "white" {}
		_Color ("Color", Color) = (1,1,1,1)

		[MaterialToggle] PixelSnap ("Pixel snap", Float) = 0

		_ZWrite ("Depth Write", Float) = 0.0
		_Cutoff ("Depth alpha cutoff", Range(0,1)) = 0.0
		_ShadowAlphaCutoff ("Shadow alpha cutoff", Range(0,1)) = 0.1
		_CustomRenderQueue ("Custom Render Queue", Float) = 0.0

		_OverlayColor ("Overlay Color", Color) = (0,0,0,0)
		_Hue("Hue", Range(-0.5,0.5)) = 0.0
		_Saturation("Saturation", Range(0,2)) = 1.0
		_Brightness("Brightness", Range(0,2)) = 1.0

		_BlendTex ("Blend Texture", 2D) = "white" {}
		_BlendAmount ("Blend", Range(0,1)) = 0.0

		[MaterialToggle(_TINT_BLACK_ON)]  _TintBlack("Tint Black", Float) = 0
		_Black("Dark Color", Color) = (0,0,0,0)

		_HatchStrength ("Hatch Strength", Range(0,1)) = 0
		_HatchColor ("Hatch Color", Color) = (0,0,0,1)
		_HatchScale ("Hatch Scale", Range(0.1,20)) = 8
		_HatchWidth ("Hatch Width", Range(0.01,0.5)) = 0.18
		_HatchAngleDeg ("Hatch Angle (Deg)", Range(0,180)) = 45
		_HatchOpacity ("Hatch Opacity", Range(0,1)) = 0.8
		_HatchScrollVelocity ("Hatch Scroll Velocity", Vector) = (0,0,0,0)
		_HatchUseWorldSpace ("Hatch Use World Space", Float) = 0

		[HideInInspector] _SrcBlend ("__src", Float) = 1.0
		[HideInInspector] _DstBlend ("__dst", Float) = 0.0
		[HideInInspector] _RenderQueue ("__queue", Float) = 0.0
		[HideInInspector] _Cull ("__cull", Float) = 0.0
		[HideInInspector] _StencilRef("Stencil Reference", Float) = 1.0
		[HideInInspector][Enum(UnityEngine.Rendering.CompareFunction)] _StencilComp("Stencil Comparison", Float) = 8 // Set to Always as default

		// Outline properties are drawn via custom editor.
		[HideInInspector] _OutlineWidth("Outline Width", Range(0,8)) = 3.0
		[HideInInspector][MaterialToggle(_USE_SCREENSPACE_OUTLINE_WIDTH)] _UseScreenSpaceOutlineWidth("Width in Screen Space", Float) = 0
		[HideInInspector] _OutlineColor("Outline Color", Color) = (1,1,0,1)
		[HideInInspector][MaterialToggle(_OUTLINE_FILL_INSIDE)]_Fill("Fill", Float) = 0
		[HideInInspector] _OutlineReferenceTexWidth("Reference Texture Width", Int) = 1024
		[HideInInspector] _ThresholdEnd("Outline Threshold", Range(0,1)) = 0.25
		[HideInInspector] _OutlineSmoothness("Outline Smoothness", Range(0,1)) = 1.0
		[HideInInspector][MaterialToggle(_USE8NEIGHBOURHOOD_ON)] _Use8Neighbourhood("Sample 8 Neighbours", Float) = 1
		[HideInInspector] _OutlineOpaqueAlpha("Opaque Alpha", Range(0,1)) = 1.0
		[HideInInspector] _OutlineMipLevel("Outline Mip Level", Range(0,3)) = 0
	}

	SubShader
	{
		Tags { "Queue"="Transparent" "RenderType"="Sprite" "AlphaDepth"="False" "CanUseSpriteAtlas"="True" "IgnoreProjector"="True" }
		LOD 100

		Stencil {
			Ref[_StencilRef]
			Comp[_StencilComp]
			Pass Keep
		}

		Pass
		{
			Name "Normal"

			Blend [_SrcBlend] [_DstBlend]
			Lighting Off
			ZWrite [_ZWrite]
			ZTest LEqual
			Cull [_Cull]
			Lighting Off

			CGPROGRAM
				#pragma shader_feature _ _ALPHABLEND_ON _ALPHAPREMULTIPLY_ON _ALPHAPREMULTIPLY_VERTEX_ONLY _ADDITIVEBLEND _ADDITIVEBLEND_SOFT _MULTIPLYBLEND _MULTIPLYBLEND_X2
				#pragma shader_feature _ALPHA_CLIP
				#pragma shader_feature _TEXTURE_BLEND
				#pragma shader_feature _COLOR_ADJUST
				#pragma shader_feature _FOG
				#pragma shader_feature _TINT_BLACK_ON

				#pragma fragmentoption ARB_precision_hint_fastest
				#pragma multi_compile_fog
				#pragma multi_compile _ PIXELSNAP_ON

				#pragma vertex vert
				#pragma fragment frag

				#include "Assets/Spine/Runtime/spine-unity/Shaders/Sprite/CGIncludes/ShaderShared.cginc"
				#include "Assets/Spine/Runtime/spine-unity/Shaders/CGIncludes/Spine-Skeleton-Tint-Common.cginc"

				uniform float _HatchStrength;
				uniform float4 _HatchColor;
				uniform float _HatchScale;
				uniform float _HatchWidth;
				uniform float _HatchAngleDeg;
				uniform float _HatchOpacity;
				uniform float2 _HatchScrollVelocity;
				uniform float _HatchUseWorldSpace;

				struct VertexInput
				{
					float4 vertex : POSITION;
					float4 texcoord : TEXCOORD0;
					fixed4 color : COLOR;
				#if defined(_TINT_BLACK_ON)
					float2 tintBlackRG : TEXCOORD1;
					float2 tintBlackB : TEXCOORD2;
				#endif
					UNITY_VERTEX_INPUT_INSTANCE_ID
				};

				struct VertexOutput
				{
					float4 pos : SV_POSITION;
					float2 texcoord : TEXCOORD0;
					fixed4 color : COLOR;
					float2 objectPos : TEXCOORD1;
					float3 worldPos : TEXCOORD3;
				#if defined(_FOG)
					UNITY_FOG_COORDS(1)
				#endif // _FOG

				#if defined(_TINT_BLACK_ON)
					float3 darkColor : TEXCOORD2;
				#endif

					UNITY_VERTEX_OUTPUT_STEREO
				};

				VertexOutput vert(VertexInput input)
				{
					VertexOutput output;

					UNITY_SETUP_INSTANCE_ID(input);
					UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(output);

					output.pos = calculateLocalPos(input.vertex);
					output.texcoord = calculateTextureCoord(input.texcoord);
					output.color = calculateVertexColor(input.color);
					output.objectPos = input.vertex.xy;
					output.worldPos = calculateWorldPos(input.vertex).xyz;
				#if defined(_TINT_BLACK_ON)
					output.darkColor = GammaToTargetSpace(half3(input.tintBlackRG.r, input.tintBlackRG.g, input.tintBlackB.r))
						+ (_Black.rgb * input.color.a);
				#endif

				#if defined(_FOG)
					UNITY_TRANSFER_FOG(output,output.pos);
				#endif // _FOG

					return output;
				}

				fixed4 frag(VertexOutput input) : SV_Target
				{
					fixed4 texureColor = calculateTexturePixel(input.texcoord.xy);
					RETURN_UNLIT_IF_ADDITIVE_SLOT_TINT(texureColor, input.color, input.darkColor, _Color.a, _Black.a) // shall be called before ALPHA_CLIP
					ALPHA_CLIP(texureColor, input.color)

				#if defined(_TINT_BLACK_ON)
					texureColor = fragTintedColor(texureColor, input.darkColor, input.color, _Color.a, _Black.a);
				#endif

					fixed4 pixel = calculatePixel(texureColor, input.color);

					// World-space hatch overlay with fwidth-based anti-aliasing
					float angleRad = _HatchAngleDeg * 0.01745329252;
					float2 dir = float2(cos(angleRad), sin(angleRad));
					float2 p = lerp(input.objectPos, input.worldPos.xy, _HatchUseWorldSpace);
					p += _HatchScrollVelocity * _Time.y;
					float v = dot(p, dir) * _HatchScale;
					float stripe = abs(frac(v) - 0.5);
					
					// fwidth-based anti-aliasing to prevent shimmering
					float halfWidth = _HatchWidth * 0.5;
					float fw = fwidth(stripe) * 0.5;
					float hatchLine = 1.0 - smoothstep(halfWidth - fw, halfWidth + fw, stripe);
					
					float hatchAlpha = _HatchStrength * _HatchOpacity * hatchLine * texureColor.a * input.color.a;
					pixel.rgb = lerp(pixel.rgb, _HatchColor.rgb, hatchAlpha);

					COLORISE(pixel)
					APPLY_FOG(pixel, input)

					return pixel;
				}
			ENDCG
		}
		Pass
		{
			Name "ShadowCaster"
			Tags { "LightMode"="ShadowCaster" }
			Offset 1, 1

			Fog { Mode Off }
			ZWrite On
			ZTest LEqual
			Cull Off
			Lighting Off

			CGPROGRAM
				#pragma fragmentoption ARB_precision_hint_fastest
				#pragma multi_compile_shadowcaster
				#pragma multi_compile _ PIXELSNAP_ON

				#pragma vertex vert
				#pragma fragment frag

				#include "Assets/Spine/Runtime/spine-unity/Shaders/Sprite/CGIncludes/SpriteShadows.cginc"
			ENDCG
		}
	}

	CustomEditor "SpineSpriteShaderGUI"
}