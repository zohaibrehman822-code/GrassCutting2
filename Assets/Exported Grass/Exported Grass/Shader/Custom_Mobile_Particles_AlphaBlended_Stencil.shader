Shader "Custom/Mobile/Particles/AlphaBlended_Stencil" {
	Properties {
		_MainTex ("Particle Texture", 2D) = "white" {}
		_Color ("Tint", Vector) = (1,1,1,1)
		_Stencil ("Stencil Ref", Float) = 0
		[Enum(Disabled,0, Never,1, Less,2, Equal,3, LessEqual,4, Greater,5, NotEqual,6, GreaterEqual,7, Always,8)] _StencilComp ("Stencil Comparison", Float) = 8
		[Enum(Keep,0, Zero,1, Replace,2, IncrSat,3, DecrSat,4, Invert,5, IncrWrap,6, DecrWrap,7)] _StencilOp ("Stencil Pass Op", Float) = 0
		_StencilReadMask ("Stencil Read Mask", Float) = 255
		_StencilWriteMask ("Stencil Write Mask", Float) = 255
		[Enum(0,0, R,1, G,2, B,4, A,8, RGB,7, RGBA,15)] _ColorMask ("Color Mask", Float) = 15
	}
	//DummyShaderTextExporter
	SubShader{
		Tags { "RenderType"="Opaque" }
		LOD 200

		Pass
		{
			HLSLPROGRAM
			#pragma vertex vert
			#pragma fragment frag

			float4x4 unity_ObjectToWorld;
			float4x4 unity_MatrixVP;
			float4 _MainTex_ST;

			struct Vertex_Stage_Input
			{
				float4 pos : POSITION;
				float2 uv : TEXCOORD0;
			};

			struct Vertex_Stage_Output
			{
				float2 uv : TEXCOORD0;
				float4 pos : SV_POSITION;
			};

			Vertex_Stage_Output vert(Vertex_Stage_Input input)
			{
				Vertex_Stage_Output output;
				output.uv = (input.uv.xy * _MainTex_ST.xy) + _MainTex_ST.zw;
				output.pos = mul(unity_MatrixVP, mul(unity_ObjectToWorld, input.pos));
				return output;
			}

			Texture2D<float4> _MainTex;
			SamplerState sampler_MainTex;
			float4 _Color;

			struct Fragment_Stage_Input
			{
				float2 uv : TEXCOORD0;
			};

			float4 frag(Fragment_Stage_Input input) : SV_TARGET
			{
				return _MainTex.Sample(sampler_MainTex, input.uv.xy) * _Color;
			}

			ENDHLSL
		}
	}
	Fallback "Mobile/Particles/Alpha Blended"
}