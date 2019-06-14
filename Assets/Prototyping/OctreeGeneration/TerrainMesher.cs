using Unity.Jobs;
using Unity.Mathematics;
using static Unity.Mathematics.math;
using Unity.Collections;
using UnityEngine;

namespace OctreeGeneration {
	public struct TerrainMeshJob : IJob {
		[ReadOnly] public float3 ChunkPos;
		[ReadOnly] public float ChunkSize;
		[ReadOnly] public int ChunkVoxels;
		[ReadOnly] public NativeArray<Voxel> Voxels;

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

			var cell = new MarchingCubes.Gridcell { vert = new MarchingCubes.Vertex[8], val = new float[8] };
			var tris = new MarchingCubes.Triangle[5] {
				new MarchingCubes.Triangle { vert = new MarchingCubes.Vertex[3] },
				new MarchingCubes.Triangle { vert = new MarchingCubes.Vertex[3] },
				new MarchingCubes.Triangle { vert = new MarchingCubes.Vertex[3] },
				new MarchingCubes.Triangle { vert = new MarchingCubes.Vertex[3] },
				new MarchingCubes.Triangle { vert = new MarchingCubes.Vertex[3] },
			};

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
							
							var voxel = TerrainChunk.GetVoxel(ref Voxels, voxel_index, ChunkVoxels);

							cell.vert[i].pos = pos_local;
							cell.vert[i].color = Color.white;
							cell.val[i] = voxel.density;
						}
						
						int ntriangles = MarchingCubes.Polygonise(cell, 0.0f, ref tris);
						
						for (int i=0; i<ntriangles; ++i) {
							var a = tris[i].vert[0];
							var b = tris[i].vert[1];
							var c = tris[i].vert[2];

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

	public struct TerrainMesher {
		//public JobHandle? jobHandle;
		//
		//NativeList<Vector3>?	vertices;
		//NativeList<Color>?		colors;
		//NativeList<Vector2>?	uv;
		//NativeList<int>?		triangles;
		//
		//public void StartJob (TerrainChunk chunk, int ChunkVoxels) {
		//	vertices	= new NativeList<Vector3>(	Allocator.Persistent );
		//	colors		= new NativeList<Color>(	Allocator.Persistent );
		//	uv			= new NativeList<Vector2>(	Allocator.Persistent );
		//	triangles	= new NativeList<int>(		Allocator.Persistent );
		//
		//	var job = new TerrainMeshJob {
		//		ChunkPos = chunk.pos,
		//		ChunkSize = chunk.size,
		//		ChunkVoxels = ChunkVoxels,
		//		Voxels = chunk.voxels.Value,
		//		
		//		vertices	= vertices.Value,
		//		colors		= colors.Value,
		//		uv			= uv.Value,
		//		triangles	= triangles.Value,
		//	};
		//	jobHandle = job.Schedule();
		//}
		//
		//public void Update (TerrainChunk chunk) {
		//	if (jobHandle != null && jobHandle.Value.IsCompleted) {
		//		jobHandle.Value.Complete();
		//		jobHandle = null;
		//
		//		chunk.needsRemesh = false;
		//		
		//		chunk.mesh.Clear();
		//		chunk.mesh.vertices		= vertices.Value.ToArray();
		//		chunk.mesh.colors		= colors.Value.ToArray();
		//		chunk.mesh.uv			= uv.Value.ToArray();
		//		chunk.mesh.triangles	= triangles.Value.ToArray();
		//		chunk.mesh.RecalculateNormals();
		//
		//		vertices	.Value.Dispose();
		//		colors		.Value.Dispose();
		//		uv			.Value.Dispose();
		//		triangles	.Value.Dispose();
		//	}
		//}
		//
		//public void Dispose () {
		//	if (jobHandle != null) {
		//		Debug.Log("TerrainMesher.Dispose(): Job still running, need to wait for it to complete inorder to Dispose of the native arrays");
		//		jobHandle.Value.Complete();
		//		jobHandle = null;
		//	}
		//
		//	vertices?.Dispose();
		//	colors?.Dispose();
		//	uv?.Dispose();
		//	triangles?.Dispose();
		//}
	}
}
