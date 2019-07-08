using UnityEngine;
using Unity.Mathematics;
using static Unity.Mathematics.math;
using Unity.Collections;
using System.Collections.Generic;
using UnityEngine.Profiling;
using System.Collections.ObjectModel;

namespace OctreeGeneration {

	[RequireComponent(typeof(TerrainVoxelizer), typeof(TerrainMesher))]
	public class TerrainOctree : MonoBehaviour {
		
		public float VoxelSize = 1;
		public int NodeVoxels = 32;
		[Range(0, 15)]
		public int MaxLod = 5;

		public float LodFuncStart = 0.5f;
		public float LodFuncEnd = 16f;
		public float LodFuncEndLod = 6f;
		
		float prevVoxelSize;
		int prevNodeVoxels;
		int prevMaxLod;
		
		public GameObject TerrainNodePrefab;
		public GameObject player;
		
		public bool AlwaysDrawOctree = false;

		float3 playerPos { get { return player.transform.position; } }
		
		public TerrainNode root = null; // Tree of visible TerrainNodes
		public List<TerrainNode> sortedNodes = new List<TerrainNode>();
		
		TerrainNode createNode (int lod, float3 pos, float size, TerrainNode parent=null) {
			var n = new TerrainNode(lod, pos, size, TerrainNodePrefab, this.transform, parent);
			sortedNodes.Add(n);
			
			if (n.Parent != null)
				n.Parent.needsRemesh = true;

			return n;
		}
		void destroyNode (TerrainNode n) {
			for (int i=0; i<8; ++i)
				if (n.Children[i] != null)
					destroyNode(n.Children[i]);

			n.Destroy();
			sortedNodes.Remove(n); // SLOW: O(N), can't use RemoveAtSwapBack because that need the index, which do not have, and to have it would require keeping track of it during sorting, which the standart sorting algo does not allow
			
			if (n.mesh != null && n.Parent != null)
				n.Parent.needsRemesh = true;
		}

		public float CalcDistToPlayer (float3 nodePos, float nodeSize) {
			float3 posRel = playerPos - nodePos;
			var closest = clamp(posRel, 0, nodeSize);
			return length(posRel - closest);
		}
		int calcLod (float dist) {
			float m = LodFuncStart;
			float n = LodFuncEnd;
			float l = LodFuncEndLod;
		
			float a = -l / ( Mathf.Log(m) - Mathf.Log(n) );
			float b = (l * Mathf.Log(m)) / ( Mathf.Log(m) - Mathf.Log(n) );
		
			float Lod0NodeSize = VoxelSize * NodeVoxels;
			var lod = max(Mathf.FloorToInt( a * Mathf.Log(dist / Lod0NodeSize) + b ), 0);
			return lod;
		}
		
		void updateTree (TerrainNode node) {
			Debug.Assert(node != null && !node.IsDestroyed);
			
			for (int i=0; i<8; ++i) {
				int3 childIndx = TerrainNode.ChildOrder[i];
				var child = node.Children[i];

				int childLod = node.lod - 1;
				float childSize = node.size / 2;
				float3 childPos = node.pos + (float3)childIndx * childSize;
				
				float dist = CalcDistToPlayer(childPos, childSize);
				int desiredLod = calcLod(dist);
				
				bool wantChild = desiredLod <= childLod;

				if (wantChild && child == null) {
					child = createNode(childLod, childPos, childSize, node);
				}
				if (!wantChild && child != null) {
					destroyNode(child);
					child = null;
				}

				if (child != null) {
					node.latestDistToPlayer = dist;
					
					updateTree(child);
				}

				node.Children[i] = child;
			}
		}
		
		void resortNodeList () {
			Profiler.BeginSample("resortNodeList()");
			sortedNodes.Sort( (l, r) => {
				//int order =				-l.InTree				.CompareTo(r.InTree);
				int             order =	 (l.lod			        .CompareTo(r.lod));
				if (order == 0) order =	 l.latestDistToPlayer	.CompareTo(r.latestDistToPlayer);
				return order;
			});
			Profiler.EndSample();
		}

		TerrainVoxelizer voxelizer;
		TerrainMesher mesher;

		void Awake () {
			voxelizer = GetComponent<TerrainVoxelizer>();
			mesher = GetComponent<TerrainMesher>();
		}

		void Update () { // Updates the Octree by creating and deleting TerrainNodes of different sizes (LOD)
			if (MaxLod != prevMaxLod || VoxelSize != prevVoxelSize || NodeVoxels != prevNodeVoxels) {
				if (root != null)
					destroyNode(root); // rebuild tree
				root = null;
			}
			
			prevMaxLod = MaxLod;
			prevVoxelSize = VoxelSize;
			prevNodeVoxels = NodeVoxels;
			
			float rootSize = (1 << MaxLod) * NodeVoxels * VoxelSize;
			float3 rootPos = -rootSize / 2;

			rootPos = (floor(playerPos / (rootSize / 2) + 0.5f) - 1f) * (rootSize / 2); // snap root node in a grid half its size so that is contains the player

			if (root == null) {
				root = createNode(MaxLod, rootPos, rootSize);
				root.latestDistToPlayer = 0;
			}

			// Move Root node
			if (root != null && any(rootPos != root.pos)) {

				var oldRoot = root;
				root = createNode(MaxLod, rootPos, rootSize);
				root.latestDistToPlayer = 0;
				
				Debug.Assert(oldRoot.lod == root.lod);
				
				// Keep old children subtrees
				for (int i=0; i<8; ++i) {
					int3 childIndx = TerrainNode.ChildOrder[i];
					
					int childLod = root.lod - 1;
					float childSize = root.size / 2;
					float3 childPos = root.pos + (float3)childIndx * childSize;
					
					for (int j=0; j<8; ++j) {
						if (oldRoot.Children[j] != null) {
							Debug.Assert(oldRoot.Children[j].lod == childLod);

							if (all(oldRoot.Children[j].pos == childPos)) {
								root.Children[i] = oldRoot.Children[j];
								oldRoot.Children[j] = null;
								break;
							}
						}
					}
				}
				
				destroyNode(oldRoot);
			}

			updateTree(root);

			resortNodeList();

			mesher.ManualUpdateStartJobs(sortedNodes, this);

			voxelizer.ManualUpdate(sortedNodes, this);
		}
		void LateUpdate () {
			mesher.ManualUpdateFinishJobs(sortedNodes, this);
		}

		void OnDestroy () {
			if (root != null)
				destroyNode(root); // rebuild tree
			root = null;
		}

		//TerrainNode _lookupOctree (TerrainNode n, float3 worldPos, int minLod) {
		//	if (n.coord.lod < minLod)
		//		return null;
		//	
		//	float3 nodePos = n.coord.ToWorldCube(VoxelSize, NodeVoxels, out float size);
		//	
		//	var hs = size/2;
		//	var pos = worldPos;
		//	pos -= nodePos - hs;
		//	pos /= hs;
		//	var posChild = (int3)floor(pos);
		//	if (all(posChild >= 0 & posChild <= 1)) {
		//		
		//		var child = n.GetChild(posChild);
		//		if (child != null) {
		//			var childLookup = _lookupOctree(child, worldPos, minLod);
		//			if (childLookup != null)	
		//				return childLookup; // in child octant
		//		}
		//		return n; // in octant that does not have a child
		//	} else {
		//		return null; // not in this Nodes octants
		//	}
		//}
		//public TerrainNode LookupOctree (float3 worldPos, int minLod=-1) { // Octree lookup which smallest Node contains the world space postion
		//	if (root == null) return null;
		//	return _lookupOctree(root, worldPos, minLod);
		//}
		//public TerrainNode GetNeighbourTree (TerrainNode n, int3 dir) {
		//	float3 pos = n.coord.ToWorldCube(VoxelSize, NodeVoxels, out float size);
		//	var posInNeighbour = pos + (float3)dir * (size + VoxelSize)/2;
		//	return LookupOctree(posInNeighbour, n.coord.lod);
		//}

		public static readonly Color[] drawColors = new Color[] {
			Color.blue, Color.cyan, Color.green, Color.red, Color.yellow, Color.magenta, Color.gray,
		};
		int _countNodes = 0;
		void drawTree (TerrainNode n) {
			_countNodes++;
			
			Gizmos.color = drawColors[clamp(n.lod % drawColors.Length, 0, drawColors.Length-1)];
			Gizmos.DrawWireCube(n.pos + n.size/2, (float3)n.size);
			
			//if (n.voxels != null)
			//	Gizmos.DrawWireCube(n.pos + n.size/2, (float3)n.size * 0.96f);
			
			for (int i=0; i<8; ++i) {
				if (n.Children[i] != null)
					drawTree(n.Children[i]);
			}
		}
		void drawOctree () {
			_countNodes = 0;
			if (root != null)
				drawTree(root);
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
			float rootSize = (1 << MaxLod) * NodeVoxels * VoxelSize;
			
			GUI.Label(new Rect(0, 0, 500,30), "Terrain Nodes: "+ _countNodes);
			GUI.Label(new Rect(0, 20, 500,30), "Root Node Size: "+ rootSize);
		}
	}
}
