Shader "Custom/Grass" {
	Properties {
		_TintColor1 ("Tint Color 1", Vector) = (1,1,1,1)
		_TintColor2 ("Tint Color 2", Vector) = (1,1,1,1)
		_TintColor3 ("Tint Color 3", Vector) = (1,1,1,1)
		_ColorHeight1 ("Color Height1", Range(0, 1)) = 0.3
		_ColorHeight2 ("Color Height2", Range(0, 1)) = 0.3
		_CutColor ("Cut Color", Vector) = (1,1,1,1)
		[Space(10)] _CollisionBendMultiplier ("Collision Bend Multiplier", Range(0, 5)) = 2
		_BendHeightReduction ("Bend Height Reduction", Range(0, 1)) = 0.7
		_WindMultiplier ("Wind Multiplier (instanced)", Float) = 1
		_CollisionBending ("Collision Bending (instanced)", Vector) = (0,1,0,0)
		_UseDynamicShadowData ("Use Dynamic Shadow Data", Float) = 1
		[HideInInspector] _Seed ("Seed (instanced)", Float) = 0
		[HideInInspector] _AdditionalInstanceColors ("Instance Color2 (instanced)", Vector) = (1,1,1,1)
	}
	//DummyShaderTextExporter
	SubShader{
		Tags { "RenderType" = "Opaque" }
		LOD 200

		Pass
		{
			HLSLPROGRAM
			#pragma vertex vert
			#pragma fragment frag

			float4x4 unity_ObjectToWorld;
			float4x4 unity_MatrixVP;

			struct Vertex_Stage_Input
			{
				float4 pos : POSITION;
			};

			struct Vertex_Stage_Output
			{
				float4 pos : SV_POSITION;
			};

			Vertex_Stage_Output vert(Vertex_Stage_Input input)
			{
				Vertex_Stage_Output output;
				output.pos = mul(unity_MatrixVP, mul(unity_ObjectToWorld, input.pos));
				return output;
			}

			float4 frag(Vertex_Stage_Output input) : SV_TARGET
			{
				return float4(1.0, 1.0, 1.0, 1.0); // RGBA
			}

			ENDHLSL
		}
	}
	Fallback "Diffuse"
}