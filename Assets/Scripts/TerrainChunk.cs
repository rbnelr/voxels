using System.Collections;
using System.Collections.Generic;
using Unity.Collections;
using UnityEngine;

public class TerrainChunk {
	static object _inited_threadpool = _init_threadpool();
	static object _init_threadpool () {
		ThreadPool.Initialize();
		return null;
	}

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
	
	bool voxelize_queued = false;
	bool remesh_queued = false;
	
	void queue_voxelize (int new_lod) {
		ThreadPool.Push( new VoxelizeJob { chunk = this, pos = pos, lod = new_lod } );

		mesh_stale();

		voxelize_queued = true;
	}
	void queue_remesh () {
		ThreadPool.Push( new RemeshJob { chunk = this, lod = lod, voxels = voxels } );

		remesh_queued = true;
	}

	void apply_voxelize (VoxelizeJob job) {
		if (!terrain_controller.chunk_exists(job.chunk.pos)) {
			return; // chunk was deleted since the job was queued, drop the result
		}

		lod = job.lod;
		voxels = job.voxels;
				
		queue_remesh();

		voxelize_queued = false;
	}
	void apply_remesh (RemeshJob job) {
		if (!terrain_controller.chunk_exists(job.chunk.pos)) {
			return; // chunk was deleted since the job was queued, drop the result
		}

		mesh.Clear();

		mesh.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32;

		mesh.vertices = job.vertices;
		mesh.normals = job.normals;
		mesh.colors = job.colors;
		mesh.triangles = job.indices;

		mesh_no_longer_stale();
		
		remesh_queued = false;
	}

	public static void update_jobs () {
		foreach (var result in ThreadPool.PopAll()) {
			if (result is VoxelizeJob) {
				var job = result as VoxelizeJob;
				job.chunk.apply_voxelize(job);
			} else if (result is RemeshJob) {
				var job = result as RemeshJob;
				job.chunk.apply_remesh(job);
			}
		}
	}
	public void update (int new_lod) {
		if (new_lod != lod) {
			if (!voxelize_queued && !remesh_queued) {
				queue_voxelize(new_lod);
			}
		}
	}

	void mesh_stale () {
		mesh_renderer.material = terrain_controller.terrain_stale_mat;
	}
	void mesh_no_longer_stale () {
		mesh_renderer.material = terrain_controller.terrain_mat;
	}
	
	static Color test_color (Vector3 pos_in_world) {
		var c = Mathf.Clamp01(Mathf.Lerp(1,0, -pos_in_world.y / 300));
		return new Color(1,c,c,1);
	}

	class VoxelizeJob : ThreadPool.IJob {
		public TerrainChunk chunk;

		// input
		public Vector3Int pos;
		public int lod;
		// output
		public Voxel[,,] voxels;

		public void Execute () {
			var generator = new TerrainGenerator();

			int voxel_size = calc_voxel_size(lod);
			int res = calc_resolution(lod);

			voxels = new Voxel[res+1, res+1,  res+1];

			for (int z=0; z<res+1; ++z) {
				for (int y=0; y<res+1; ++y) {
					for (int x=0; x<res+1; ++x) {
						var voxel_index = new Vector3Int(x,y,z);
						var pos_in_chunk = voxel_index * voxel_size;
						var pos_in_world = pos_in_chunk + pos * SIZE;


						voxels[z,y,x] = new Voxel { density = generator.get_density(pos_in_world) };
					}
				}
			}
		}
	}

	class RemeshJob : ThreadPool.IJob {
		public TerrainChunk chunk;
		
		// input
		public int lod;
		public Voxel[,,] voxels;
		
		// output
		public Vector3[]	vertices;
		public Vector3[]	normals;
		public Color[]		colors;
		public int[]		indices;
		
		public void Execute () {
			var _vertices	= new List<Vector3>();
			var _normals	= new List<Vector3>();
			var _colors		= new List<Color>();
			var _indices	= new List<int>();

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
						
							//float density = dummy_density;
							//if (voxel_index.x < res && voxel_index.y < res && voxel_index.z < res) {
							//	density = voxels[voxel_index.z, voxel_index.y, voxel_index.x].density;
							//}

							var density = voxels[voxel_index.z, voxel_index.y, voxel_index.x].density;

							gc.p[i] = pos_in_chunk;
							gc.val[i] = density;
						}

						int tri_count = MarchingCubes.Polygonise(gc, 0.0f, ref triangles);

						for (int tri_i=0; tri_i<tri_count; ++tri_i) {
							var a = triangles[tri_i].p[0];
							var b = triangles[tri_i].p[1];
							var c = triangles[tri_i].p[2];

							_vertices.Add(a);
							_vertices.Add(b);
							_vertices.Add(c);

							var normal = Vector3.Normalize(Vector3.Cross(b - a, c - a));
						
							_normals.Add(normal);
							_normals.Add(normal);
							_normals.Add(normal);

							_colors.Add( test_color(a + chunk.pos * SIZE) );
							_colors.Add( test_color(b + chunk.pos * SIZE) );
							_colors.Add( test_color(c + chunk.pos * SIZE) );

							_indices.Add(index_counter++);
							_indices.Add(index_counter++);
							_indices.Add(index_counter++);
						}
					}
				}
			}
			
			vertices = _vertices.ToArray();
			normals = _normals.ToArray();
			colors = _colors.ToArray();
			indices = _indices.ToArray();
		}
	}
}

	
