using System.Collections.Generic;
using UnityEngine;

public class TerrainController : MonoBehaviour {
	
	float chunk_gen_radius = 32 * 12;
	float chunk_lod_falloff = 32;

	int chunk_calc_lod (float dist) {
		return Mathf.FloorToInt(Mathf.Log(dist / chunk_lod_falloff + 1, 2));
		//return Mathf.FloorToInt(dist / chunk_lod_falloff);
	}

	Dictionary<Vector3Int, TerrainChunk> chunks = new Dictionary<Vector3Int, TerrainChunk>();
    
	public GameObject terrain_chunk_prefab;
	public GameObject player;

	public Material terrain_mat;
	public Material terrain_stale_mat;
	
	public bool chunk_exists (Vector3Int pos) {
		return chunks.ContainsKey(pos);
	}

	void add_chunk (Vector3Int pos) {
		var chunk_obj = Instantiate(terrain_chunk_prefab, this.gameObject.transform) as GameObject;
		chunk_obj.transform.position = pos * TerrainChunk.SIZE;
		var chunk = new TerrainChunk();
		chunk.gameObject = chunk_obj;
		chunk.pos = pos;
		chunk.mesh = chunk_obj.GetComponent<MeshFilter>().mesh;
		chunk.mesh_renderer = chunk_obj.GetComponent<MeshRenderer>();

		chunk.terrain_controller = this;

		//GameObject DebugChunkOutlines = null;
		//foreach (Transform trs in chunk_obj.transform) {
		//	if (trs.gameObject.name == "DebugChunkOutlines")
		//		DebugChunkOutlines = trs.gameObject;
		//}
		//if (DebugChunkOutlines != null) {
		//	DebugChunkOutlines.transform.localScale = new Vector3(TerrainChunk.SIZE, TerrainChunk.SIZE, TerrainChunk.SIZE);
		//	DebugChunkOutlines.SetActive(false);
		//}
		chunks.Add(pos, chunk);
	}
	void remove_chunk (TerrainChunk chunk) {
		Destroy(chunk.gameObject);
		chunks.Remove(chunk.pos);
	}
	
	void Start () {
		
	}
	
	float dist (Vector3Int chunk_pos, Vector3 player_pos) {
		var chunk_p = chunk_pos * TerrainChunk.SIZE;

		var chunk_min = chunk_p;
		var chunk_max = chunk_p + new Vector3(TerrainChunk.SIZE, TerrainChunk.SIZE, TerrainChunk.SIZE);
		
		var nearest_pos_in_chunk = VectorExt.Clamp(player_pos, chunk_min, chunk_max);

		return Vector3.Distance(nearest_pos_in_chunk, player_pos);
	}

	//struct CubeEdge { public Vector3 a, b; }
	//static readonly CubeEdge[] cube_edges = new CubeEdge[12] {
	//	new CubeEdge { a = new Vector3(0,0,0), b = new Vector3(1,0,0) },
	//	new CubeEdge { a = new Vector3(1,0,0), b = new Vector3(1,1,0) },
	//	new CubeEdge { a = new Vector3(1,1,0), b = new Vector3(0,1,0) },
	//	new CubeEdge { a = new Vector3(0,1,0), b = new Vector3(0,0,0) },
	//	
	//	new CubeEdge { a = new Vector3(0,0,0), b = new Vector3(0,0,1) },
	//	new CubeEdge { a = new Vector3(1,0,0), b = new Vector3(1,0,1) },
	//	new CubeEdge { a = new Vector3(1,1,0), b = new Vector3(1,1,1) },
	//	new CubeEdge { a = new Vector3(0,1,0), b = new Vector3(0,1,1) },
	//
	//	new CubeEdge { a = new Vector3(0,0,1), b = new Vector3(1,0,1) },
	//	new CubeEdge { a = new Vector3(1,0,1), b = new Vector3(1,1,1) },
	//	new CubeEdge { a = new Vector3(1,1,1), b = new Vector3(0,1,1) },
	//	new CubeEdge { a = new Vector3(0,1,1), b = new Vector3(0,0,1) },
	//};
	struct ChunkDist {
		public TerrainChunk c;
		public float dist;
	}

	void Update () {
		var player_pos = player.transform.position;

		int minx = Mathf.FloorToInt((player_pos.x - chunk_gen_radius) / TerrainChunk.SIZE);
		int miny = Mathf.FloorToInt((player_pos.y - chunk_gen_radius) / TerrainChunk.SIZE);
		int minz = Mathf.FloorToInt((player_pos.z - chunk_gen_radius) / TerrainChunk.SIZE);
		int maxx = Mathf.CeilToInt ((player_pos.x + chunk_gen_radius) / TerrainChunk.SIZE);
		int maxy = Mathf.CeilToInt ((player_pos.y + chunk_gen_radius) / TerrainChunk.SIZE);
		int maxz = Mathf.CeilToInt ((player_pos.z + chunk_gen_radius) / TerrainChunk.SIZE);
		
		for (int y=miny; y<maxy; ++y) {
			for (int z=minz; z<maxz; ++z) {
				for (int x=minx; x<maxx; ++x) {
					var chunk_pos = new Vector3Int(x,y,z);

					float dist_to_player = dist(chunk_pos, player_pos);
					
					if (!chunks.ContainsKey(chunk_pos) && dist_to_player <= chunk_gen_radius) {
						add_chunk(chunk_pos);
					}
				}
			}
		}
		
		var to_delete = new List<TerrainChunk>();
		foreach (var c in chunks.Values) {
			if (dist(c.pos, player_pos) > chunk_gen_radius) {
				to_delete.Add(c);
			}
		}

		foreach(var c in to_delete)
			remove_chunk(c);
		
		TerrainChunk.update_jobs();
		
		//
		#if true
		var chunks_sorted = new List<ChunkDist>();
		foreach (var chunk in chunks.Values) {
			float dist_to_player = dist(chunk.pos, player_pos);
			
			chunks_sorted.Add(new ChunkDist { c = chunk, dist = dist_to_player });
		}

		chunks_sorted.Sort((l,r) => l.dist.CompareTo(r.dist));

		foreach (var cd in chunks_sorted) {
			var chunk = cd.c;
			float dist_to_player = cd.dist;
		#else
		foreach (var chunk in chunks.Values) {
			float dist_to_player = dist(chunk.pos, player_pos);
			#endif

			int lod = chunk_calc_lod(dist_to_player);

			chunk.update(lod);
		}
	}
}
