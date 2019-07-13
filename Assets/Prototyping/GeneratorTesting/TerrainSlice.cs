using System.Collections.Generic;
using UnityEngine;

namespace GeneratorTesting {
	[RequireComponent(typeof(MeshFilter), typeof(MeshRenderer))]
	public class TerrainSlice : MonoBehaviour {
		
		public Vector3Int size = new Vector3Int(10, 10, 10);
		Vector3 sizef { get { return new Vector3(size.x, size.y, size.z); } }

		[Range(0.25f, 50)]
		public float voxelSize = 1;

		public float densityThres = 0;
		
		public TerrainGenerator generator;

		Mesh mesh;
		
		void OnEnable () {
			FullUpdate();
		}
		
		private void Update () {
			if (transform.hasChanged || generator.hasChanged) {
				transform.hasChanged = false;
				FullUpdate();
			}
		}

		void OnDrawGizmosSelected () {
			Gizmos.color = Color.red;
			Gizmos.matrix = transform.localToWorldMatrix;
			Gizmos.DrawWireCube(new Vector3(0,0,0), new Vector3(size.x, size.y, size.z));
		}

		Vector3 voxel_to_world (Vector3Int pos) {
			return transform.TransformPoint((Vector3)pos * voxelSize -sizef/2);
		}
		Vector3 voxel_to_local (Vector3Int pos) {
			return (Vector3)pos * voxelSize -sizef/2;
		}

		public void FullUpdate () {
			Vector3Int voxelCount = VectorExt.Max(new Vector3Int(1,1,1),
				VectorExt.CeilToInt(sizef / voxelSize)) + new Vector3Int(1,1,1);

			Voxel[,,] voxels = new Voxel[voxelCount.z, voxelCount.y, voxelCount.x];

			for (int z=0; z<voxelCount.z; ++z) {
				for (int y=0; y<voxelCount.y; ++y) {
					for (int x=0; x<voxelCount.x; ++x) {
						var pos_world = voxel_to_world(new Vector3Int(x,y,z));
						
						voxels[z,y,x] = generator.Generate(pos_world);
					}
				}
			}

			if (mesh == null) {
				mesh = new Mesh();
				mesh.name = "TerrainSlide Generated Mesh";
				mesh.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32;
				GetComponent<MeshFilter>().mesh = mesh;
			}

			Voxel dummy_voxel = new Voxel { density = 0, color = new Color(0.7f, 0.7f, 0.7f) };
			
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

			for (int z=-1; z<voxelCount.z; ++z) {
				for (int y=-1; y<voxelCount.y; ++y) {
					for (int x=-1; x<voxelCount.x; ++x) {
						for (int i=0; i<8; ++i) {
							var voxel_index = new Vector3Int(x,y,z) + MarchingCubes.corners[i];
							
							Voxel voxel = dummy_voxel;
							if (		voxel_index.x >= 0 && voxel_index.x < voxelCount.x
									&&	voxel_index.y >= 0 && voxel_index.y < voxelCount.y
									&&	voxel_index.z >= 0 && voxel_index.z < voxelCount.z) {
								voxel = voxels[voxel_index.z, voxel_index.y, voxel_index.x];
							}

							var pos_local = voxel_to_local(new Vector3Int(voxel_index.x,voxel_index.y,voxel_index.z));

							cell.vert[i].pos = pos_local;
							cell.vert[i].color = voxel.color;
							cell.val[i] = voxel.density;
						}
						
						int ntriangles = MarchingCubes.Polygonise(cell, densityThres, ref tris);
						
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

			mesh.Clear();
			mesh.vertices = vertices.ToArray();
			mesh.colors = colors.ToArray();
			mesh.uv = uv.ToArray();
			mesh.triangles = triangles.ToArray();
			mesh.RecalculateNormals();
		}
	}
}
