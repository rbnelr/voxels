Shader "Custom/TerrainShader"
{
	Properties
	{
		_Color("Color", Color) = (1,1,1,1)
		_Glossiness("Smoothness", Range(0,1)) = 0.5
		_Metallic("Metallic", Range(0,1)) = 0.0

		_Mat0TexA		("_Mat0TexA", 2D) = "white" {}
		_Mat0TexScale	("_Mat0TexScale", Float) = 1
		_Mat0TexAspect	("_Mat0TexAspect", Float) = 1
		_Mat0Color		("_Mat0Color", Color) = (1,1,1,1)

		_Mat1TexA		("_Mat1TexA", 2D) = "white" {}
		_Mat1TexScale	("_Mat1TexScale", Float) = 1
		_Mat1TexAspect	("_Mat1TexAspect", Float) = 1
		_Mat1Color		("_Mat1Color", Color) = (1,1,1,1)
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

		sampler2D	_Mat0TexA;
		half		_Mat0TexScale;
		half		_Mat0TexAspect;
		half4		_Mat0Color;

		sampler2D	_Mat1TexA;
		half		_Mat1TexScale;
		half		_Mat1TexAspect;
		half4		_Mat1Color;

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

		float4 sampleMaterial (Input IN, float3 blendCoeff, sampler2D texA, half scale, half aspect, half4 col) {
			float2 scale2d = scale * float2(1, aspect);

			float4 texX = tex2D(texA, IN.worldPos.zy / scale2d) * blendCoeff.x;
			float4 texY = tex2D(texA, IN.worldPos.xz / scale2d) * blendCoeff.y;
			float4 texZ = tex2D(texA, IN.worldPos.xy / scale2d) * blendCoeff.z;

			return (texX + texY + texZ) * col;
		}

		void surf(Input IN, inout SurfaceOutputStandard o) {
			o.Metallic = _Metallic;
			o.Smoothness = _Glossiness;

			float3 blendCoeff = saturate(normalize(abs(IN.worldNormal) * 1.3 - 0.3));

			float4 mat0 = sampleMaterial(IN, blendCoeff, _Mat0TexA, _Mat0TexScale, _Mat0TexAspect, _Mat0Color);
			float4 mat1 = sampleMaterial(IN, blendCoeff, _Mat1TexA, _Mat1TexScale, _Mat1TexAspect, _Mat1Color);

			float blend = IN.worldNormal.y * IN.worldNormal.y * 1.3f - 0.3f;

			blend = smoothstep(0,1, blend);
			blend = smoothstep(0,1, blend);
			blend = smoothstep(0,1, blend);
			blend = smoothstep(0,1, blend);

			float4 mat = lerp(mat0, mat1, saturate(blend));

			fixed4 c = mat * _Color;
			o.Albedo = c.rgb;
			o.Alpha = c.a;
		}
		ENDCG
	}
		FallBack "Diffuse"
}
