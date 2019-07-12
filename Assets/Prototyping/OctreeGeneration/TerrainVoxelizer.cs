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
using static OctreeGeneration.VoxelUtil;

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
	}

	public struct TerrainGeneratorStruct {
		
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
			float3 normal = float3(1, 7, 2);
			return new Voxel {
				distance = dot(pos, normal),
				gradient = normal,
			};
			
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
	
	public class TerrainGenerator : MonoBehaviour {
		
		TerrainGeneratorStruct terrainGenerator = new TerrainGeneratorStruct();
		
		public GetVoxelsJob GetVoxels (float3 nodePos, float nodeSize) {
			return new GetVoxelsJob(nodePos, nodeSize, terrainGenerator);
		}

		[BurstCompile]
		struct Job : IJobParallelFor {
			[ReadOnly] public float3 NodePos;
			[ReadOnly] public float NodeSize;
			[ReadOnly] public TerrainGeneratorStruct Gen;
		
			[WriteOnly] public NativeArray<Voxel> voxels;

			public void Execute (int i) {
				int voxelIndex = i;
				int3 voxelCoord = flatTo3dIndex(i, TerrainNode.VOXEL_COUNT+1);

				float3 pos_world = (float3)voxelCoord;
				pos_world *= NodeSize / TerrainNode.VOXEL_COUNT;
				pos_world += NodePos;
						
				voxels[voxelIndex] = Gen.Generate(pos_world);
			}
		}

		public class GetVoxelsJob : NodeOperation {
			public Voxels Voxels;
			JobHandle? JobHandle;
			Job job;
			
			public GetVoxelsJob (float3 pos, float size, TerrainGeneratorStruct gen) {
				job = new Job {
					NodePos = pos,
					NodeSize = size,
					Gen = gen,
				};
			}
			public override void Schedule () {
				Profiler.BeginSample("GetVoxelsJob.Schedule()");

				int ArraySize = TerrainNode.VOXEL_COUNT + 1;
				int voxelsLength = ArraySize * ArraySize * ArraySize;
				
				Voxels = new Voxels { native = new NativeArray<Voxel>(voxelsLength, Allocator.Persistent) };

				Voxels.IncRef();
				job.voxels = Voxels.native;

				JobHandle = job.Schedule(voxelsLength, ArraySize);
				
				Profiler.EndSample();
			}
			public override bool IsCompleted () => JobHandle.Value.IsCompleted;
			public override void Apply (TerrainNode node) {
				JobHandle.Value.Complete();

				node.SetVoxels(Voxels);
				
				Dispose();
			}

			public override void Dispose () {
				if (JobHandle != null) {
					JobHandle.Value.Complete();
					
					Voxels.DecRef();

					JobHandle = null;
					Voxels = null;
				}
			}
		}
	}
}
