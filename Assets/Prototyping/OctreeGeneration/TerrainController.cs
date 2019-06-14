using UnityEngine;
using Unity.Mathematics;
using static Unity.Mathematics.math;

namespace OctreeGeneration {
	
	
	public class TerrainController : MonoBehaviour {
		
		public GameObject TerrainChunkPrefab;
		
		public TerrainGenerator generator;
		public GameObject player;
		float3 playerPos { get { return player.transform.position; } }

		public GameObject test;
		float3 testPos { get { return test.transform.position; } }
		public int3 testDir;

		public float VoxelSize = 1f;
		public int ChunkVoxels = 32;
		
		[Range(0, 15)]
		public int MaxLod = 7;

		TerrainOctree octree = new TerrainOctree();

		//TerrainChunk root;
		//
		//TerrainChunk newChunk (float size, float3 pos, int depth, TerrainChunk parent=null) {
		//	var c = new TerrainChunk {
		//		children = new TerrainChunk[8],
		//		parent = parent,
		//		depth = depth,
		//
		//		pos = pos,
		//		size = size,
		//		
		//		go = Instantiate(TerrainChunkPrefab, pos, Quaternion.identity, this.gameObject.transform),
		//		mesh = new Mesh(),
		//	};
		//
		//	c.mesh.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32;
		//	c.go.GetComponent<MeshFilter>().mesh = c.mesh;
		//
		//	c.voxelizer.StartJob(c, ChunkVoxels, generator);
		//
		//	return c;
		//}
		//void deleteChunk (TerrainChunk c) { // deletes all children recursivly
		//	Destroy(c.go);
		//	Destroy(c.mesh); // is this needed?
		//	for (int i=0; i<8; ++i)
		//		if (c.children[i] != null)
		//			deleteChunk(c.children[i]);
		//
		//	if (c.parent != null)
		//		c.parent.needsRemesh = true;
		//
		//	c.Dispose();
		//}
		//void updateChunkLod (TerrainChunk chunk, int depth=0) { // Creates and Deletes Chunks when needed according to Lod function
		//	chunk.voxelizer.Update(chunk);
		//	chunk.mesher.Update(chunk);
		//
		//	if (depth == MaxLod) return;
		//
		//	for (int i=0; i<8; ++i) {
		//		var child = chunk.children[i];
		//
		//		var size = chunk.size / 2;
		//		var pos = chunk.pos + (float3)TerrainChunk.childrenPos[i] * size / 2;
		//		
		//		if (child != null) {
		//			Debug.Assert(all(child.pos == pos));
		//			Debug.Assert(child.size == size);
		//		}
		//
		//		var closest = pos + clamp(playerPos - pos, -size/2, size/2);
		//		var dist = length(playerPos - closest);
		//		
		//		int ChunkLod = MaxLod - depth;
		//		var desiredLod = Mathf.FloorToInt(calcLod(dist));
		//
		//		bool needChild = desiredLod < ChunkLod;
		//
		//		if (needChild && child == null) {
		//			child = newChunk(size, pos, depth+1, chunk);
		//		} else if (!needChild && child != null) {
		//			deleteChunk(child);
		//			child = null;
		//		}
		//
		//		if (child != null) {
		//			updateChunkLod(child, depth +1);
		//		}
		//		
		//		chunk.children[i] = child;
		//	}
		//}
		//
		int calcLod (float3 chunkPos, float chunkSize) {
			var closest = chunkPos + clamp(playerPos - chunkPos, -chunkSize/2, chunkSize/2);
			var dist = length(playerPos - closest);
			
			float m = 0.5f;
			float n = 16f;
			float l = 6f;
		
			float a = -l / ( Mathf.Log(m) - Mathf.Log(n) );
			float b = (l * Mathf.Log(m)) / ( Mathf.Log(m) - Mathf.Log(n) );
		
			float Lod0ChunkSize = VoxelSize * ChunkVoxels;
			var lod = Mathf.FloorToInt( a * Mathf.Log(dist / Lod0ChunkSize) + b );
			return lod;
		}
		
		//void updateChunkMesh (TerrainChunk chunk) {
		//	for (int i=0; i<8; ++i) {
		//		if (chunk.children[i] != null)
		//			updateChunkMesh(chunk.children[i]);
		//	}
		//
		//	if (chunk.needsRemesh && chunk.mesher.jobHandle == null) {
		//		chunk.mesher.StartJob(chunk, ChunkVoxels);
		//	}
		//}
		//
		//void disposeChunk (TerrainChunk chunk) {
		//	for (int i=0; i<8; ++i) {
		//		if (chunk.children[i] != null)
		//			disposeChunk(chunk.children[i]);
		//	}
		//
		//	chunk.Dispose();
		//}
		//
		//float _prevVoxelSize;
		//int _prevChunkVoxels;
		//int _prevMaxLod;

		void OnDestroy () {
			//if (root != null)
			//	disposeChunk(root);
		}
		void Update () {
			//if (	VoxelSize != _prevVoxelSize || 
			//		ChunkVoxels != _prevChunkVoxels ||
			//		MaxLod != _prevMaxLod ) {
			//
			//	if (root != null)
			//		deleteChunk(root);
			//	root = null;
			//	_prevVoxelSize = VoxelSize;
			//	_prevChunkVoxels = ChunkVoxels;
			//	_prevMaxLod = MaxLod;
			//}
			//
			//float Lod0ChunkSize = VoxelSize * ChunkVoxels;
			//float RootChunkSize = Lod0ChunkSize * Mathf.Pow(2f, MaxLod);
			//
			//if (root == null) {
			//	root = newChunk(RootChunkSize, new float3(0,0,0), 0);
			//}
			//
			//updateChunkLod(root);
			//
			//updateChunkMesh(root);

			//octree.Update(calcLod);
		}

		static readonly Color[] drawColors = new Color[] {
			Color.blue,
			Color.cyan,
			Color.green,
			Color.red,
			Color.yellow,
			Color.magenta,
			Color.gray,
			Color.white,
			Color.black,
			Color.blue,
			Color.cyan,
			Color.green,
			Color.red,
			Color.yellow,
			Color.magenta,
			Color.gray,
			Color.white,
			Color.black
		};
		int _countChunks = 0;
		void drawChunk (TerrainChunk n, int depth=0) {
			//Gizmos.color = drawColors[Mathf.Clamp(MaxLod - depth + 1, 0, drawColors.Length-1)];
			//Gizmos.DrawWireCube(n.pos, new float3(n.size, n.size, n.size));
			//_countChunks++;
			//
			//for (int i=0; i<8; ++i) {
			//	if (n.children[i] != null) {
			//		drawChunk(n.children[i], depth+1);
			//	}
			//}
		}
		void OnDrawGizmosSelected () {
			//_countChunks = 0;
			//if (root != null)
			//	drawChunk(root);
		}

		void OnDrawGizmos () {
			//{ // debug test octree lookup
			//	var n = LookupOctree(testPos);
			//	if (n != null) {
			//		Gizmos.color = Color.red;
			//		Gizmos.DrawWireCube(n.pos, new float3(n.size*0.9f, n.size*0.9f, n.size*0.9f));
			//
			//		n = GetNeighbourTree(n, testDir.x, testDir.y, testDir.z);
			//		if (n != null) {
			//			Gizmos.color = Color.blue;
			//			Gizmos.DrawWireCube(n.pos, new float3(n.size*0.9f, n.size*0.9f, n.size*0.9f));
			//		}
			//	}
			//}
			
			//_countChunks = 0;
			//if (root != null)
			//	drawChunk(root);
		}

		void OnGUI () {
			float Lod0ChunkSize = VoxelSize * ChunkVoxels;
			float RootChunkSize = Lod0ChunkSize * Mathf.Pow(2f, MaxLod);
			
			GUI.Label(new Rect(0,  0, 500,30), "Terrain Chunks: "+ _countChunks);
			GUI.Label(new Rect(0, 30, 500,30), "Root Chunk Size: "+ RootChunkSize);
		}
	}
}
