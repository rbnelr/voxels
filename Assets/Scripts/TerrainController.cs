using System.Collections;
using System.Collections.Generic;
using System.Threading;
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
	
	void add_chunk (Vector3Int pos) {
		var chunk_obj = Instantiate(terrain_chunk_prefab, this.gameObject.transform) as GameObject;
		chunk_obj.transform.position = pos * TerrainChunk.SIZE;
		var chunk = new TerrainChunk();
		chunk.gameObject = chunk_obj;
		chunk.pos = pos;
		chunk.mesh = chunk_obj.GetComponent<MeshFilter>().mesh;
		chunk.mesh_renderer = chunk_obj.GetComponent<MeshRenderer>();

		chunk.terrain_controller = this;

		GameObject DebugChunkOutlines = null;
		foreach (Transform trs in chunk_obj.transform) {
			if (trs.gameObject.name == "DebugChunkOutlines")
				DebugChunkOutlines = trs.gameObject;
		}
		if (DebugChunkOutlines != null)
			DebugChunkOutlines.transform.localScale = new Vector3(TerrainChunk.SIZE, TerrainChunk.SIZE, TerrainChunk.SIZE);
		
		chunks.Add(pos, chunk);
	}
	void remove_chunk (TerrainChunk chunk) {
		Destroy(chunk.gameObject);
		chunks.Remove(chunk.pos);
	}

	void Start () {
		{
			int a, b, c, d;
			ThreadPool.GetMinThreads(out a, out b);
			ThreadPool.GetMaxThreads(out c, out d);
			ThreadPool.SetMinThreads(1, 1);
			ThreadPool.SetMaxThreads(1, 1);
		}
	}
	
	float dist (Vector3Int chunk_pos, Vector3 player_pos) {
		var chunk_p = chunk_pos * TerrainChunk.SIZE;

		var chunk_min = chunk_p;
		var chunk_max = chunk_p + new Vector3(TerrainChunk.SIZE, TerrainChunk.SIZE, TerrainChunk.SIZE);
		
		var nearest_pos_in_chunk = VectorExt.Clamp(player_pos, chunk_min, chunk_max);

		return Vector3.Distance(nearest_pos_in_chunk, player_pos);
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
		
		foreach (var chunk in chunks.Values) {
			float dist_to_player = dist(chunk.pos, player_pos);
			int lod = chunk_calc_lod(dist_to_player);

			chunk.update(lod);
		}
	}
}
