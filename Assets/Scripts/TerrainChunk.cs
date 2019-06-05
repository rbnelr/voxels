using System.Collections;
using System.Collections.Generic;
using System.Threading;
using Unity.Collections;
using UnityEngine;

public class TerrainChunk {
	public const int SIZE = 64;

	public struct Voxel { 
		public float density;
	}

	public GameObject gameObject;
	public Vector3Int pos;

	public int lod = -1;
	public Voxel[,,] voxels;
	public Mesh mesh;
	public MeshRenderer mesh_renderer;

	public TerrainController terrain_controller;
	
	VoxelizeTask voxelize_task = null;
	RemeshTask remesh_task = null;
	
	public static int calc_voxel_size (int lod) { // size of one voxel
		int size = 1 << lod;
		return size <= SIZE ? size : SIZE;
	}
	public static int calc_resolution (int lod) { // voxels per chunk
		return SIZE / calc_voxel_size(lod);
	}

	public int voxel_size () { // size of one voxel
		return calc_voxel_size(lod);
	}
	public int resolution () { // voxels per chunk
		return calc_resolution(lod);
	}

	public void update (int new_lod) {
		bool need_remesh = false;

		if (voxelize_task != null && voxelize_task.done) {
			lod = voxelize_task.lod;
			voxels = voxelize_task.voxels;
			
			voxelize_task = null;

			need_remesh = true;
		}

		if (new_lod != lod) {
			if (voxelize_task == null && remesh_task == null) { // shedule voxelize job if not yet sheduled
				voxelize_task = new VoxelizeTask { pos = pos, lod = new_lod };
				if (!ThreadPool.QueueUserWorkItem(VoxelizeTask.Func, voxelize_task))
					throw new System.SystemException("QueueUserWorkItem(VoxelizeTask) failed");

				mesh_stale();
			}
		}

		if (need_remesh) {
			remesh_task = new RemeshTask { lod = lod, voxels = voxels };
			if (!ThreadPool.QueueUserWorkItem(RemeshTask.Func, remesh_task))
				throw new System.SystemException("QueueUserWorkItem(RemeshTask) failed");
		}

		if (remesh_task != null && remesh_task.done) {
			apply_remesh();
			remesh_task = null;

			mesh_no_longer_stale();
		}
	}

	void mesh_stale () {
		mesh_renderer.material = terrain_controller.terrain_stale_mat;
	}
	void mesh_no_longer_stale () {
		mesh_renderer.material = terrain_controller.terrain_mat;
	}

	void apply_remesh () {
		mesh.Clear();

		mesh.vertices = remesh_task.verticies.ToArray();
		mesh.normals = remesh_task.normals.ToArray();
		mesh.colors = remesh_task.colors.ToArray();
		mesh.triangles = remesh_task.indicies.ToArray();
	}
	
	class VoxelizeTask {
		public static void Func (object obj) {
			((VoxelizeTask)obj).func();
		}

		// input
		public Vector3Int pos;
		public int lod;
		// output
		public Voxel[,,] voxels;

		public volatile bool done = false; // no idea if this is safe but fuck it, multithreading in c# is confusing

		public void func () {
			var generator = new TerrainGenerator();

			int voxel_size = calc_voxel_size(lod);
			int res = calc_resolution(lod);

			voxels = new Voxel[res,res,res];

			for (int z=0; z<res; ++z) {
				for (int y=0; y<res; ++y) {
					for (int x=0; x<res; ++x) {
						var voxel_index = new Vector3Int(x,y,z);
						var pos_in_chunk = voxel_index * voxel_size;
						var pos_in_world = pos_in_chunk + pos * SIZE;

						voxels[z,y,x] = new Voxel { density = generator.get_density(pos_in_world) };
					}
				}
			}

			done = true;
		}
	}

	class RemeshTask {
		public static void Func (object obj) {
			((RemeshTask)obj).func();
		}

		public int lod;
		public Voxel[,,] voxels;

		public List<Vector3>	verticies	= new List<Vector3>();
		public List<Vector3>	normals		= new List<Vector3>();
		public List<Color>		colors		= new List<Color>();
		public List<int>		indicies	= new List<int>();
		
		public volatile bool done = false;

		public void func () {
			int index_counter = 0;

			int voxel_size = calc_voxel_size(lod);
			int res = calc_resolution(lod);
		
			float dummy_density = 0.0f;

			Vector3Int[] corners = new Vector3Int[8] {
				new Vector3Int(0,1,0),
				new Vector3Int(1,1,0),
				new Vector3Int(1,0,0),
				new Vector3Int(0,0,0),
				new Vector3Int(0,1,1),
				new Vector3Int(1,1,1),
				new Vector3Int(1,0,1),
				new Vector3Int(0,0,1),
			};

			MarchingCubes.Triangle[] triangles = new MarchingCubes.Triangle[5] {
				new MarchingCubes.Triangle { p = new Vector3[3] },
				new MarchingCubes.Triangle { p = new Vector3[3] },
				new MarchingCubes.Triangle { p = new Vector3[3] },
				new MarchingCubes.Triangle { p = new Vector3[3] },
				new MarchingCubes.Triangle { p = new Vector3[3] },
			};

			var gc = new MarchingCubes.Gridcell { p = new Vector3[8], val = new float[8] };

			for (int z=0; z<res; ++z) {
				for (int y=0; y<res; ++y) {
					for (int x=0; x<res; ++x) {
						for (int i=0; i<8; ++i) {
							var corner = corners[i];
						
							var voxel_index = new Vector3Int(x,y,z) + corner;
							var pos_in_chunk = voxel_index * voxel_size;
						
							float density = dummy_density;
							if (voxel_index.x < res && voxel_index.y < res && voxel_index.z < res) {
								density = voxels[voxel_index.z, voxel_index.y, voxel_index.x].density;
							}

							gc.p[i] = pos_in_chunk;
							gc.val[i] = density;
						}

						int tri_count = MarchingCubes.Polygonise(gc, 0.0f, ref triangles);

						for (int tri_i=0; tri_i<tri_count; ++tri_i) {
							var a = triangles[tri_i].p[0];
							var b = triangles[tri_i].p[1];
							var c = triangles[tri_i].p[2];

							verticies.Add(a);
							verticies.Add(b);
							verticies.Add(c);

							var normal = Vector3.Normalize(Vector3.Cross(b - a, c - a));
						
							normals.Add(normal);
							normals.Add(normal);
							normals.Add(normal);

							colors.Add(Color.white);
							colors.Add(Color.white);
							colors.Add(Color.white);

							indicies.Add(index_counter++);
							indicies.Add(index_counter++);
							indicies.Add(index_counter++);
						}
					}
				}
			}

			done = true;
		}
	}
}

	
