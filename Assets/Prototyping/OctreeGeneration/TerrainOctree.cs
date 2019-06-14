using UnityEngine;
using Unity.Mathematics;
using static Unity.Mathematics.math;
using Unity.Collections;

namespace OctreeGeneration {
	public class TerrainChunk {
		public float3 pos;
		public float size;

		public GameObject go;
		public Mesh mesh = new Mesh();

		public NativeArray<Voxel>? voxels = null;

		public Voxel GetVoxel (int3 pos, int ChunkVoxels) {
			int ArraySize = ChunkVoxels + 1;
			return voxels.Value[pos.z * ArraySize * ArraySize + pos.y * ArraySize + pos.x];
		}
		public static Voxel GetVoxel (ref NativeArray<Voxel> voxels, int3 pos, int ChunkVoxels) {
			int ArraySize = ChunkVoxels + 1;
			return voxels[pos.z * ArraySize * ArraySize + pos.y * ArraySize + pos.x];
		}

		public TerrainChunk (float3 pos, float size, GameObject TerrainChunkPrefab, Transform parent) {
			this.pos = pos;
			this.size = size;
				
			go = Object.Instantiate(TerrainChunkPrefab, pos, Quaternion.identity, parent);
		}

		public void Destroy () {
			Object.Destroy(go);
			Object.Destroy(mesh);
			voxels?.Dispose();
		}
	}

	public class TerrainOctree : MonoBehaviour {
		public class Node {
			// A Node contains
			//  a TerrainChunk as leaf node
			//  or its 8 children Nodes as Node[8] array
			// all 8 children must exist -> this way we do not have overlapping TerrainChunks
			public object Content = null;

			public float3 _pos;
			public float _size;

			public Node[] Children => Content as Node[];
			public TerrainChunk TerrainChunk => Content as TerrainChunk;
			
			public static readonly int3[] ChildOrder = new int3[8] { int3(0,0,0), int3(1,0,0),  int3(0,1,0), int3(1,1,0),   int3(0,0,1), int3(1,0,1),  int3(0,1,1), int3(1,1,1) };
		}

		public float VoxelSize = 1;
		public int ChunkVoxels = 32;
		[Range(0, 15)]
		public int MaxLod = 5;
		
		float prevVoxelSize;
		int prevChunkVoxels;
		int prevMaxLod;

		public float LodFuncStart = 0.5f;
		public float LodFuncEnd = 16f;
		public float LodFuncEndLod = 6f;
		
		public GameObject TerrainChunkPrefab;
		
		public GameObject player;
		
		public bool AlwaysDrawOctree = false;

		float3 playerPos { get { return player.transform.position; } }
		
		Node root;
		
		int calcLod (float3 chunkPos, float chunkSize) {
			var closest = chunkPos + clamp(playerPos - chunkPos, -chunkSize/2, chunkSize/2);
			var dist = length(playerPos - closest);
			
			float m = LodFuncStart;
			float n = LodFuncEnd;
			float l = LodFuncEndLod;
		
			float a = -l / ( Mathf.Log(m) - Mathf.Log(n) );
			float b = (l * Mathf.Log(m)) / ( Mathf.Log(m) - Mathf.Log(n) );
		
			float Lod0ChunkSize = VoxelSize * ChunkVoxels;
			var lod = max(Mathf.FloorToInt( a * Mathf.Log(dist / Lod0ChunkSize) + b ), 0);
			return lod;
		}

		void destroy (Node node) {
			node.TerrainChunk?.Destroy(); // destroy TerrainChunk if we have one
			
			if (node.Children != null) { // destroy children if we have them
				var children = node.Children;
				for (int i=0; i<8; ++i)
					destroy(children[i]);
			}
		}

		void updateTree (Node node, float3 pos, float size, int depth=0) {
			int desiredLod = calcLod(pos, size);
			int lod = MaxLod - depth;
			
			node._pos = pos;
			node._size = size;

			bool wantChildren = desiredLod < lod;
			
			if (wantChildren) {
				node.TerrainChunk?.Destroy(); // destroy TerrainChunk if we have one
				
				if (node.Children == null) { // Create Children if we dont already have them
					var children = new Node[8];
					for (int i=0; i<8; ++i)
						children[i] = new Node();

					node.Content = children;
				}
			} else {
				if (node.Children != null) { // destroy children if we have them
					var children = node.Children;
					for (int i=0; i<8; ++i)
						destroy(children[i]);
				}

				if (node.TerrainChunk == null)
					node.Content = new TerrainChunk(pos, size, TerrainChunkPrefab, this.gameObject.transform);
			}
			
			if (node.Children != null) {
				var children = node.Children;
				for (int i=0; i<8; ++i) {
					var child = Node.ChildOrder[i];

					var childSize = size / 2;
					var childPos = pos + lerp(-childSize/2, +childSize/2, child);

					updateTree(children[i], childPos, childSize, depth + 1);
				}
			}
		}
		
		void Update () { // Updates the Octree by creating and deleting TerrainChunks of different sizes (LOD)
			if (MaxLod != prevMaxLod || VoxelSize != prevVoxelSize || ChunkVoxels != prevChunkVoxels)
				root = null; // rebuild tree
			
			prevMaxLod = MaxLod;
			prevVoxelSize = VoxelSize;
			prevChunkVoxels = ChunkVoxels;
			
			float Lod0ChunkSize = VoxelSize * ChunkVoxels;
			float RootChunkSize = Lod0ChunkSize * Mathf.Pow(2f, MaxLod);
			
			if (root == null)
				root = new Node();

			updateTree(root, 0, RootChunkSize);
		}

		
		static readonly Color[] drawColors = new Color[] {
			Color.blue, Color.cyan, Color.green, Color.red, Color.yellow, Color.magenta, Color.gray,
		};
		int _countChunks = 0;
		int _countNodes = 0;
		void drawChunk (Node n, int depth=0) {
			_countNodes++;

			Gizmos.color = drawColors[clamp((MaxLod - depth) % drawColors.Length, 0, drawColors.Length-1)];
			Gizmos.DrawWireCube(n._pos, (float3)n._size);

			if (n.TerrainChunk != null) {
				_countChunks++;
			}
			
			if (n.Children != null) {
				for (int i=0; i<8; ++i) {
					drawChunk(n.Children[i], depth+1);
				}
			}
		}
		void drawOctree () {
			_countChunks = 0;
			_countNodes = 0;
			if (root != null)
				drawChunk(root);
		}

		void OnDrawGizmosSelected () {
			if (!AlwaysDrawOctree)
				drawOctree();
		}
		void OnDrawGizmos () {
			if (AlwaysDrawOctree)
				drawOctree();
		}
		
		void OnGUI () {
			float Lod0ChunkSize = VoxelSize * ChunkVoxels;
			float RootChunkSize = Lod0ChunkSize * Mathf.Pow(2f, MaxLod);
			
			GUI.Label(new Rect(0,  0, 500,30), "Terrain Chunks: "+ _countChunks);
			GUI.Label(new Rect(0, 30, 500,30), "Terrain Octree Nodes: "+ _countNodes);
			GUI.Label(new Rect(0, 60, 500,30), "Root Chunk Size: "+ RootChunkSize);
		}
	}
}
