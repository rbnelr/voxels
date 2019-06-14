using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using Unity.Mathematics;
using static Unity.Mathematics.math;

namespace OctreeGeneration {
	public class TerrainMesher {
		static bool voxelInChild (TerrainChunk node, int ChunkVoxels, int x, int y, int z) { // x,y,z: voxel index
			return node.getChild(	x / (ChunkVoxels / 2),
									y / (ChunkVoxels / 2),
									z / (ChunkVoxels / 2)) != null;
		}

		public static void Meshize (TerrainChunk node, int ChunkVoxels, TerrainController tc) {
			var sw = new System.Diagnostics.Stopwatch();
			sw.Start();

			var vertices = new List<Vector3>();
			var colors = new List<Color>();
			var uv = new List<Vector2>();
			var triangles = new List<int>();
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
						if (voxelInChild(node, ChunkVoxels, x,y,z))
							continue;
						{
							
							var pos_local = new float3(x,y,z);
							pos_local *= (node.size / ChunkVoxels);
							pos_local += -new float3(node.size,node.size,node.size) * 0.5f;

							var pos_world = pos_local + node.pos;
						}
						
						for (int i=0; i<8; ++i) {
							var voxel_index = new int3(x,y,z) + MarchingCubes.corners[i];
							
							var pos_local = (float3)voxel_index;
							pos_local *= (node.size / ChunkVoxels);
							pos_local += -new float3(node.size,node.size,node.size) * 0.5f;

							var pos_world = pos_local + node.pos;
							
							var voxel = node.GetVoxel(voxel_index, ChunkVoxels);

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

			node.mesh.Clear();
			node.mesh.vertices = vertices.ToArray();
			node.mesh.colors = colors.ToArray();
			node.mesh.uv = uv.ToArray();
			node.mesh.triangles = triangles.ToArray();
			node.mesh.RecalculateNormals();
			
			sw.Stop();
			
			Debug.Log("TerrainMesher.Meshize(): took "+ sw.Elapsed.TotalMilliseconds +" ms ");
		}
	}
}
