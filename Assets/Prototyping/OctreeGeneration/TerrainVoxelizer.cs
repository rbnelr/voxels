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
		public NativeArray<Voxel>	native;
		public int refCount = 0;

		public void Use () {
			refCount++;
		}
		public void Dispose () {
			refCount--;
			if (refCount == 0)
				native.Dispose();
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

		public class RunningJob {
			public Voxels voxels;
			public TerrainNode node;
			public JobHandle JobHandle;
		}
		
		public class TerrainNodeCache : KeyedCollection<OctreeCoord, TerrainNode> {
			public delegate void NodeDeleter (TerrainNode n);
		
			public void MakeRoomForOne (int CacheSize, NodeDeleter onDelete) {
				if (CacheSize == 0)
					return;
				while (Count > 0 && Count >= CacheSize) { // uncache until we have Room for one 
					onDelete(this[0]);
					RemoveAt(0); // Remove oldest chached Voxel
				}
			}

			protected override OctreeCoord GetKeyForItem (TerrainNode item) {
				return item.coord;
			}
		}
		
		List<RunningJob> runningJobs = new List<RunningJob>();
		
		public TerrainGenerator terrainGenerator;
		public int MaxJobs = 3;
		
		public void ManualUpdate (List<TerrainNode> sortedNodes, TerrainOctree octree) {
			
			Profiler.BeginSample("StartJob loop");
			for (int i=0; i<sortedNodes.Count; ++i) {
				var node = sortedNodes[i];
				
				if (	node.needsVoxelize && // A voxelize was flagged
						runningJobs.Find(x => x.node.coord == node.coord) == null && // not running yet
						runningJobs.Count < MaxJobs // not too many jobs yet
						) {
					StartJob(node, octree.VoxelSize, octree.ChunkVoxels, terrainGenerator);
				}
			}
			Profiler.EndSample();

			Profiler.BeginSample("Finish Jobs");
			for (int i=0; i<runningJobs.Count; ++i) {
				var job = runningJobs[i];

				if (job.JobHandle.IsCompleted) {
					FinishJob(job);
					runningJobs.RemoveAtSwapBack(i);
				} else {
					++i;
				}
			}
			Profiler.EndSample();
		}

		void StartJob (TerrainNode node, float VoxelSize, int ChunkVoxels, TerrainGenerator gen) {
			Profiler.BeginSample("StartJob");

			int ArraySize = ChunkVoxels + 1;
			int voxelsLength = ArraySize * ArraySize * ArraySize;
			
			Profiler.BeginSample("new NativeArray");
			var voxels = new Voxels() { native = new NativeArray<Voxel>(voxelsLength, Allocator.Persistent) };
			voxels.Use();
			Profiler.EndSample();
			
			var runningJob = new RunningJob { node = node, voxels = voxels };

			Profiler.BeginSample("new Job");
			var job = new Job {
				ChunkVoxels = ChunkVoxels,
				Gen = gen,
				voxels = runningJob.voxels.native
			};
			job.ChunkPos = node.coord.ToWorldCube(VoxelSize, ChunkVoxels, out job.ChunkSize);
			Profiler.EndSample();
			
			Profiler.BeginSample("job.Schedule");
			runningJob.JobHandle = job.Schedule(voxelsLength, ArraySize*8);
			Profiler.EndSample();

			runningJobs.Add(runningJob);

			Profiler.EndSample();
		}
		void FinishJob (RunningJob job) {
			Profiler.BeginSample("FinishJob");
			job.JobHandle.Complete();

			job.node.AssignVoxels(job.voxels);
			job.voxels.Dispose();
			Profiler.EndSample();
		}

		void Dispose () {
			Profiler.BeginSample("OnDestroy");
			foreach (var job in runningJobs) {
				job.JobHandle.Complete(); // block main thread
				job.voxels.Dispose();
			}
			runningJobs.Clear();
			Profiler.EndSample();
		}

		void OnDestroy () {
			Dispose();
		}
	}
}
