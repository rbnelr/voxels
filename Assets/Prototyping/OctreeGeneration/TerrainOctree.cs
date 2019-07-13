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
		public readonly int   lod;
		public readonly float dist;

		public AtomicTreeOperation (int lod, float dist) {
			this.lod = lod;
			this.dist = dist;
		}

		public abstract void Schedule (TerrainOctree octree);
		public abstract bool IsCompleted ();
		public abstract void Apply (TerrainOctree octree);
		public abstract void Dispose ();

		public abstract override string ToString ();
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

		float3 playerPos { get { return player.transform.position; } }
		
		public bool AlwaysDrawOctree = false;

		TerrainGenerator generator;
		TerrainMesher mesher;

		#region main logic
		void Awake () {
			generator = GetComponent<TerrainGenerator>();
			mesher = GetComponent<TerrainMesher>();
		}
		
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
		void updateTree () {
			_countNodes = 0;

			float rootSize = (1 << MaxLod) * TerrainNode.VOXEL_COUNT * VoxelSize;
			float3 rootPos = -rootSize / 2;

			rootPos = (floor(playerPos / (rootSize / 2) + 0.5f) - 1f) * (rootSize / 2); // snap root node in a grid half its size so that is contains the player

			if (root == null) {
				considerOp( new CreateNodeOp(null, -1, MaxLod, -1, rootPos, rootSize) );
			}
			
			bool moveRoot = root != null && any(rootPos != root.Pos);
			if (moveRoot) {
				considerOp( new MoveRootOp(root, root.Lod, -1, rootPos) );
			}

			if (root != null && !moveRoot) {
				updateTreeRecurse(root);
			}
		}

		void updateTreeRecurse (TerrainNode node) {
			Debug.Assert(node != null && !node.IsDestroyed);
			
			_countNodes++;
			
			for (int i=0; i<8; ++i) {
				int3 childIndx = TerrainNode.ChildOrder[i];
				var child = node.Children[i];

				int childLod = node.Lod - 1;
				float childSize = node.Size / 2;
				float3 childPos = node.Pos + (float3)childIndx * childSize;
				
				float dist = CalcDistToPlayer(childPos, childSize);
				int desiredLod = calcLod(dist);
				
				bool wantChild = desiredLod <= childLod;

				if (wantChild && child == null) {
					considerOp( new CreateNodeOp(node, i, childLod, dist, childPos, childSize) );
				}
				if (!wantChild && child != null) {
					considerOp( new DeleteNodeOp(node, i, childLod, dist) );
				}

				if (child != null && wantChild)
					updateTreeRecurse(child);
			}
		}

		void Update () { // Updates the Octree by creating and deleting TerrainNodes of different sizes (LOD)
			if (MaxLod != prevMaxLod || VoxelSize != prevVoxelSize) {
				if (root != null)
					destroyNode(root); // rebuild tree
				root = null;
			}
			
			prevMaxLod = MaxLod;
			prevVoxelSize = VoxelSize;
			
			applyAtomicTreeOp();
			
			updateTree();

			processAtomicTreeOp();
		}
		void LateUpdate () {
			applyAtomicTreeOp(); // give ops a chance to complete in the same frame
		}

		void OnDestroy () {
			Debug.Assert(curOp == null);

			if (runningOp != null)
				runningOp.Dispose();
			runningOp = null;
			if (root != null)
				destroyNode(root); // rebuild tree
			root = null;
		}
		#endregion

		#region Tree Operations
		AtomicTreeOperation curOp = null;
		AtomicTreeOperation runningOp = null;
		
		int prioritizeNodes (AtomicTreeOperation l, AtomicTreeOperation r) {
			int             order = (l is DeleteNodeOp ? 0:1).CompareTo(r is DeleteNodeOp ? 0:1); // priotorize deltions over creations since they're faster and we prevent unbounded node counts this way 
			//if (order == 0) order = -l.lod .CompareTo(r.lod ); // dont prefer lods, just use distance
			if (order == 0) order = l.dist.CompareTo(r.dist);
			return order;
		}
		void considerOp (AtomicTreeOperation op) {
			if (runningOp == null && (curOp == null || prioritizeNodes(op, curOp) < 0))
				curOp = op;
		}
		void processAtomicTreeOp () {
			if (curOp != null) {
				curOp.Schedule(this);
				runningOp = curOp;
				curOp = null;
			}
		}
		void applyAtomicTreeOp () {
			if (runningOp != null && runningOp.IsCompleted()) {
				runningOp.Apply(this);
				runningOp = null;
			}
		}

		class CreateNodeOp : AtomicTreeOperation {
			readonly TerrainNode parent;
			readonly int childIndx;
			readonly float3 pos;
			readonly float size;

			public override string ToString () {
				var coord = (int3)round(pos / size);
				return string.Format("Creating Node (#{0:2} {1:2}:{2:2})", lod, coord.x, coord.y, coord.z);
			}

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
				
				meshingJob = octree.mesher.MeshNode(size, 0, voxelsJob);
				meshingJob.Schedule();

				if (parent != null) {
					int childrenMask = parent.GetChildrenMask();
					Debug.Assert((childrenMask & (1 << childIndx)) == 0);
					childrenMask |= 1 << childIndx;
				
					parentMeshingJob = octree.mesher.MeshNode(parent.Size, childrenMask, parent.Voxels);
					parentMeshingJob.Schedule();
				}
			}
			public override bool IsCompleted () => voxelsJob.IsCompleted() && meshingJob.IsCompleted() && (parentMeshingJob?.IsCompleted() ?? true);
			public override void Apply (TerrainOctree octree) {
				if (parent?.IsDestroyed ?? false)
					return; // can happen on root move
				
				var n = octree.createNode(lod, pos, size);

				voxelsJob.Apply(n);
				meshingJob.Apply(n);
				parentMeshingJob?.Apply(parent);

				if (parent != null) {
					Debug.Assert(parent.Children[childIndx] == null);
					parent.Children[childIndx] = n;
				} else {
					Debug.Assert(octree.root == null);
					octree.root = n;
				}

				Dispose();
			}
			public override void Dispose () {
				voxelsJob.Dispose();
				meshingJob.Dispose();
				parentMeshingJob?.Dispose();
			}
		}

		class DeleteNodeOp : AtomicTreeOperation {
			readonly TerrainNode parent;
			readonly int childIndx;

			TerrainMesher.MeshingJob parentMeshingJob;
			
			public override string ToString () {
				var pos = parent.Children[childIndx].Pos;
				var size = parent.Children[childIndx].Size;
				var coord = (int3)round(pos / size);
				return string.Format("Deleting Node (#{0:2} {1:2}:{2:2})", lod, coord.x, coord.y, coord.z);
			}

			public DeleteNodeOp (TerrainNode parent, int childIndx, int lod, float dist) : base(lod, dist) {
				this.parent = parent;
				this.childIndx = childIndx;
			}
			public override void Schedule (TerrainOctree octree) {
				int childrenMask = parent.GetChildrenMask();
				Debug.Assert((childrenMask & (1 << childIndx)) != 0);
				childrenMask &= ~(1 << childIndx);
				
				parentMeshingJob = octree.mesher.MeshNode(parent.Size, childrenMask, parent.Voxels);
				parentMeshingJob.Schedule();
			}
			public override bool IsCompleted () => true;
			public override void Apply (TerrainOctree octree) {
				if (parent.IsDestroyed)
					return; // can happen on root move

				destroyNode(parent.Children[childIndx]);
				
				parentMeshingJob.Apply(parent);

				parent.Children[childIndx] = null;

				Dispose();
			}
			public override void Dispose () {
				parentMeshingJob.Dispose();
			}
		}
		
		class MoveRootOp : AtomicTreeOperation {
			readonly TerrainNode oldRoot;
			readonly float3 newPos;

			public override string ToString () {
				return string.Format("Moving Root Node");
			}

			TerrainNode[] newChildren;

			TerrainGenerator.GetVoxelsJob voxelsJob;
			TerrainMesher.MeshingJob meshingJob;
			
			public MoveRootOp (TerrainNode oldRoot, int lod, float dist, float3 newPos) : base(lod, dist) {
				this.oldRoot = oldRoot;
				this.newPos = newPos;
			}

			TerrainNode[] getNewChildren () {
				var children = new TerrainNode[8];
				for (int i=0; i<8; ++i) {
					int3 childIndx = TerrainNode.ChildOrder[i];
					
					float childSize = oldRoot.Size / 2;
					float3 childPos = newPos + (float3)childIndx * childSize;
					
					for (int j=0; j<8; ++j) {
						if (oldRoot.Children[j] != null && all(oldRoot.Children[j].Pos == childPos)) {
							children[i] = oldRoot.Children[j];
							break;
						}
					}
				}
				return children;
			}
			void moveChildren (TerrainNode newRoot) {
				for (int i=0; i<8; ++i) {
					if (newChildren.Contains(oldRoot.Children[i])) {
						oldRoot.Children[i] = null; // remove child from old root so it won't be destroyed
					}
				}
				newRoot.Children = newChildren;
				destroyNode(oldRoot);
			}

			public override void Schedule (TerrainOctree octree) {
				voxelsJob = octree.generator.GetVoxels(newPos, oldRoot.Size);
				voxelsJob.Schedule();
				
				newChildren = getNewChildren();
				int newChildrenMask = TerrainNode.GetChildrenMask(newChildren);

				meshingJob = octree.mesher.MeshNode(oldRoot.Size, newChildrenMask, voxelsJob);
				meshingJob.Schedule();
			}
			public override bool IsCompleted () => voxelsJob.IsCompleted() && meshingJob.IsCompleted();
			public override void Apply (TerrainOctree octree) {
				var newRoot = octree.createNode(lod, newPos, oldRoot.Size);
				octree.root = newRoot;

				voxelsJob.Apply(newRoot);
				meshingJob.Apply(newRoot);

				moveChildren(newRoot);

				Dispose();
			}
			public override void Dispose () {
				voxelsJob.Dispose();
				meshingJob.Dispose();
			}
		}
		#endregion

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

		#region Debug Visualizations
		public static readonly Color[] _drawColors = new Color[] {
			Color.blue, Color.cyan, Color.green, Color.red, Color.yellow, Color.magenta, Color.gray,
		};
		public static Color _GetLodColor (int lod) => _drawColors[clamp(lod % _drawColors.Length, 0, _drawColors.Length-1)];

		void drawTree (TerrainNode n) {
			Gizmos.color = _GetLodColor(n.Lod);
			Gizmos.DrawWireCube(n.Pos + n.Size/2, (float3)n.Size);
			
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

			GUI.color = runningOp == null ? Color.white : _GetLodColor(runningOp.lod);
			GUI.Label(new Rect(0, 40, 500,30), "Async: "+ runningOp?.ToString() ?? "");
		}
		#endregion
	}
}
