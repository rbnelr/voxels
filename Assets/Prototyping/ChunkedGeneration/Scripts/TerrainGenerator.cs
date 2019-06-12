using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class TerrainGenerator {
	
	CoherentNoise.Generator[] noise2d;
	CoherentNoise.Generator[] noise;
	
	public float get_density (Vector3 pos) {
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

		float stalactite = stalactite_test(pos);
		cave += stalactite * stalactite_fac * 0.5f;

		float terrain = terrain_height -pos.y;

		float density = Mathf.Lerp(Mathf.Min(terrain, 0.0f), cave, Mathf.Clamp01(MyMath.Map(terrain, 0, +10)));
		
		return -density;
	}

	public float stalactite_test (Vector3 pos) {
		var pos2d = new Vector2(pos.x, pos.z);
		
		float spike = Mathf.Clamp01(MyMath.Map(noise2d[3].GetValue(pos2d / 5), -1,+1, -1,1));
		return spike;
	}

	public TerrainGenerator () {
		noise2d = new CoherentNoise.Generator[] {
			(new CoherentNoise.Generation.ValueNoise2D(   0) + new CoherentNoise.Generation.GradientNoise2D(   0)) / 2,
			(new CoherentNoise.Generation.ValueNoise2D(1000) + new CoherentNoise.Generation.GradientNoise2D(1000)) / 2,
			(new CoherentNoise.Generation.ValueNoise2D(2000) + new CoherentNoise.Generation.GradientNoise2D(2000)) / 2,
			(new CoherentNoise.Generation.ValueNoise2D(3000) + new CoherentNoise.Generation.GradientNoise2D(3000)) / 2,
		};
		noise = new CoherentNoise.Generator[] {
			(new CoherentNoise.Generation.ValueNoise(3000) + new CoherentNoise.Generation.GradientNoise(3000)) / 2,
			(new CoherentNoise.Generation.ValueNoise(4000) + new CoherentNoise.Generation.GradientNoise(4000)) / 2,
			(new CoherentNoise.Generation.ValueNoise(5000) + new CoherentNoise.Generation.GradientNoise(5000)) / 2,
			(new CoherentNoise.Generation.ValueNoise(6000) + new CoherentNoise.Generation.GradientNoise(6000)) / 2,
		};
	}
}
