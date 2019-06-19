using Unity.Jobs;
using Unity.Mathematics;
using static Unity.Mathematics.math;
using Unity.Collections;
using UnityEngine;
using System.Collections.Generic;
using Unity.Burst;
using UnityEngine.Profiling;
using System.Collections.ObjectModel;

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
	
	public class Voxels {
		public readonly OctreeCoord	coord;
		public NativeArray<Voxel>	native;

		public Voxels (OctreeCoord c) {
			coord = c;
		}

		public static int _3dToFlatIndex (int3 pos, int ChunkVoxels) {
			int ArraySize = ChunkVoxels + 1;
			
			return pos.z * ArraySize * ArraySize + pos.y * ArraySize + pos.x;
		}
		public static int3 flatTo3dIndex (int index, int ChunkVoxels) {
			int ArraySize = ChunkVoxels + 1;
			
			int3 voxelCoord;
			voxelCoord.x = index % ArraySize;
			index /= ArraySize;
			voxelCoord.y = index % ArraySize;
			index /= ArraySize;
			voxelCoord.z = index;

			return voxelCoord;
		}
	}

	class VoxelCache : KeyedCollection<OctreeCoord, Voxels> {
		
		protected override OctreeCoord GetKeyForItem (Voxels item) {
			return item.coord;
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
			//return new Voxel {
			//	density = dot(pos, normalize(float3(1,2,3))),
			//};

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
	
	public class TerrainVoxelizer : MonoBehaviour {
		
		[BurstCompile]
		struct Job : IJobParallelFor {
			[ReadOnly] public float3 ChunkPos;
			[ReadOnly] public float ChunkSize;
			[ReadOnly] public int ChunkVoxels;
			[ReadOnly] public TerrainGenerator Gen;
		
			[WriteOnly] public NativeArray<Voxel> voxels;

			public void Execute (int i) {
				int voxelIndex = i;
				int3 voxelCoord = Voxels.flatTo3dIndex(i, ChunkVoxels);

				float3 pos_world = (float3)voxelCoord;
				pos_world *= ChunkSize / ChunkVoxels;
				pos_world += ChunkPos - ChunkSize * 0.5f;
						
				voxels[voxelIndex] = Gen.Generate(pos_world);
			}
		}

		class RunningJob {
			public Voxels voxels;
			public JobHandle JobHandle;
		}
		
		TerrainOctree octree;
		TerrainMesher mesher;

		VoxelCache cache = new VoxelCache();

		Dictionary<OctreeCoord, RunningJob> runningJobs = new Dictionary<OctreeCoord, RunningJob>();
		
		public TerrainGenerator terrainGenerator;
		public int MaxJobs = 3;
		public int CacheSize = 512;
		
		void Awake () {
			octree = GetComponent<TerrainOctree>();
			mesher = GetComponent<TerrainMesher>();
		}

		public Voxels GetCachedVoxels (OctreeCoord coord) {
			if (!cache.Contains(coord))
				return null;
			return cache[coord];
		}
		
		void Update () {
			if (octree.root == null)
				return;

			Profiler.BeginSample("StartJob loop");
			for (int i=0; i<octree.SortedTerrainChunks.Count; ++i) {
				var chunk = octree.SortedTerrainChunks[i];
				if (!cache.Contains(chunk.coord) && !runningJobs.ContainsKey(chunk.coord) && runningJobs.Count < MaxJobs) {
					StartJob(chunk, octree.ChunkVoxels, terrainGenerator);
				}
			}
			Profiler.EndSample();

			Profiler.BeginSample("Finish Jobs");
			var toRemove = new List<OctreeCoord>();
			
			foreach (var job in runningJobs) {
				if (job.Value.JobHandle.IsCompleted) {
					job.Value.JobHandle.Complete();

					while (cache.Count > 0 && cache.Count > (CacheSize -1)) { // uncache
						cache.RemoveAt(0);
					}

					cache.Add(job.Value.voxels);
					
					toRemove.Add(job.Key);
				}
			}
			
			foreach (var j in toRemove) {
				runningJobs.Remove(j);
			}
			Profiler.EndSample();
		}

		void OnDestroy () {
			Profiler.BeginSample("OnDestroy");
			mesher.Dispose(); // stop running mesher jobs first, then dispose the voxels they might be using

			foreach (var voxels in cache) {
				voxels.native.Dispose();
			}

			foreach (var job in runningJobs) {
				job.Value.JobHandle.Complete(); // block main thread
				job.Value.voxels.native.Dispose();
			}
			Profiler.EndSample();
		}

		void StartJob (TerrainNode chunk, int ChunkVoxels, TerrainGenerator gen) {
			Profiler.BeginSample("StartJob");

			int ArraySize = ChunkVoxels + 1;
			int voxelsLength = ArraySize * ArraySize * ArraySize;
			
			Profiler.BeginSample("new NativeArray");
			var runningJob = new RunningJob();
			runningJob.voxels = new Voxels(chunk.coord) { native = new NativeArray<Voxel>(voxelsLength, Allocator.Persistent) };
			Profiler.EndSample();
			
			Profiler.BeginSample("new Job");
			var job = new Job {
				ChunkPos = chunk.TerrainChunk.pos,
				ChunkSize = chunk.TerrainChunk.size,
				ChunkVoxels = ChunkVoxels,
				Gen = gen,
				voxels = runningJob.voxels.native
			};
			Profiler.EndSample();
			
			Profiler.BeginSample("job.Schedule");
			runningJob.JobHandle = job.Schedule(voxelsLength, ChunkVoxels);
			Profiler.EndSample();

			runningJobs.Add(chunk.coord, runningJob);

			Profiler.EndSample();
		}

		void OnGUI () {
			GUI.Label(new Rect(0, 90, 500,30), "Voxelizer Cache: "+ cache.Count +" / "+ CacheSize);
		}
	}
}
