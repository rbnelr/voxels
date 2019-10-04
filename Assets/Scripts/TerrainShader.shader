Shader "Custom/TerrainShader"
{
	Properties
	{
		_Color("Color", Color) = (1,1,1,1)
		_Glossiness("Smoothness", Range(0,1)) = 0.5
		_Metallic("Metallic", Range(0,1)) = 0.0

		_Antistretch    ("_Antistretch", Float) = 0.3
		_AtlasUVBias	("_AtlasUVBias", Float) = 0.0001
	}
		SubShader
	{
		Tags { "RenderType" = "Opaque" }
		LOD 200

		CGPROGRAM
		// Physically based Standard lighting model, and enable shadows on all light types
		#pragma surface surf Standard fullforwardshadows vertex:vert

		// Use shader model 3.0 target, to get nicer looking lighting
		#pragma target 3.0

		sampler2D	_Atlas;
		float4		_AtlasUVRects[32]; // float4(size.xy, óffset.xy)
		float2		_MaterialScales[32];
		float4		_MaterialTints[32];

		float		_Antistretch;
		//float		_AtlasUVBias;

		float _Glossiness;
		float _Metallic;
		fixed4 _Color;

		// Add instancing support for this shader. You need to check 'Enable Instancing' on materials that use the shader.
		// See https://docs.unity3d.com/Manual/GPUInstancing.html for more information about instancing.
		// #pragma instancing_options assumeuniformscaling
		UNITY_INSTANCING_BUFFER_START(Props)
		// put more per-instance properties here
		UNITY_INSTANCING_BUFFER_END(Props)

		struct Input {
			float3 worldPos;
			float3 worldNormal;

			float material;
		};

		void vert (inout appdata_full v, out Input o) {
			UNITY_INITIALIZE_OUTPUT(Input, o);
			o.material = v.texcoord.x;
		}

		float2 uv_in_atlas (float2 uv, float4 atlas_rect, float2 scale) {

			float2 size_in_atlas = atlas_rect.xy;
			float2 offs_in_atlas = atlas_rect.zw;

			uv /= scale;
			uv = uv - floor(uv); // texture wrapping manually since we are atlasing

			// --- Add uv bias to prevent visible seams, this could probalby be avoided by adding repeating the pixels around the packed textures in the atlas, but currently the black borders the atlas packer puts in the atlas are visible
			// Seams still appearing, not sure why they happen
			// -> Caused by bilinear filtering not being compatible with atlassing
			// can be fixed -> https://www.gamedev.net/forums/topic/602143-texture-atlas-seam-issue/ not worth to try to do in unity for now
			uv *= size_in_atlas;
			uv += offs_in_atlas;

			return uv;
		}
	
		// Get Material albedo color by sampling the 2d texture with 3d positions by projecting the texture from the x y and z axis and blending those based on the surface normal in a way that tried to prevent stretching
		float4 sampleMaterial (Input IN, sampler2D atlas, float4 atlas_rect, float2 scale, float4 tint) {
			float3 antistretchCoeff = saturate(normalize(abs(IN.worldNormal) * (1 + _Antistretch) - _Antistretch));
			antistretchCoeff = normalize(antistretchCoeff);

			float4 texX = tex2D(atlas, uv_in_atlas(IN.worldPos.zy, atlas_rect, scale)) * antistretchCoeff.x;
			float4 texY = tex2D(atlas, uv_in_atlas(IN.worldPos.xz, atlas_rect, scale)) * antistretchCoeff.y;
			float4 texZ = tex2D(atlas, uv_in_atlas(IN.worldPos.xy, atlas_rect, scale)) * antistretchCoeff.z;
			
			return (texX + texY + texZ) * tint;
		}

		void surf(Input IN, inout SurfaceOutputStandard o) {
			o.Metallic = _Metallic;
			o.Smoothness = _Glossiness;

			int mat_id = int(round(IN.material));

			float4 mat = sampleMaterial(IN, _Atlas, _AtlasUVRects[mat_id], _MaterialScales[mat_id], _MaterialTints[mat_id]);

			//float blend = abs(IN.worldNormal.y) * IN.worldNormal.y * 1.3f - 0.3f;

			//blend = smoothstep(0,1, blend);
			//blend = smoothstep(0,1, blend);
			//blend = smoothstep(0,1, blend);
			//blend = smoothstep(0,1, blend);

			//float4 mat = lerp(mat0, mat1, saturate(blend));

			fixed4 c = mat * _Color;
			o.Albedo = c.rgb;
			o.Alpha = c.a;
		}
		ENDCG
	}
		FallBack "Diffuse"
}
