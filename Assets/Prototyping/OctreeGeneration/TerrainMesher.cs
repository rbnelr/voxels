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

								cell.vert[i] = new MarchingCubes.Vertex { pos = pos_local, color = Color.white };
								cell.val[i] = voxel.density;
							}
							
							int ntriangles = MarchingCubes.Polygonise(cell, 0.0f, verts, ref vertlist);
						
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
		
		class RunningJob {
			public TerrainChunk chunk;
			public Job job;
			public JobHandle JobHandle;
		}
		
		TerrainOctree octree;
		TerrainVoxelizer voxelizer;
		
		Dictionary<OctreeCoord, RunningJob> runningJobs = new Dictionary<OctreeCoord, RunningJob>();
		
		public TerrainGenerator terrainGenerator;
		public int MaxJobs = 10;
		
		void Awake () {
			octree = GetComponent<TerrainOctree>();
			voxelizer = GetComponent<TerrainVoxelizer>();
		}
		
		void Update () {
			if (octree.root == null)
				return;
			
			Profiler.BeginSample("StartJob loop");
			for (int i=0; i<octree.SortedTerrainChunks.Count; ++i) {
				var chunk = octree.SortedTerrainChunks[i];
				var voxels = voxelizer.GetCachedVoxels(chunk.coord);
				if (chunk.TerrainChunk.needsRemesh && voxels != null && !runningJobs.ContainsKey(chunk.coord) && runningJobs.Count < MaxJobs) {
					StartJob(chunk, octree.ChunkVoxels, voxels);
				}
			}
			Profiler.EndSample();
		
			Profiler.BeginSample("FinishJob loop");
			var toRemove = new List<OctreeCoord>();
		
			foreach (var job in runningJobs) {
				if (job.Value.JobHandle.IsCompleted) {
					job.Value.JobHandle.Complete();
		
					FinishJob(job.Value);
					
					toRemove.Add(job.Key);
				}
			}
			
			foreach (var j in toRemove) {
				runningJobs.Remove(j);
			}
			Profiler.EndSample();
		}
		
		void StartJob (TerrainNode node, int ChunkVoxels, Voxels voxels) {
			Profiler.BeginSample("StartJob()");
			
			Profiler.BeginSample("new RunningJob()");
			var runningJob = new RunningJob();
			Profiler.EndSample();

			runningJob.chunk = node.TerrainChunk;
			
			Profiler.BeginSample("new Job");
			runningJob.job = new Job {
				ChunkPos = node.TerrainChunk.pos,
				ChunkSize = node.TerrainChunk.size,
				ChunkVoxels = ChunkVoxels,
				voxels = voxels.native,
				
				vertices	= new NativeList<float3>(	Allocator.Persistent ),
				normals		= new NativeList<float3>(	Allocator.Persistent ),
				colors		= new NativeList<Color>(	Allocator.Persistent ),
				uv			= new NativeList<float2>(	Allocator.Persistent ),
				triangles	= new NativeList<int>(		Allocator.Persistent ),
			};
			Profiler.EndSample();

			Profiler.BeginSample("job.Schedule");
			runningJob.JobHandle = runningJob.job.Schedule();
			Profiler.EndSample();
		
			runningJobs.Add(node.coord, runningJob);
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
			
			if (job.chunk.mesh == null) {
				// chunk was deleted, ignore result
			} else {
				job.chunk.mesh.Clear();
				Profiler.BeginSample("NativeArray.ToArray() for mesh attributes");
					Profiler.BeginSample("vertices");
						job.chunk.mesh.SetVerticesNative(job.job.vertices, ref verticesBuf);
					Profiler.EndSample();
					Profiler.BeginSample("normals");
						job.chunk.mesh.SetNormalsNative(job.job.normals, ref normalsBuf);
					Profiler.EndSample();
					Profiler.BeginSample("uv");
						job.chunk.mesh.SetUvsNative(0, job.job.uv, ref uvsBuf);
					Profiler.EndSample();
					Profiler.BeginSample("colors");
						job.chunk.mesh.SetColorsNative(job.job.colors, ref colorsBuf);
					Profiler.EndSample();
					Profiler.BeginSample("triangles");
						job.chunk.mesh.SetTrianglesNative(job.job.triangles, 0, ref trianglesBuf);
					Profiler.EndSample();
				Profiler.EndSample();
			
				Profiler.BeginSample("NativeArray.Dispose");
				job.job.vertices	.Dispose();
				job.job.normals		.Dispose();
				job.job.colors		.Dispose();
				job.job.uv			.Dispose();
				job.job.triangles	.Dispose();
				Profiler.EndSample();

				job.chunk.needsRemesh = false;
			}
			Profiler.EndSample();
		}
		
		public void Dispose () {
			Profiler.BeginSample("Dispose");
			var toRemove = new List<OctreeCoord>();

			foreach (var job in runningJobs) {
				job.Value.JobHandle.Complete(); // block main thread
				
				job.Value.job.vertices.Dispose();
				job.Value.job.normals.Dispose();
				job.Value.job.colors.Dispose();
				job.Value.job.uv.Dispose();
				job.Value.job.triangles.Dispose();
				
				toRemove.Add(job.Key);
			}
			
			foreach (var j in toRemove) {
				runningJobs.Remove(j);
			}
			Profiler.EndSample();
		}

		void OnDestroy () {
			Dispose();
		}
	}
}
