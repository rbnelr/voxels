using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using CoherentNoise;
using CoherentNoise.Generation;
using CoherentNoise.Generation.Fractal;
using CoherentNoise.Generation.Voronoi;

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

		public Voxel Generate (Vector3 pos) {
			var noise2d = new CoherentNoise.Generator[] {
				(new CoherentNoise.Generation.ValueNoise2D(   0) + new CoherentNoise.Generation.GradientNoise2D(   0)) / 2,
				(new CoherentNoise.Generation.ValueNoise2D(1000) + new CoherentNoise.Generation.GradientNoise2D(1000)) / 2,
				(new CoherentNoise.Generation.ValueNoise2D(2000) + new CoherentNoise.Generation.GradientNoise2D(2000)) / 2,
				(new CoherentNoise.Generation.ValueNoise2D(3000) + new CoherentNoise.Generation.GradientNoise2D(3000)) / 2,
			};
			var noise = new CoherentNoise.Generator[] {
				(new CoherentNoise.Generation.ValueNoise(3000) + new CoherentNoise.Generation.GradientNoise(3000)) / 2,
				(new CoherentNoise.Generation.ValueNoise(4000) + new CoherentNoise.Generation.GradientNoise(4000)) / 2,
				(new CoherentNoise.Generation.ValueNoise(5000) + new CoherentNoise.Generation.GradientNoise(5000)) / 2,
				(new CoherentNoise.Generation.ValueNoise(6000) + new CoherentNoise.Generation.GradientNoise(6000)) / 2,
			};
			
			//return new Voxel {
			//	density = (new Vector3(32,32,32) - pos).magnitude - 2, 
			//	color = Color.white
			//};

			//return new Voxel {
			//	density = Vector3.Dot(new Vector3(1,2,3).normalized, pos) - noise[0].GetValue(pos / 10) * 30 + 30, 
			//	color = Color.white
			//};

			var pos2d = new Vector2(pos.x, pos.z);

			float terrain_height = 0;
			float cave_isolevel = 0;
		
			terrain_height += noise2d[0].GetValue(pos2d / (80.0f / 1)) * (40.0f / 1);
			terrain_height += noise2d[1].GetValue(pos2d / (80.0f / 4)) * (20.0f / 3);
			terrain_height += noise2d[2].GetValue(pos2d / (80.0f / 16)) * (20.0f / 20);
		
			float cave = cave_isolevel;
			cave += noise[0].GetValue(pos / 120) * (1.0f / 1) + (1.0f / 5);
			cave += noise[1].GetValue(pos / 50) * (1.0f / 4);
			cave += noise[2].GetValue(pos / 10) * Mathf.Max(0.0f, MyMath.Map(noise[3].GetValue(pos / 70), -1,+1, -0.5f, 2.0f)) / 16;
		
			float stalactite_fac = Mathf.Max(0.0f, MyMath.Map(noise[3].GetValue(pos / 150), -1,+1, -1f, 1.2f));

			float stalactite = Mathf.Clamp01(MyMath.Map(noise2d[3].GetValue(pos2d / 5), -1,+1, -1,1));
			cave += stalactite * stalactite_fac * 0.5f;

			float terrain = terrain_height -pos.y;

			float density = -Mathf.Lerp(Mathf.Min(terrain, 0.0f), cave, Mathf.Clamp01(MyMath.Map(terrain, 0, +10)));
			
			return new Voxel {
				density = density, 
				color = coloring.Evaluate(density * 0.5f + 0.5f)
			};
		}
	}
}
