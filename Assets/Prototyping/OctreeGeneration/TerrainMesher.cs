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

			[WriteOnly] public NativeList<Vector3>	vertices;
			[WriteOnly] public NativeList<Color>	colors;
			[WriteOnly] public NativeList<Vector2>	uv;
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
						
							int ntriangles = MarchingCubes.Polygonise(cell, 0.0f, verts);
						
							for (int i=0; i<ntriangles; ++i) {
								var a = verts[i*3 +0];
								var b = verts[i*3 +1];
								var c = verts[i*3 +2];

								vertices.Add(a.pos);
								vertices.Add(b.pos);
								vertices.Add(c.pos);
							
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
		
		void _collectChunks (TerrainNode node, ref List<TerrainNode> list) {
			if (node.TerrainChunk != null)
				list.Add(node);
			else
				for (int i=0; i<8; ++i)
					_collectChunks(node.Children[i], ref list);
		}
		List<TerrainNode> collectChunks (TerrainNode node) {
			Profiler.BeginSample("collectChunks");
			var list = new List<TerrainNode>();
			_collectChunks(node, ref list);
			Profiler.EndSample();
			return list;
		}
		
		float calcDistToPlayer (TerrainNode node) {
			return octree.CalcDistToPlayer(node.TerrainChunk.pos, node.TerrainChunk.size);
		}
		
		void Update () {
			if (octree.root == null)
				return;

			{
				var chunks = collectChunks(octree.root);
		
				Profiler.BeginSample("chunks.Sort()");
				chunks.Sort( (l, r)=> calcDistToPlayer(l).CompareTo(calcDistToPlayer(r)) );
				Profiler.EndSample();
		
				Profiler.BeginSample("StartJob loop");
				foreach (var chunk in chunks) {
					var voxels = voxelizer.GetCachedVoxels(chunk.coord);
					if (chunk.TerrainChunk.needsRemesh && voxels != null && !runningJobs.ContainsKey(chunk.coord) && runningJobs.Count < MaxJobs) {
						StartJob(chunk, octree.ChunkVoxels, voxels);
					}
				}
				Profiler.EndSample();
			}
		
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
			var runningJob = new RunningJob();

			runningJob.chunk = node.TerrainChunk;

			runningJob.job = new Job {
				ChunkPos = node.TerrainChunk.pos,
				ChunkSize = node.TerrainChunk.size,
				ChunkVoxels = ChunkVoxels,
				voxels = voxels.native,
				
				vertices	= new NativeList<Vector3>(	Allocator.Persistent ),
				colors		= new NativeList<Color>(	Allocator.Persistent ),
				uv			= new NativeList<Vector2>(	Allocator.Persistent ),
				triangles	= new NativeList<int>(		Allocator.Persistent ),
			};
			runningJob.JobHandle = runningJob.job.Schedule();
		
			runningJobs.Add(node.coord, runningJob);
		}
		
		void FinishJob (RunningJob job) {
			Profiler.BeginSample("FinishJob");
			job.JobHandle.Complete();
			
			if (job.chunk.mesh == null) {
				// chunk was deleted, ignore result
			} else {
				job.chunk.mesh.Clear();
				Profiler.BeginSample("NativeArray.ToArray() for mesh attributes");
				job.chunk.mesh.vertices		= job.job.vertices.ToArray();
				job.chunk.mesh.colors		= job.job.colors.ToArray();
				job.chunk.mesh.uv			= job.job.uv.ToArray();
				job.chunk.mesh.triangles	= job.job.triangles.ToArray();
				Profiler.EndSample();
				job.chunk.mesh.RecalculateNormals();
			
				Profiler.BeginSample("NativeArray.Dispose");
				job.job.vertices	.Dispose();
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
