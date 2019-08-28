Shader "Custom/TerrainShader"
{
	Properties
	{
		_Color("Color", Color) = (1,1,1,1)
		_Glossiness("Smoothness", Range(0,1)) = 0.5
		_Metallic("Metallic", Range(0,1)) = 0.0

		_MainTex("Albedo (RGB)", 2D) = "white" {}
		_TextureScale("TextureScale", Float) = 1
		_TextureAspect("TextureAspect", Float) = 1
	}
		SubShader
	{
		Tags { "RenderType" = "Opaque" }
		LOD 200

		CGPROGRAM
// Upgrade NOTE: excluded shader from OpenGL ES 2.0 because it uses non-square matrices
#pragma exclude_renderers gles
		// Physically based Standard lighting model, and enable shadows on all light types
		#pragma surface surf Standard fullforwardshadows

		// Use shader model 3.0 target, to get nicer looking lighting
		#pragma target 3.0

		half _TextureScale;
		half _TextureAspect;
		sampler2D _MainTex;

		struct Input {
			float3 worldPos;
			float3 worldNormal;

			//float2 uv_MainTex;
		};

		half _Glossiness;
		half _Metallic;
		fixed4 _Color;

		// Add instancing support for this shader. You need to check 'Enable Instancing' on materials that use the shader.
		// See https://docs.unity3d.com/Manual/GPUInstancing.html for more information about instancing.
		// #pragma instancing_options assumeuniformscaling
		UNITY_INSTANCING_BUFFER_START(Props)
			// put more per-instance properties here
		UNITY_INSTANCING_BUFFER_END(Props)

		void surf(Input IN, inout SurfaceOutputStandard o)
		{
			o.Metallic = _Metallic;
			o.Smoothness = _Glossiness;

			float3 axisCoeff = saturate(normalize(abs(IN.worldNormal) * 1.3 - 0.3));

			float2 scale = _TextureScale * float2(1, _TextureAspect);

			float4 texX = tex2D(_MainTex, IN.worldPos.zy / scale) * axisCoeff.x;
			float4 texY = tex2D(_MainTex, IN.worldPos.xz / scale) * axisCoeff.y;
			float4 texZ = tex2D(_MainTex, IN.worldPos.xy / scale) * axisCoeff.z;
			float4 texCombined = texX + texY + texZ;

			fixed4 c = texCombined * _Color;
			o.Albedo = c.rgb;
			o.Alpha = c.a;
		}
		ENDCG
	}
		FallBack "Diffuse"
}
