using UnityEngine;
using Unity.Mathematics;
using System.Linq;
using static Unity.Mathematics.math;
using Unity.Collections;
using System.Collections.Generic;
using UnityEngine.Profiling;
using System.Collections.ObjectModel;

namespace OctreeGeneration {
	public abstract class NodeOperation {
		public abstract void Schedule ();
		public abstract bool IsCompleted ();
		public abstract void Apply (TerrainNode node);
		public abstract void Dispose ();
	}
	public abstract class AtomicTreeOperation {
		public int   lod;
		public float dist;

		public AtomicTreeOperation (int lod, float dist) {
			this.lod = lod;
			this.dist = dist;
		}

		public abstract void Schedule (TerrainOctree octree);
		public abstract bool IsCompleted ();
		public abstract void Apply (TerrainOctree octree);
		public abstract void Dispose ();
	}

	[RequireComponent(typeof(TerrainGenerator), typeof(TerrainMesher))]
	public class TerrainOctree : MonoBehaviour {
		
		public float VoxelSize = 1;
		[Range(0, 15)]
		public int MaxLod = 5;

		public float LodFuncStart = 0.5f;
		public float LodFuncEnd = 16f;
		public float LodFuncEndLod = 6f;
		
		float prevVoxelSize;
		int prevMaxLod;
		
		public GameObject TerrainNodePrefab;
		public GameObject player;
		
		public bool AlwaysDrawOctree = false;

		TerrainGenerator generator;
		TerrainMesher mesher;

		void Awake () {
			generator = GetComponent<TerrainGenerator>();
			mesher = GetComponent<TerrainMesher>();
		}

		float3 playerPos { get { return player.transform.position; } }
		
		TerrainNode root = null; // Tree of visible TerrainNodes
		
		TerrainNode createNode (int lod, float3 pos, float size) {
			var n = new TerrainNode(lod, pos, size, TerrainNodePrefab, this.transform);
			return n;
		}
		static void destroyNode (TerrainNode n) {
			for (int i=0; i<8; ++i)
				if (n.Children[i] != null)
					destroyNode(n.Children[i]);

			n.Destroy();
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
		
			float Lod0NodeSize = VoxelSize * TerrainNode.VOXEL_COUNT;
			var lod = max(Mathf.FloorToInt( a * Mathf.Log(dist / Lod0NodeSize) + b ), 0);
			return lod;
		}
		
		int _countNodes = 0;
		void updateTree (TerrainNode node) {
			Debug.Assert(node != null && !node.IsDestroyed);
			
			_countNodes++;
			
			for (int i=0; i<8; ++i) {
				int3 childIndx = TerrainNode.ChildOrder[i];
				var child = node.Children[i];

				int childLod = node.lod - 1;
				float childSize = node.size / 2;
				float3 childPos = node.pos + (float3)childIndx * childSize;
				
				float dist = CalcDistToPlayer(childPos, childSize);
				int desiredLod = calcLod(dist);
				
				bool wantChild = desiredLod <= childLod;

				//if (wantChild && child == null) {
				//	child = createNode(childLod, childPos, childSize, node);
				//}
				//if (!wantChild && child != null) {
				//	destroyNode(child);
				//	child = null;
				//}

				if (wantChild && child == null) {
					createNodeOp(node, i, childLod, childPos, childSize, dist);
				}
				if (!wantChild && child != null) {
					deleteNodeOp(node, i, childLod, dist);
				}

				if (child != null && wantChild) {
					node.latestDistToPlayer = dist;
					
					updateTree(child);
				}
			}
		}
		
		AtomicTreeOperation curOp = null;
		
		int priotorizeNodes (AtomicTreeOperation l, AtomicTreeOperation r) {
			int             order = l.lod .CompareTo(r.lod );
			if (order == 0) order = l.dist.CompareTo(r.dist);
			return order;
		}
		void considerOp (AtomicTreeOperation op) {
			if (curOp == null || priotorizeNodes(op, curOp) < 0)
				curOp = op;
		}
		void processAtomicTreeOp () {
			if (curOp != null) {
				curOp.Schedule();
			}
		}
		void applyAtomicTreeOp () {
			if (curOp != null && curOp.IsCompleted()) {
				curOp.Apply(this);
			}
			curOp = null;
		}

		class CreateNodeOp : AtomicTreeOperation {
			public TerrainNode parent;
			public int childIndx;
			public float3 pos;
			public float size;
			
			TerrainGenerator.GetVoxelsJob voxelsJob;
			TerrainMesher.MeshingJob meshingJob;
			TerrainMesher.MeshingJob parentMeshingJob;
			
			public CreateNodeOp (TerrainNode parent, int childIndx, int lod, float dist, float3 pos, float size) : base(lod, dist) {
				this.parent = parent;
				this.childIndx = childIndx;
				this.pos = pos;
				this.size = size;
			}

			public override void Schedule (TerrainOctree octree) {
				voxelsJob = octree.generator.GetVoxels(pos, size);
				voxelsJob.Schedule();
				
				meshingJob = octree.mesher.MeshNode(size, 0, voxelsJob.Voxels);
				meshingJob.Schedule();

				int childrenMask = parent.GetChildrenMask();
				Debug.Assert((childrenMask & (1 << childIndx)) == 0);
				childrenMask |= 1 << childIndx;
				
				parentMeshingJob = octree.mesher.MeshNode(parent.size, childrenMask, voxelsJob.Voxels);
				parentMeshingJob.Schedule();
			}
			public override bool IsCompleted () => voxelsJob.IsCompleted() && meshingJob.IsCompleted() && parentMeshingJob.IsCompleted();
			public override void Apply (TerrainOctree octree) {
				if (parent.IsDestroyed)
					return; // can happen on root move
				Debug.Assert(parent.Children[childIndx] == null);
				
				var n = octree.createNode(parent.lod -1, pos, size);

				voxelsJob.Apply(n);
				meshingJob.Apply(n);
				parentMeshingJob.Apply(parent);

				parent.Children[childIndx] = n;

				Dispose();
			}
			public override void Dispose () {
				voxelsJob.Dispose();
				meshingJob.Dispose();
				parentMeshingJob.Dispose();
			}
		}

		class DeleteNodeOp : AtomicTreeOperation {
			TerrainNode parent;
			int childIndx;



			public override void Schedule () { }
			public override bool IsCompleted () => true;
			public override void Apply (TerrainOctree octree) {
				if (parent.IsDestroyed)
					return; // can happen on root move
				destroyNode(parent.Children[childIndx]);
				parent.Children[childIndx] = null;
			}
			public override void Dispose () {
				voxelsJob.Dispose();
				meshingJob.Dispose();
			}
		}
		void deleteNodeOp (TerrainNode parent, int childIndx, int lod, float dist) {
			var op = new CreateNodeOp { lod=lod, dist=dist, parent=parent, childIndx=childIndx };
			if (curOp == null || priotorizeNodes(op, curOp) < 0)
				curOp = op;
		}

		void Update () { // Updates the Octree by creating and deleting TerrainNodes of different sizes (LOD)
			if (MaxLod != prevMaxLod || VoxelSize != prevVoxelSize) {
				if (root != null)
					destroyNode(root); // rebuild tree
				root = null;
			}
			
			prevMaxLod = MaxLod;
			prevVoxelSize = VoxelSize;
			
			float rootSize = (1 << MaxLod) * TerrainNode.VOXEL_COUNT * VoxelSize;
			float3 rootPos = -rootSize / 2;

			rootPos = (floor(playerPos / (rootSize / 2) + 0.5f) - 1f) * (rootSize / 2); // snap root node in a grid half its size so that is contains the player

			if (root == null) {
				root = createNode(MaxLod, rootPos, rootSize);
			}

			// Move Root node
			if (root != null && any(rootPos != root.pos)) {

				var oldRoot = root;
				root = createNode(MaxLod, rootPos, rootSize);
				
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

			applyAtomicTreeOp();
			
			_countNodes = 0;
			updateTree(root);

			processAtomicTreeOp();
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
		void drawTree (TerrainNode n) {
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
			float rootSize = (1 << MaxLod) * TerrainNode.VOXEL_COUNT * VoxelSize;
			
			GUI.Label(new Rect(0, 0, 500,30), "Terrain Nodes: "+ _countNodes);
			GUI.Label(new Rect(0, 20, 500,30), "Root Node Size: "+ rootSize);
		}
	}
}
