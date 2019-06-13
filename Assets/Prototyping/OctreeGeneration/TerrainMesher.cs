using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace OctreeGeneration {
	public class TerrainMesher {
		static bool voxelInChild (TerrainNode node, int ChunkVoxels, int x, int y, int z) { // x,y,z: voxel index
			return node.getChild(	x / (ChunkVoxels / 2),
									y / (ChunkVoxels / 2),
									z / (ChunkVoxels / 2)) != null;
		}
		
		static Voxel lerp3D (TerrainNode n, Vector3Int a, Vector3Int b, Vector3 t) {
			var dummy = new Voxel { density = 0.0f, color = Color.black };

			var v000 =													n.voxels[a.z,a.y,a.x];
			var v001 = t.x==0.0f							? dummy :	n.voxels[a.z,a.y,b.x];
			var v010 =				t.y==0.0f				? dummy :	n.voxels[a.z,b.y,a.x];
			var v011 = t.x==0.0f || t.y==0.0f				? dummy :	n.voxels[a.z,b.y,b.x];
			var v100 =							 t.z==0.0f	? dummy :	n.voxels[b.z,a.y,a.x];
			var v101 = t.x==0.0f			  || t.z==0.0f	? dummy :	n.voxels[b.z,a.y,b.x];
			var v110 =			    t.y==0.0f || t.z==0.0f	? dummy :	n.voxels[b.z,b.y,a.x];
			var v111 = t.x==0.0f || t.y==0.0f || t.z==0.0f	? dummy :	n.voxels[b.z,b.y,b.x];
			return Voxel.Lerp(
				Voxel.Lerp(Voxel.Lerp(v000, v001, t.x), Voxel.Lerp(v010, v011, t.x), t.y),
				Voxel.Lerp(Voxel.Lerp(v100, v101, t.x), Voxel.Lerp(v110, v111, t.x), t.y),
			t.z);
		}
		static Voxel seamInterpolateVoxel (TerrainNode neighbour, Vector3 posWorld, int ChunkVoxels) {
			var pos = posWorld;
			pos -= neighbour.pos - new Vector3(neighbour.size/2, neighbour.size/2, neighbour.size/2);
			pos *= ChunkVoxels / neighbour.size;
			var a = VectorExt.FloorToInt(pos);
			var b = a + new Vector3Int(1,1,1);
			var t = pos -(Vector3)a;
			return lerp3D(neighbour, a, b, t);
		}

		public static void Meshize (TerrainNode node, int ChunkVoxels, TerrainController tc) {
			var sw = new System.Diagnostics.Stopwatch();
			sw.Start();

			var vertices = new List<Vector3>();
			var colors = new List<Color>();
			var uv = new List<Vector2>();
			var triangles = new List<int>();
			int index_counter = 0;

			var neighbours = new TerrainNode[,] { // need to find neighbours of equal or larger size to be able to fix up seams between differently sized nodes and to be able to avoid douplicate voxel cells
				{ tc.GetNeighbourTree(node, -1,0,0), tc.GetNeighbourTree(node, +1,0,0) },
				{ tc.GetNeighbourTree(node, 0,-1,0), tc.GetNeighbourTree(node, 0,+1,0) },
				{ tc.GetNeighbourTree(node, 0,0,-1), tc.GetNeighbourTree(node, 0,0,+1) },
			};

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
							
							var pos_local = new Vector3(x,y,z);
							pos_local *= (node.size / ChunkVoxels);
							pos_local += -new Vector3(node.size,node.size,node.size) * 0.5f;

							var pos_world = pos_local + node.pos;

							if (pos_world.x == -304 && pos_world.y == 16 && pos_world.z == -80) {
								int a = 5;
							}
						}
						
						for (int i=0; i<8; ++i) {
							var voxel_index = new Vector3Int(x,y,z) + MarchingCubes.corners[i];
							
							var pos_local = (Vector3)voxel_index;
							pos_local *= (node.size / ChunkVoxels);
							pos_local += -new Vector3(node.size,node.size,node.size) * 0.5f;

							var pos_world = pos_local + node.pos;
							
							Voxel voxel = new Voxel { density = 0.0f, color = Color.black };
							bool voxelGot = false;

							bool xSeam = voxel_index.x == 0 || voxel_index.x == ChunkVoxels;
							bool ySeam = voxel_index.y == 0 || voxel_index.y == ChunkVoxels;
							bool zSeam = voxel_index.z == 0 || voxel_index.z == ChunkVoxels;
							if (xSeam || ySeam || zSeam) { // for seam voxels look into neighbours if they exist
								TerrainNode biggestNeighbour = null;
								if (xSeam) {
									var neighb = neighbours[0, voxel_index.x == 0 ? 0:1];
									if (neighb != null && (biggestNeighbour == null || biggestNeighbour.size > neighb.size))
										biggestNeighbour = neighb;
								}
								if (ySeam) {
									var neighb = neighbours[1, voxel_index.y == 0 ? 0:1];
									if (neighb != null && (biggestNeighbour == null || biggestNeighbour.size > neighb.size))
										biggestNeighbour = neighb;
								}
								if (zSeam) {
									var neighb = neighbours[2, voxel_index.z == 0 ? 0:1];
									if (neighb != null && (biggestNeighbour == null || biggestNeighbour.size > neighb.size))
										biggestNeighbour = neighb;
								}
								if (biggestNeighbour != null) {
									voxel = seamInterpolateVoxel(biggestNeighbour, pos_world, ChunkVoxels);
									voxelGot = true;
								}
							}
							if (!voxelGot) {
								voxel = node.voxels[voxel_index.z, voxel_index.y, voxel_index.x];
							}

							cell.vert[i].pos = pos_local;
							cell.vert[i].color = voxel.color;
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
