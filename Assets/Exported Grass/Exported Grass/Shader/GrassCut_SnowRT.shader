Shader "GrassCut/SnowRT" {
	Properties {
		_SnowColor ("Snow Color", Vector) = (0.95,0.97,1,1)
		_DiffuseStrength ("Diffuse Strength", Range(0, 2)) = 1
		_SpecularStrength ("Specular Strength", Range(0, 1)) = 0
		_CutEdge ("Cut Angle", Range(0.001, 0.45)) = 0.05
		_SnowHeight ("Snow Height", Float) = 0.5
		_DriftHeight ("Drift Height", Range(0, 1)) = 0.08
		_DriftScale ("Drift Scale", Range(0.1, 5)) = 0.8
		[Header(Snow Texture)] _SnowTex ("Albedo", 2D) = "white" {}
		_SnowTexScale ("Tiling", Float) = 3
		[Header(Drift)] _DriftNoiseTex ("Noise Texture", 2D) = "white" {}
		_DriftDepth ("Drift Depth", Range(0, 0.5)) = 0
		_DriftColor ("Drift Color", Vector) = (0.6,0.78,1,1)
		_DriftAO ("Drift Strength", Range(0, 1)) = 0.4
		_DriftColorThreshold ("Drift Color Start", Range(0, 1)) = 0
		_CutColor ("Cut Wall Color", Vector) = (0.5,0.7,1,1)
		_CutStrength ("Cut Wall Strength", Range(0, 1)) = 0.4
		_CutColorThreshold ("Cut Wall Color Start", Range(0, 1)) = 0
		[Header(Edges)] _EdgeAngle ("Edge Angle", Range(0.001, 0.5)) = 0.1
		_EdgeColor ("Edge Color", Vector) = (0.5,0.7,1,1)
		_EdgeStrength ("Edge Strength", Range(0, 1)) = 0.4
		_DrawerRt ("Drawer RT", 2D) = "black" {}
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
}