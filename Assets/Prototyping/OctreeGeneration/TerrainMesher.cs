using Unity.Jobs;
using Unity.Mathematics;
using static Unity.Mathematics.math;
using Unity.Collections;
using UnityEngine;
using Unity.Burst;
using System.Collections.Generic;
using UnityEngine.Profiling;

namespace OctreeGeneration {
	
	[RequireComponent(typeof(TerrainOctree), typeof(TerrainVoxelizer))]
	public class TerrainMesher : MonoBehaviour {
		[BurstCompile]
		public struct Job : IJob {
			[ReadOnly] public float3 ChunkPos;
			[ReadOnly] public float ChunkSize;
			[ReadOnly] public int ChunkVoxels;
			[ReadOnly] public NativeArray<Voxel> voxels;
			[ReadOnly] public float DensityIsoLevel;

			[WriteOnly] public NativeList<float3>	vertices;
			[WriteOnly] public NativeList<float3>	normals;
			[WriteOnly] public NativeList<Color>	colors;
			[WriteOnly] public NativeList<float2>	uv;
			[WriteOnly] public NativeList<int>		triangles;

			bool voxelInChild (TerrainChunk node, int ChunkVoxels, int x, int y, int z) { // x,y,z: voxel index
				//return node.getChild(	x / (ChunkVoxels / 2),
				//						y / (ChunkVoxels / 2),
				//						z / (ChunkVoxels / 2)) != null;
				return false;
			}

			public void Execute () {
				int index_counter = 0;
				
				var cell = new MarchingCubes.Gridcell {
					vert = new NativeArray<MarchingCubes.Vertex>(8, Allocator.Temp, NativeArrayOptions.UninitializedMemory),
					val = new NativeArray<float>(8, Allocator.Temp, NativeArrayOptions.UninitializedMemory),
				};
				var verts = new NativeArray<MarchingCubes.Vertex>(5*3, Allocator.Temp, NativeArrayOptions.UninitializedMemory);
				
				var vertlist = new NativeArray<MarchingCubes.Vertex>(12, Allocator.Temp, NativeArrayOptions.UninitializedMemory);

				for (int z=0; z<ChunkVoxels; ++z) {
					for (int y=0; y<ChunkVoxels; ++y) {
						for (int x=0; x<ChunkVoxels; ++x) {
							//if (voxelInChild(node, ChunkVoxels, x,y,z))
							//	continue;
							
							for (int i=0; i<8; ++i) {
								var voxel_index = new int3(x,y,z) + MarchingCubes.corners[i];
							
								var pos_local = (float3)voxel_index;
								pos_local *= ChunkSize / ChunkVoxels;
								pos_local -= ChunkSize * 0.5f;

								var pos_world = pos_local + ChunkPos;
							
								var voxel = voxels[Voxels._3dToFlatIndex(voxel_index, ChunkVoxels)];

								//var c = normalize(voxel.gradient) * 0.5f + 0.5f;
								var c = normalize(voxel.gradient);
								cell.vert[i] = new MarchingCubes.Vertex { pos = pos_local, color = Color.white, /*new Color(c.x,c.y,c.z),*/ normal = normalize(voxel.gradient) };
								cell.val[i] = voxel.distance;
							}
							
							int ntriangles = MarchingCubes.Polygonise(cell, DensityIsoLevel, verts, ref vertlist);
						
							for (int i=0; i<ntriangles; ++i) {
								var a = verts[i*3 +0];
								var b = verts[i*3 +1];
								var c = verts[i*3 +2];

								vertices.Add(a.pos);
								vertices.Add(b.pos);
								vertices.Add(c.pos);

								var flatNormal = cross(b.pos - a.pos, c.pos - a.pos);
								
								normals.Add(flatNormal);
								normals.Add(flatNormal);
								normals.Add(flatNormal);
								
								//normals.Add(a.normal);
								//normals.Add(b.normal);
								//normals.Add(c.normal);
							
								colors.Add(a.color);
								colors.Add(b.color);
								colors.Add(c.color);

								uv.Add(new Vector2(a.pos.x, a.pos.y));
								uv.Add(new Vector2(b.pos.x, b.pos.y));
								uv.Add(new Vector2(c.pos.x, c.pos.y));

								triangles.Add(index_counter++);
								triangles.Add(index_counter++);
								triangles.Add(index_counter++);
							}
						}
					}
				}

			}
		}
		
		public class RunningJob {
			public TerrainNode	node;
			public Job			job;
			public JobHandle	JobHandle;
		}
		
		List<RunningJob> runningJobs = new List<RunningJob>();
		
		public int MaxJobs = 10;
		
		public void ManualUpdateStartJobs (List<TerrainNode> sortedNodes, TerrainOctree octree) {
			
			Profiler.BeginSample("StartJob loop");
			for (int i=0; i<sortedNodes.Count; ++i) {
				var node = sortedNodes[i];
				if (	node.needsRemesh && // remesh was flagged
						node.voxels != null && // we have voxels yet (if these voxels are up to date or if there if already a voxelize job is handled by the octree)
						runningJobs.Find(x => x.node.coord == node.coord) == null && // no job yet
						runningJobs.Count < MaxJobs) { // not already too many jobs
					StartJob(node, octree.VoxelSize, octree.ChunkVoxels, octree.DensityIsoLevel);
				}
			}
			Profiler.EndSample();
		}
		public void ManualUpdateFinishJobs (List<TerrainNode> sortedNodes, TerrainOctree octree) {
			
			Profiler.BeginSample("FinishJob loop");
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
		
		void StartJob (TerrainNode node, float VoxelSize, int ChunkVoxels, float DensityIsoLevel) {
			Profiler.BeginSample("StartJob()");
			
			Profiler.BeginSample("new RunningJob()");
			var runningJob = new RunningJob { node = node };
			Profiler.EndSample();
			
			int initCap = ChunkVoxels * ChunkVoxels * MarchingCubes.MaxVartsPerCell;
			node.voxels.Use();

			Profiler.BeginSample("new Job");
			runningJob.job = new Job {
				ChunkVoxels = ChunkVoxels,
				voxels = node.voxels.native,
				DensityIsoLevel = DensityIsoLevel,
				
				vertices	= new NativeList<float3>(	initCap, Allocator.Persistent ),
				normals		= new NativeList<float3>(	initCap, Allocator.Persistent ),
				colors		= new NativeList<Color>(	initCap, Allocator.Persistent ),
				uv			= new NativeList<float2>(	initCap, Allocator.Persistent ),
				triangles	= new NativeList<int>(		initCap, Allocator.Persistent ),
			};
			runningJob.job.ChunkPos = node.coord.ToWorldCube(VoxelSize, ChunkVoxels, out runningJob.job.ChunkSize);
			Profiler.EndSample();

			Profiler.BeginSample("job.Schedule");
			runningJob.JobHandle = runningJob.job.Schedule();
			Profiler.EndSample();
		
			runningJobs.Add(runningJob);
			Profiler.EndSample();
		}

		List<Vector3> verticesBuf = new List<Vector3>();
		List<Vector3> normalsBuf = new List<Vector3>();
		List<Vector2> uvsBuf = new List<Vector2>();
		List<Color> colorsBuf = new List<Color>();
		List<int> trianglesBuf = new List<int>();
		
		void FinishJob (RunningJob job) {
			Profiler.BeginSample("FinishJob");

			job.JobHandle.Complete();
			
			job.node.voxels.Dispose();

			if (job.node.IsDestroyed) {
				// node was deleted, ignore result
			} else {
				job.node.AssignMesh(job.job.vertices, ref verticesBuf, job.job.normals, ref normalsBuf, job.job.uv, ref uvsBuf, job.job.colors, ref colorsBuf, job.job.triangles, ref trianglesBuf);
				
				Profiler.BeginSample("NativeArray.Dispose");
				job.job.vertices	.Dispose();
				job.job.normals		.Dispose();
				job.job.colors		.Dispose();
				job.job.uv			.Dispose();
				job.job.triangles	.Dispose();
				Profiler.EndSample();
			}
			Profiler.EndSample();
		}
		
		public void Dispose () {
			Profiler.BeginSample("Dispose");
			foreach (var job in runningJobs) {
				job.JobHandle.Complete(); // block main thread
				
				job.job.vertices.Dispose();
				job.job.normals.Dispose();
				job.job.colors.Dispose();
				job.job.uv.Dispose();
				job.job.triangles.Dispose();
			}
			runningJobs.Clear();
			Profiler.EndSample();
		}

		void OnDestroy () {
			Dispose();
		}
	}
}
