using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class TerrainGenerator {
	
	CoherentNoise.Generator[] noise2d;
	CoherentNoise.Generator[] noise;
	
	public float clamp (float x, float a=0.0f, float b=1.0f) {
		return Mathf.Clamp(x, a, b);
	}
	public float lerp (float a, float b, float t) {
		return Mathf.Lerp(a, b, t);
	}
	public float map (float x, float in_a, float in_b) {
		return (x - in_a) / (in_b - in_a);
	}
	public float map (float x, float in_a, float in_b, float out_a, float out_b) {
		return ((x - in_a) / (in_b - in_a)) * (out_b - out_a) + out_a;
	}

	public float get_density (Vector3 pos) {
		var pos2d = new Vector2(pos.x, pos.z);

		float terrain_height = 0;
		float cave_isolevel = 0;
		
		terrain_height += noise2d[0].GetValue(pos2d / (80 / 1)) * (20 / 1);
		terrain_height += noise2d[1].GetValue(pos2d / (80 / 4)) * (20 / 3);
		terrain_height += noise2d[2].GetValue(pos2d / (80 / 16)) * (20 / 20);
		
		float cave = cave_isolevel;
		cave += noise[0].GetValue(pos / 120) * 20 + 5;
		cave += noise[1].GetValue(pos / 50) * 5;
		cave += noise[2].GetValue(pos / 10) * Mathf.Max(0.0f, map(noise[3].GetValue(pos / 70), -1,+1, -0.5f, 2.0f));
		
		float terrain = terrain_height -pos.y;

		float density = lerp(Mathf.Min(terrain, 0.0f), cave, clamp(map(terrain, 0, +10)));
		
		return density;
	}

	public TerrainGenerator () {
		noise2d = new CoherentNoise.Generator[] {
			(new CoherentNoise.Generation.ValueNoise2D(   0) + new CoherentNoise.Generation.GradientNoise2D(   0)) / 2,
			(new CoherentNoise.Generation.ValueNoise2D(1000) + new CoherentNoise.Generation.GradientNoise2D(1000)) / 2,
			(new CoherentNoise.Generation.ValueNoise2D(2000) + new CoherentNoise.Generation.GradientNoise2D(2000)) / 2,
		};
		noise = new CoherentNoise.Generator[] {
			(new CoherentNoise.Generation.ValueNoise(3000) + new CoherentNoise.Generation.GradientNoise(3000)) / 2,
			(new CoherentNoise.Generation.ValueNoise(4000) + new CoherentNoise.Generation.GradientNoise(4000)) / 2,
			(new CoherentNoise.Generation.ValueNoise(5000) + new CoherentNoise.Generation.GradientNoise(5000)) / 2,
			(new CoherentNoise.Generation.ValueNoise(6000) + new CoherentNoise.Generation.GradientNoise(6000)) / 2,
		};
	}
}
