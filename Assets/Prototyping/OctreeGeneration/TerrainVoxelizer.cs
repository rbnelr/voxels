using Unity.Jobs;
using Unity.Mathematics;
using static Unity.Mathematics.math;
using Unity.Collections;
using UnityEngine;
using System.Collections.Generic;
using Unity.Burst;
using UnityEngine.Profiling;
using System.Collections.ObjectModel;
using static NoiseExt;

namespace OctreeGeneration {
	public struct Voxel {
		public float distance;
		public float3 gradient;
		
		public static Voxel Lerp (Voxel a, Voxel b, float t) {
			a.distance  = a.distance  + (b.distance  - a.distance ) * t;
			a.gradient = a.gradient + (b.gradient - a.gradient) * t;
			return a;
		}

		public override string ToString () {
			return distance.ToString();
		}
	}
	
	public class Voxels {
		public NativeArray<Voxel>	native;
		public int refCount = 0;

		public void IncRef () {
			refCount++;
		}
		public void DecRef () {
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
		public NoiseSample3 Surface (float3 pos) {
			float2 pos2d = pos.xz;

			var height = fsnoise(pos2d + 3000, 4000, 6, true) * 0.1f;
			//float height = fractal(pos2d + 3000, 4, 4000) * 0.1f;

			var val = pos.y - height;

			return new NoiseSample3 { // map from 2d noise gradients to 3d
				val = val.val,
				gradient = float3(val.gradient.x, 0, val.gradient.y)
			};
		}
		// TODO: change to be a "multiplier" for cave intensity
		public NoiseSample3 Abyss (float3 pos) {
			var abyssX = new NoiseSample3 { val = pos.x, gradient = float3(1,0,0) };
			var abyssZ = new NoiseSample3 { val = pos.z, gradient = float3(0,0,1) };
		
			const float radius = 555;

			var strength = fsnoise(pos.y + 999, radius * 6, 2);
			
			var offsX = fsnoise(pos.y + 10000, radius * 2, 3) * radius * (0.6f + strength * 0.5f);
			var offsZ = fsnoise(pos.y - 10000, radius * 2, 3) * radius * (0.6f + strength * 0.5f);
			
			abyssX.val += offsX.val;
			abyssX.gradient.y += offsX.gradient;
			
			abyssZ.val += offsZ.val;
			abyssZ.gradient.y += offsZ.gradient;
		
			var radiusSample = new NoiseSample3 { val = 555, gradient = 0 };
			
			var radiusScale = 1f + fsnoise(pos.y + 20000, radius * 4, 3) * 0.7f;
			radiusSample *= new NoiseSample3 { val = radiusScale.val, gradient = float3(0, radiusScale.gradient, 0) };
		
			return (radiusSample - sqrt(abyssX*abyssX + abyssZ*abyssZ)) / radiusSample;
		}
		public NoiseSample3 Cave (float3 pos) {
			return fsnoise(pos, 600, 3);
		}
		public Voxel Generate (float3 pos) {
			//return new Voxel {
			//	density = dot(pos, normalize(float3(1,2,3))),
			//};
			
			pos *= 12f; // for testing

			var surf = Surface(pos);
			
			var abyss = Abyss(pos);
			var cave = Cave(pos);
			
			cave = cave - 1f + abyss * 2.2f;
			
			pos /= 12f;
			cave = min(cave, Cube(pos, float3(14f, 0.5f, 10.6f), 5f));
			cave = min(cave, Sphere(pos, float3(14f, 0.7f, 10.6f - 8f), 3f));
			cave = max(cave, -1 * Sphere(pos, float3(15.82f, 16.79f, -12.94f), 12.94f-7.23f));

			return new Voxel {
				distance = cave.val,
				gradient = cave.gradient,
			};
			
			//var val = smooth_union(surf, cave, 2000f);
			//
			////val += fractal(pos + 400, 5, 220) * 0.15f;
			//
			//return new Voxel {
			//	distance = val,
			//};
		}

		public static int getLargestAxis (float3 v) {
			int i = v.x >= v.y ? 0 : 1;
			i = v[i] >= v.z ? i : 2;
			return i;
		}
		public NoiseSample3 Cube (float3 pos, float3 cubePos, float3 radius) {
			pos -= cubePos; // cube is now at origin
			
			float val ;
			float3 gradient;

			if (all(pos == 0)) {
				val = 0;
				gradient = float3(0, 1, 0);
			} else {
				int axis = getLargestAxis(abs(pos));

				gradient = 0;
				gradient[axis] = sign(pos[axis]);

				val = abs(pos[axis]) - radius[axis];
			}

			return new NoiseSample3 {
				val = val,
				gradient = gradient,
			};
		}
		public NoiseSample3 Sphere (float3 pos, float3 spherePos, float radius) {
			float3 offs = pos - spherePos;
			
			return new NoiseSample3 {
				val = length(offs) - radius,
				gradient = normalizesafe(offs),
			};
		}
	}
	
	public class TerrainVoxelizer : MonoBehaviour {
		
		[BurstCompile]
		struct Job : IJobParallelFor {
			[ReadOnly] public float3 ChunkPos;
			[ReadOnly] public float NodeSize;
			[ReadOnly] public int NodeVoxels;
			[ReadOnly] public TerrainGenerator Gen;
		
			[WriteOnly] public NativeArray<Voxel> voxels;

			public void Execute (int i) {
				int voxelIndex = i;
				int3 voxelCoord = Voxels.flatTo3dIndex(i, NodeVoxels);

				float3 pos_world = (float3)voxelCoord;
				pos_world *= NodeSize / NodeVoxels;
				pos_world += ChunkPos;
						
				voxels[voxelIndex] = Gen.Generate(pos_world);
			}
		}

		class RunningJob {
			public Voxels voxels;
			public TerrainNode node;
			public JobHandle JobHandle;
		}
		
		List<RunningJob> runningJobs = new List<RunningJob>();
		
		TerrainGenerator terrainGenerator = new TerrainGenerator();
		public int MaxJobs = 3;
		
		public void ManualUpdate (List<TerrainNode> sortedNodes, TerrainOctree octree) {
			
			Profiler.BeginSample("StartJob loop");
			for (int i=0; i<sortedNodes.Count; ++i) {
				var node = sortedNodes[i];
				
				if (	node.needsVoxelize && // A voxelize was flagged
						runningJobs.Find(x => x.node == node) == null && // not running yet
						runningJobs.Count < MaxJobs // not too many jobs yet
						) {
					StartJob(node, octree);
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

		void StartJob (TerrainNode node, TerrainOctree octree) {
			Profiler.BeginSample("StartJob");

			int ArraySize = octree.ChunkVoxels + 1;
			int voxelsLength = ArraySize * ArraySize * ArraySize;
			
			Profiler.BeginSample("new NativeArray");
			var voxels = new Voxels() { native = new NativeArray<Voxel>(voxelsLength, Allocator.Persistent) };
			voxels.IncRef();
			Profiler.EndSample();
			
			var runningJob = new RunningJob { node = node, voxels = voxels };

			Profiler.BeginSample("new Job");
			var job = new Job {
				NodeVoxels = octree.ChunkVoxels,
				Gen = terrainGenerator,
				voxels = runningJob.voxels.native
			};
			job.ChunkPos = node.pos;
			job.NodeSize = node.size;
			Profiler.EndSample();
			
			Profiler.BeginSample("job.Schedule");
			runningJob.JobHandle = job.Schedule(voxelsLength, ArraySize);
			Profiler.EndSample();

			runningJobs.Add(runningJob);

			Profiler.EndSample();
		}
		void FinishJob (RunningJob job) {
			Profiler.BeginSample("FinishJob");
			job.JobHandle.Complete();

			if (!job.node.IsDestroyed)
				job.node.AssignVoxels(job.voxels);
			job.voxels.DecRef();
			Profiler.EndSample();
		}

		void Dispose () {
			Profiler.BeginSample("OnDestroy");
			foreach (var job in runningJobs) {
				job.JobHandle.Complete(); // block main thread
				job.voxels.DecRef();
			}
			runningJobs.Clear();
			Profiler.EndSample();
		}

		void OnDestroy () {
			Dispose();
		}
		
		void OnGUI () {
			GUI.Label(new Rect(0, 40, 500,30), "Voxelizer Jobs: ");

			for (int i=0; i<runningJobs.Count; ++i) {
				var job = runningJobs[i];
				int3 name = (int3)floor(job.node.pos / job.node.size);

				GUI.color = TerrainOctree.drawColors[clamp(job.node.lod % TerrainOctree.drawColors.Length, 0, TerrainOctree.drawColors.Length-1)];

				GUI.Label(new Rect(100 + i*100, 40, 100,30), name.ToString());
			}
		}
	}
}
