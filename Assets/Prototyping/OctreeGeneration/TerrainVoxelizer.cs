using Unity.Jobs;
using Unity.Mathematics;
using static Unity.Mathematics.math;
using Unity.Collections;
using UnityEngine;
using System.Collections.Generic;

namespace OctreeGeneration {
	public struct Voxel {
		public float density;
		
		public static Voxel Lerp (Voxel a, Voxel b, float t) {
			a.density	= a.density + (b.density	- a.density	) * t;
			return a;
		}

		public override string ToString () {
			return density.ToString();
		}
	}
	
	public struct TerrainGenerator {
		
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
			};
		}
	}

	public struct TerrainVoxelizeJob : IJobParallelFor {
		[ReadOnly] public float3 ChunkPos;
		[ReadOnly] public float ChunkSize;
		[ReadOnly] public int ChunkVoxels;
		[ReadOnly] public TerrainGenerator Gen;
		
		[WriteOnly] public NativeArray<Voxel> Voxels;

		public void Execute (int i) {
			int ArraySize = ChunkVoxels + 1;

			int voxelIndex = i;
			int3 voxelCoord;
			voxelCoord.x = i % ArraySize;
			i /= ArraySize;
			voxelCoord.y = i % ArraySize;
			i /= ArraySize;
			voxelCoord.z = i;

			float3 pos_world = (float3)voxelCoord;
			pos_world *= ChunkSize / ChunkVoxels;
			pos_world += ChunkPos - ChunkSize * 0.5f;
						
			Voxels[voxelIndex] = Gen.Generate(pos_world);
		}
	}
	
	public class TerrainVoxelizer {
		Dictionary<TerrainChunk, JobHandle> jobs = new Dictionary<TerrainChunk, JobHandle>();


		
		//public void StartJob (TerrainChunk chunk, int ChunkVoxels, TerrainGenerator gen) {
		//	int ArraySize = ChunkVoxels + 1;
		//	int voxelsLength = ArraySize * ArraySize * ArraySize;
		//
		//	chunk.voxels?.Dispose();
		//	chunk.voxels = new NativeArray<Voxel>(voxelsLength, Allocator.Persistent);
		//
		//	var job = new TerrainVoxelizeJob {
		//		ChunkPos = chunk.pos,
		//		ChunkSize = chunk.size,
		//		ChunkVoxels = ChunkVoxels,
		//		Gen = gen,
		//		Voxels = chunk.voxels.Value
		//	};
		//	jobHandle = job.Schedule(voxelsLength, ChunkVoxels);
		//}
		//public void Update (TerrainChunk chunk) {
		//	if (jobHandle != null && jobHandle.Value.IsCompleted) {
		//		jobHandle.Value.Complete();
		//		jobHandle = null;
		//
		//		chunk.needsRemesh = true;
		//	
		//		if (chunk.parent != null)
		//			chunk.parent.needsRemesh = true;
		//	}
		//}
		//
		//public void Dispose () {
		//	if (jobHandle != null) {
		//		Debug.Log("TerrainVoxelizer.Dispose(): Job still running, need to wait for it to complete inorder to Dispose of the native arrays");
		//		jobHandle.Value.Complete();
		//		jobHandle = null;
		//	}
		//}
	}
}
