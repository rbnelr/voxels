using UnityEngine;
using Unity.Mathematics;
using static Unity.Mathematics.math;
using Unity.Collections;
using System.Collections.Generic;
using UnityEngine.Profiling;
using System.Collections.ObjectModel;

namespace OctreeGeneration {
	public class TerrainNodeCache : KeyedCollection<OctreeCoord, TerrainNode> {
		public delegate void NodeDeleter (TerrainNode n);
		
		public void MakeRoomForOne (int CacheSize, NodeDeleter onDelete) {
			if (CacheSize == 0)
				return;
			while (Count > 0 && Count >= CacheSize) { // uncache until we have Room for one 
				onDelete(this[0]);
				RemoveAt(0); // Remove oldest chached Voxel
			}
		}

		protected override OctreeCoord GetKeyForItem (TerrainNode item) {
			return item.coord;
		}
	}

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
		
		public GameObject TerrainNodePrefab;
		
		public GameObject player;
		
		public bool AlwaysDrawOctree = false;

		float3 playerPos { get { return player.transform.position; } }
		
		// TerrainNodes are can be in a state of being voxelized, being meshed while they are either in the tree or in the cache
		// The cache just represents nodes that are either supposed to be pregenerated (maybe when we notice that we have no workload currently) or were generated are now out of tree

		public int CacheSize = 100;

		public TerrainNode root = null; // Tree of visible TerrainNodes
		public TerrainNodeCache cache = new TerrainNodeCache(); // cache of TerrainNodes that are out of tree
		public List<TerrainNode> sortedNodes = new List<TerrainNode>(); // all nodes which get sorted 
		
		TerrainVoxelizer terrainVoxelizer;
		TerrainMesher terrainMesher;
		void Awake () {
			terrainVoxelizer = GetComponent<TerrainVoxelizer>();
			terrainMesher = GetComponent<TerrainMesher>();
		}
		
		TerrainNode createNode (OctreeCoord coord, GameObject prefab, Transform parent) {
			float3 pos = coord.ToWorldCube(VoxelSize, ChunkVoxels, out float size);

			var n = new TerrainNode(coord, pos, TerrainNodePrefab, parent);
			sortedNodes.Add(n);
			return n;
		}
		void destroyNode (TerrainNode n) {
			n.Destroy();
			sortedNodes.Remove(n); // SLOW: O(N)
		}

		TerrainNode createNodeOrGetCached (OctreeCoord coord, GameObject prefab, Transform parent) {
			TerrainNode node;
			if (cache.Contains(coord)) {
				node = cache[coord];
				cache.Remove(coord); // Remove from cache since we now use the node
				node.InTree = true;
			} else {
				node = createNode(coord, prefab, parent);
			}
			node.InTree = true;
			return node;
		}
		void removeFromTreeRecursive (TerrainNode n) {
			n.Parent = null;
			if (n.Children != null)
				for (int i=0; i<8; ++i)
					removeFromTreeRecursive(n.Children[i]);
			n.Children = null;

			cache.MakeRoomForOne(CacheSize, destroyNode);
			cache.Add(n);

			n.InTree = false;
		}

		void clearAllNodes () {
			if (root != null)
				removeFromTreeRecursive(root);
			foreach (var n in cache) {
				destroyNode(n);
			}
			cache.Clear();
			root = null;
		}

		public float CalcDistToPlayer (float3 chunkPos, float chunkSize) {
			var closest = chunkPos + clamp(playerPos - chunkPos, -chunkSize/2, chunkSize/2);
			return length(playerPos - closest);
		}
		int calcLod (float dist) {
			float m = LodFuncStart;
			float n = LodFuncEnd;
			float l = LodFuncEndLod;
		
			float a = -l / ( Mathf.Log(m) - Mathf.Log(n) );
			float b = (l * Mathf.Log(m)) / ( Mathf.Log(m) - Mathf.Log(n) );
		
			float Lod0ChunkSize = VoxelSize * ChunkVoxels;
			var lod = max(Mathf.FloorToInt( a * Mathf.Log(dist / Lod0ChunkSize) + b ), 0);
			return lod;
		}
		
		void updateTree (TerrainNode node) {
			float size;
			float3 pos = node.coord.ToWorldCube(VoxelSize, ChunkVoxels, out size);

			float dist = CalcDistToPlayer(pos, size);
			int desiredLod = calcLod(dist);
			
			node.latestDistToPlayer = dist;

			bool wantChildren = desiredLod < node.coord.lod;
			
			if (wantChildren) {
				if (node.Children == null) { // Create Children if we dont already have them
					node.Children = new TerrainNode[8];
					
					for (int i=0; i<8; ++i) {
						node.Children[i] = createNodeOrGetCached(
							new OctreeCoord(node.coord.lod - 1, node.coord.index * 2 + TerrainNode.ChildOrder[i] * 2),
							TerrainNodePrefab, this.transform); // Add the new or cached node to the tree by setting it as our child
					}
				}
			} else {
				if (node.Children != null) { // destroy Children if we have them
					var children = node.Children;
					for (int i=0; i<8; ++i)
						removeFromTreeRecursive(children[i]);
					node.Children = null;
				}
			}
			
			if (node.Children != null) {
				for (int i=0; i<8; ++i)
					updateTree(node.Children[i]);
			}
		}
		
		void resortNodeList () {
			Profiler.BeginSample("resortNodeList()");
			sortedNodes.Sort( (l, r) => {
				int order =				-l.InTree				.CompareTo(r.InTree);
				if (order == 0) order =	 l.coord.lod			.CompareTo(r.coord.lod);
				if (order == 0) order =	 l.latestDistToPlayer	.CompareTo(r.latestDistToPlayer);
				return order;
			});
			Profiler.EndSample();
		}

		void Update () { // Updates the Octree by creating and deleting TerrainChunks of different sizes (LOD)
			if (MaxLod != prevMaxLod || VoxelSize != prevVoxelSize || ChunkVoxels != prevChunkVoxels)
				clearAllNodes(); // rebuild tree
			
			prevMaxLod = MaxLod;
			prevVoxelSize = VoxelSize;
			prevChunkVoxels = ChunkVoxels;
			
			if (root == null) {
				root = createNodeOrGetCached(new OctreeCoord(MaxLod, -1), TerrainNodePrefab, this.transform);
				root.InTree = true;
			}

			updateTree(root);

			resortNodeList();

			terrainMesher.ManualUpdateStartJobs(sortedNodes, this);
			terrainVoxelizer.ManualUpdate(sortedNodes, this);
		}
		void LateUpdate () {
			terrainMesher.ManualUpdateFinishJobs(sortedNodes, this);

			TerrainNode.UpdateTreeVisibility(root);
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
			
			//GUI.Label(new Rect(0,  0, 500,30), "Terrain Chunks: "+ _countChunks);
			GUI.Label(new Rect(0, 30, 500,30), "Terrain Nodes: "+ _countNodes);
			GUI.Label(new Rect(0, 60, 500,30), "Root Chunk Size: "+ RootChunkSize);
		}
	}
}
