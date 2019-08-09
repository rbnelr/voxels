using System.Collections.Generic;
using System.Linq;
using Unity.Collections;
using Unity.Jobs;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.Profiling;
using static Unity.Mathematics.math;

public class Chunks : MonoBehaviour {

	public static Chunks Instance;
	private void Start () {
		Instance = this;
	}

	public float LoadRadius = 200;
		
	public GameObject ChunkPrefab;
	public GameObject Player;

	public TerrainGenerator TerrainGenerator;
	public TerrainMesher TerrainMesher;

	float3 playerPos { get { return Player.transform.position; } }
		
	public bool AlwaysDrawChunks = false;

	public Dictionary<int3, Chunk> chunks = new Dictionary<int3, Chunk>();
		
	bool shouldBeLoaded (int3 index, out float dist) {
		var nearest = clamp(playerPos, (float3)index * Chunk.SIZE, (float3)(index + 1) * Chunk.SIZE);
		dist = distance(nearest, playerPos);
		return dist <= LoadRadius;
	}
	bool shouldBeLoaded (int3 index) {
		return shouldBeLoaded(index, out float dist);
	}

	List<Chunk> toRemove = new List<Chunk>();
	void Update () {
		var a = (int3)floor((playerPos - LoadRadius) / Chunk.SIZE);
		var b = (int3)ceil ((playerPos + LoadRadius) / Chunk.SIZE);
		for (int z=a.z; z<b.z; ++z) {
			for (int y=a.y; y<b.y; ++y) {
				for (int x=a.x; x<b.x; ++x) {
					var index = int3(x,y,z);
					if (shouldBeLoaded(index))
						LoadChunk(index);
				}
			}
		}

		//FinishChunkProcessing();

		Chunk chunkToProcess = null;

		toRemove.Clear();
		foreach (var c in chunks.Values) {
			if (shouldBeLoaded(c.Index, out c.LatestDist))
				UpdateChunk(c, ref chunkToProcess);
			else
				toRemove.Add(c);
		}

		StartChunkProcessing(chunkToProcess);
			
		foreach (var c in toRemove) {
			if (c != chunkInProcess) {
				c.Dispose();
				Destroy(c);
				chunks.Remove(c.Index);
			}
		}
	}

	void LateUpdate () {
		FinishChunkProcessing();
	}

	void LoadChunk (int3 index) {
		if (chunks.ContainsKey(index))
			return;

		var c = Instantiate(ChunkPrefab, (float3)index * Chunk.SIZE, Quaternion.identity, this.transform).GetComponent<Chunk>();
		c.Index = index;
		chunks.Add(index, c);
	}

	Chunk chunkInProcess = null;
	TerrainGenerator.Job genJob;
	TerrainMesher.Job meshJob;

	List<TerrainMesher.Job> remeshJobs = new List<TerrainMesher.Job>();

	void UpdateChunk (Chunk c, ref Chunk chunkToProcess) {
		if (!c.Voxels.IsCreated) {
			if (chunkToProcess == null || c.LatestDist < chunkToProcess.LatestDist)
				chunkToProcess = c;
		}
	}

	void StartChunkProcessing (Chunk c) {
		
		remeshJobs.Clear();
		foreach (var chunk in chunks.Values) {
			if (chunk.DeferRemesh) {
				chunk.DisposeMeshing();

				remeshJobs.Add( TerrainMesher.StartJob(chunk, genJob) );
			
				chunk.DeferRemesh = false;
			}
		}

		if (chunkInProcess == null && c != null) {
			chunkInProcess = c;

			c.DisposeMeshing();
			
			genJob = TerrainGenerator.StartJob(c);
			meshJob = TerrainMesher.StartJob(c, genJob);
		}
	}
	void FinishChunkProcessing () {
		foreach (var j in remeshJobs) {
			j.Complete();
		}

		if (chunkInProcess != null && (genJob?.IsCompleted ?? true) && (meshJob?.IsCompleted ?? true)) {
			genJob?.Complete();
			meshJob?.Complete();
			chunkInProcess.Done = true;
				
			chunkInProcess = null;
			genJob = null;
			meshJob = null;
		}
	}

	void OnDestroy () {
		genJob?.Complete();
		meshJob?.Complete();

		chunkInProcess = null;
		genJob = null;
		meshJob = null;

		foreach (var c in chunks.Values) {
			c.Dispose();
			Destroy(c);
		}
		chunks.Clear();
	}
		
	void drawChunks () {
		Gizmos.color = Color.red;
		Gizmos.DrawWireSphere(playerPos, LoadRadius);
		foreach (var c in chunks.Values)
			c.DrawGizmos();
	}

	void OnDrawGizmosSelected () {
		if (!AlwaysDrawChunks)
			drawChunks();
	}
	void OnDrawGizmos () {
		if (AlwaysDrawChunks)
			drawChunks();
	}
		
	void OnGUI () {
		GUI.Label(new Rect(0, 0, 500,30), "Chunks: "+ chunks.Count);
	}
}
