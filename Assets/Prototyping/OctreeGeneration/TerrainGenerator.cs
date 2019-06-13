using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using Unity.Mathematics;
using static Unity.Mathematics.math;

namespace OctreeGeneration {

	public struct Voxel {
		public float density;
		public Color color;
		
		public static Voxel Lerp (Voxel a, Voxel b, float t) {
			a.density	= a.density + (b.density	- a.density	) * t;
			a.color		= a.color	+ (b.color		- a.color	) * t;
			return a;
		}
	}
	
	public class TerrainGenerator : MonoBehaviour {
		
		public Gradient coloring;
		
		public bool hasChanged = false;
		void LateUpdate () {
			hasChanged = false;
		}

		void OnEnable () {
			UpdateNoise();
		}

		public void UpdateNoise () {
			hasChanged = true;
		}
		
		public static float clamp (float x, float a=0.0f, float b=1.0f) {
			return Mathf.Clamp(x, a, b);
		}
		public static float lerp (float a, float b, float t) {
			return Mathf.Lerp(a, b, t);
		}
		public static float map (float x, float in_a, float in_b) {
			return (x - in_a) / (in_b - in_a);
		}
		public static float map (float x, float in_a, float in_b, float out_a, float out_b) {
			return ((x - in_a) / (in_b - in_a)) * (out_b - out_a) + out_a;
		}
		
		float fractal (float pos, int octaves, float freq, bool dampen=true) {
			float dampened = dampen ? freq : 1f;
			float total = noise.snoise(float2(pos, 999) / freq);
			float amplitude = 1.0f;
			float range = 1.0f;
			for (int i=1; i<octaves; ++i) {
				freq /= 2;
				amplitude /= 2;
				range += amplitude;
				total += noise.snoise(float2(pos, 999) / freq) * amplitude;
			}
			return total / range * dampened;
		}
		float fractal (float2 pos, int octaves, float freq, bool dampen=true) {
			float dampened = dampen ? freq : 1f;
			float total = noise.snoise(pos / freq);
			float amplitude = 1.0f;
			float range = 1.0f;
			for (int i=1; i<octaves; ++i) {
				freq /= 2;
				amplitude /= 2;
				range += amplitude;
				total += noise.snoise(pos / freq) * amplitude;
			}
			return total / range * dampened;
		}
		float fractal (float3 pos, int octaves, float freq, bool dampen=true) {
			float dampened = dampen ? freq : 1f;
			float total = noise.snoise(pos / freq);
			float amplitude = 1.0f;
			float range = 1.0f;
			for (int i=1; i<octaves; ++i) {
				freq /= 2;
				amplitude /= 2;
				range += amplitude;
				total += noise.snoise(pos / freq) * amplitude;
			}
			return total / range * dampened;
		}

		float smooth_func (float x, float n) {
			if (x > n/4f)
				return 0f;
			float tmp = (x/n - 1f/4f);
			return n * tmp * tmp;
		}
		// union of two scalar fields (normally just max(a,b)), smoothed with paramter n
		// see http://citeseerx.ist.psu.edu/viewdoc/download?doi=10.1.1.95.8898&rep=rep1&type=pdf
		float smooth_union (float a, float b, float n) {
			return max(a, b) + smooth_func(abs(a - b), n);
		}

		// units height from surface
		public float Surface (float3 pos) {
			float2 pos2d = pos.xz;

			float height = fractal(pos2d + 3000, 6, 4000) * 0.1f;
			//float height = fractal(pos2d + 3000, 4, 4000) * 0.1f;

			return pos.y - height;
		}
		// TODO: change to be a "multiplier" for cave intensity
		public float Abyss (float3 pos) {
			float2 pos2d = pos.xz;

			float radius = 555;
			
			float strength = fractal(pos.y + 999, 2, radius * 6, false);

			pos2d.x += fractal(pos.y + 10000, 3, radius * 2, false) * radius * (0.6f + strength * 0.5f);
			pos2d.y += fractal(pos.y - 10000, 3, radius * 2, false) * radius * (0.6f + strength * 0.5f);

			radius *= 1f + fractal(pos.y + 20000, 3, radius * 4, false) * 0.7f;

			return (radius - length(pos2d)) / radius;
		}
		public float Cave (float3 pos) {
			return fractal(pos, 3, 400, false);
		}
		public Voxel Generate (float3 pos) {
			var surf = Surface(pos);
			
			var abyss = Abyss(pos);
			var cave = Cave(pos);

			cave = cave - 1f + abyss * 2.2f;

			var val = smooth_union(surf, cave, 2000f);

			//val += fractal(pos + 400, 5, 220) * 0.15f;
			
			return new Voxel {
				density = val, 
				color = Color.white
			};
			////return new Voxel {
			////	density = Vector3.Dot(new Vector3(1,2,3).normalized, pos) - noise[0].GetValue(pos / 10) * 30 + 30, 
			////	color = Color.white
			////};
			//
			//var pos2d = new Vector2(pos.x, pos.z);
			//
			//float terrain_height = 0;
			//float cave_isolevel = 0;
			//
			//terrain_height += noise2d[0].GetValue(pos2d / (80.0f / 1)) * (40.0f / 1);
			//terrain_height += noise2d[1].GetValue(pos2d / (80.0f / 4)) * (20.0f / 3);
			//terrain_height += noise2d[2].GetValue(pos2d / (80.0f / 16)) * (20.0f / 20);
			//
			//float cave = cave_isolevel;
			//cave += noise[0].GetValue(pos / 120) * (1.0f / 1) + (1.0f / 5);
			//cave += noise[1].GetValue(pos / 50) * (1.0f / 4);
			//cave += noise[2].GetValue(pos / 10) * Mathf.Max(0.0f, MyMath.Map(noise[3].GetValue(pos / 70), -1,+1, -0.5f, 2.0f)) / 16;
			//
			//float stalactite_fac = Mathf.Max(0.0f, MyMath.Map(noise[3].GetValue(pos / 150), -1,+1, -1f, 1.2f));
			//
			//float stalactite = Mathf.Clamp01(MyMath.Map(noise2d[3].GetValue(pos2d / 5), -1,+1, -1,1));
			//cave += stalactite * stalactite_fac * 0.5f;
			//
			//float terrain = terrain_height -pos.y;
			//
			//float density = -Mathf.Lerp(Mathf.Min(terrain, 0.0f), cave, Mathf.Clamp01(MyMath.Map(terrain, 0, +10)));
			//
			//return new Voxel {
			//	density = density, 
			//	color = coloring.Evaluate(density * 0.5f + 0.5f)
			//};
		}
	}
}
