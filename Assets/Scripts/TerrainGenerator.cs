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
		
		terrain_height += noise2d[0].GetValue(pos2d / (80 / 1)) * (20 / 1);
		terrain_height += noise2d[1].GetValue(pos2d / (80 / 4)) * (20 / 3);
		terrain_height += noise2d[2].GetValue(pos2d / (80 / 16)) * (20 / 20);
		
		float cave = cave_isolevel;
		cave += noise[0].GetValue(pos / (40 / 1)) * (20 / 1);
		cave += noise[1].GetValue(pos / (40 / 2)) * (20 / 2);
		cave += noise[2].GetValue(pos / (40 / 4)) * (20 / 4);
		
		float terrain = Mathf.Clamp(terrain_height -pos.y, -1, +1);

		return terrain;
		//return terrain - cave;
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
		};
	}
}
