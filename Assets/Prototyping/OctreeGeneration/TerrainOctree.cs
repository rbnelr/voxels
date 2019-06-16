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

		public bool needsRemesh = true;

		public TerrainChunk (float3 pos, float size, GameObject TerrainChunkPrefab, Transform parent) {
			this.pos = pos;
			this.size = size;
				
			go = Object.Instantiate(TerrainChunkPrefab, pos, Quaternion.identity, parent);
			mesh = new Mesh();
			mesh.name = "TerrainChunk Mesh";
			mesh.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32;
			go.GetComponent<MeshFilter>().mesh = mesh;
		}

		public void Destroy () {
			Object.Destroy(go);
			Object.Destroy(mesh);
			mesh = null;
		}
	}

	public class TerrainNode {
		// A TerrainNode contains
		//  a TerrainChunk as leaf node
		//  or its 8 children Nodes as TerrainNode[8] array
		// all 8 children must exist -> this way we do not have overlapping TerrainChunks
		public object Content = null;
			
		public OctreeCoord coord;

		public TerrainNode[] Children => Content as TerrainNode[];
		public TerrainChunk TerrainChunk => Content as TerrainChunk;
			
		public static readonly int3[] ChildOrder = new int3[8] { int3(0,0,0), int3(1,0,0),  int3(0,1,0), int3(1,1,0),   int3(0,0,1), int3(1,0,1),  int3(0,1,1), int3(1,1,1) };
	}

	public struct OctreeCoord { // Unique coordinate for each octree node position in the world
		// 0 is cube of size VoxelSize * ChunkVoxels
		// 1 is 2x the size aso.
		public readonly int		lod;
		// index of the cubes of this lod level
		// (0,0,0) is the one with low corner on the orign (the one that spans (0,0,0) to (size,size,size) where size = VoxelSize * ChunkVoxels * 2 ^ lod)
		// indecies are scaled by 2 to allow the root node to shift by half of it's size and still have a valid OctreeCoord
		public readonly int3	index;

		public OctreeCoord (int lod, int3 index) {
			this.lod = lod;
			this.index = index;
		}

		public static OctreeCoord FromWorldPos (float3 posWorld, int lod, float VoxelSize, int ChunkVoxels) {
			return new OctreeCoord(lod, (int3)floor(posWorld / ((ChunkVoxels << lod) * VoxelSize * 0.5f)));
		}
		public float3 ToWorldCube (float VoxelSize, int ChunkVoxels, out float size) { // center, size from OctreeCoord
			size = (ChunkVoxels << lod) * VoxelSize;
			return ((float3)(index + 1) / 2) * size;
		}
			
		public override string ToString () {
			return string.Format("({0}, ({1}, {2}, {3}))", lod, index.x, index.y, index.z);
		}
			
		public override bool Equals (object obj) {
			if (obj == null || GetType() != obj.GetType()) {
				return false;
			}

			var r = (OctreeCoord)obj;
			return lod == r.lod && all(index == r.index);
		}
			
		public override int GetHashCode () {
			// https://stackoverflow.com/questions/1646807/quick-and-simple-hash-code-combinations
			int hash = 1009;
			hash = (hash * 9176) + lod;
			hash = (hash * 9176) + index.x;
			hash = (hash * 9176) + index.y;
			hash = (hash * 9176) + index.z;
			return hash;
		}
	};

	public class TerrainOctree : MonoBehaviour {
		
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
		
		public TerrainNode root;
		
		public float CalcDistToPlayer (float3 chunkPos, float chunkSize) {
			var closest = chunkPos + clamp(playerPos - chunkPos, -chunkSize/2, chunkSize/2);
			return length(playerPos - closest);
		}
		int calcLod (float3 chunkPos, float chunkSize) {
			var dist = CalcDistToPlayer(chunkPos, chunkSize);

			float m = LodFuncStart;
			float n = LodFuncEnd;
			float l = LodFuncEndLod;
		
			float a = -l / ( Mathf.Log(m) - Mathf.Log(n) );
			float b = (l * Mathf.Log(m)) / ( Mathf.Log(m) - Mathf.Log(n) );
		
			float Lod0ChunkSize = VoxelSize * ChunkVoxels;
			var lod = max(Mathf.FloorToInt( a * Mathf.Log(dist / Lod0ChunkSize) + b ), 0);
			return lod;
		}

		void destroy (TerrainNode node) {
			node.TerrainChunk?.Destroy(); // destroy TerrainChunk if we have one
			
			if (node.Children != null) { // destroy children if we have them
				var children = node.Children;
				for (int i=0; i<8; ++i)
					destroy(children[i]);
			}
		}

		void updateTree (TerrainNode node) {
			float size;
			float3 pos = node.coord.ToWorldCube(VoxelSize, ChunkVoxels, out size);

			int desiredLod = calcLod(pos, size);
			
			bool wantChildren = desiredLod < node.coord.lod;
			
			if (wantChildren) {
				node.TerrainChunk?.Destroy(); // destroy TerrainChunk if we have one
				
				if (node.Children == null) { // Create Children if we dont already have them
					var children = new TerrainNode[8];
					for (int i=0; i<8; ++i) {
						children[i] = new TerrainNode {
							coord = new OctreeCoord(node.coord.lod - 1, node.coord.index * 2 + TerrainNode.ChildOrder[i] * 2)
						};
					}
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
				for (int i=0; i<8; ++i)
					updateTree(node.Children[i]);
			}
		}
		
		void Update () { // Updates the Octree by creating and deleting TerrainChunks of different sizes (LOD)
			if (MaxLod != prevMaxLod || VoxelSize != prevVoxelSize || ChunkVoxels != prevChunkVoxels) {
				if (root != null)
					destroy(root);
				root = null; // rebuild tree
			}
			
			prevMaxLod = MaxLod;
			prevVoxelSize = VoxelSize;
			prevChunkVoxels = ChunkVoxels;
			
			if (root == null) {
				root = new TerrainNode {
					coord = new OctreeCoord(MaxLod, -1)
				};
			}

			updateTree(root);
		}

		
		static readonly Color[] drawColors = new Color[] {
			Color.blue, Color.cyan, Color.green, Color.red, Color.yellow, Color.magenta, Color.gray,
		};
		int _countChunks = 0;
		int _countNodes = 0;
		void drawChunk (TerrainNode n) {
			_countNodes++;
			
			float size;
			float3 pos = n.coord.ToWorldCube(VoxelSize, ChunkVoxels, out size);

			Gizmos.color = drawColors[clamp(n.coord.lod % drawColors.Length, 0, drawColors.Length-1)];
			Gizmos.DrawWireCube(pos, (float3)size);

			if (n.TerrainChunk != null) {
				_countChunks++;
			}
			
			if (n.Children != null) {
				for (int i=0; i<8; ++i) {
					drawChunk(n.Children[i]);
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
