using System.Collections.Generic;
using System.Linq;
using Unity.Collections;
using Unity.Mathematics;
using UnityEngine;
using static Unity.Mathematics.math;

namespace OctreeGeneration {
	public abstract class AtomicTreeOperation {
		public readonly int   lod;
		public readonly float dist;

		public AtomicTreeOperation (int lod, float dist) {
			this.lod = lod;
			this.dist = dist;
		}

		public abstract void Schedule (TerrainOctree octree);
		public abstract bool CanCompleteInstantly ();
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
		public GameObject test;

		float3 playerPos { get { return player.transform.position; } }
		float3 testPos { get { return test.transform.position; } }
		
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
				root = createNode(MaxLod, rootPos, rootSize);
			}
			
			bool moveRoot = root != null && any(rootPos != root.Pos);
			if (moveRoot) {
				considerOp( new MoveRootOp(root, root.Lod, -1, rootPos) );
			}

			if (root != null) {
				if (!root.IsCreated) {
					considerOp( new CreateNodeOp(root.Lod, 0, root, null, -1) );
				}
				updateTreeRecurse(root);
			}
		}

		void updateTreeRecurse (TerrainNode node) {
			bool cond = node != null && !node.IsDestroyed;
			if (!cond) {
				UnityEngine.Assertions.Assert.IsTrue(cond, "wtf");
			}

			_countNodes++;
			
			for (int i=0; i<8; ++i) {
				int3 childIndx = TerrainNode.ChildOctants[i];
				var child = node.Children[i];

				int childLod = node.Lod - 1;
				float childSize = node.Size / 2;
				float3 childPos = node.Pos + (float3)childIndx * childSize;
				
				float dist = CalcDistToPlayer(childPos, childSize);
				int desiredLod = calcLod(dist);
				
				bool wantChild = desiredLod <= childLod;

				if (wantChild && child == null) {
					child = node.Children[i] = createNode(childLod, childPos, childSize);
				}
				
				if (child != null) {
					if (!child.IsCreated) {
						considerOp( new CreateNodeOp(childLod, dist, child, node, i) );
					}
					if (!wantChild) {
						considerOp( new DeleteNodeOp(childLod, dist, child, node, i) );
					}
					
					child = node.Children[i]; // delete op might execute instantly, child should be null then
				}
					
				if (child != null) {
					updateTreeRecurse(child);
				}
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
			UnityEngine.Assertions.Assert.IsTrue(curOp == null);

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
		
		int prioritizeOpType (AtomicTreeOperation op) {
			// prioritize deltions over creations since they're faster and we prevent unbounded node counts this way 
			// prioritize root moves over everything else
			if (op is MoveRootOp) return 0;
			if (op is DeleteNodeOp) return 1;
			if (op is CreateNodeOp) return 2;
			return 3;
		}
		int prioritizeNodes (AtomicTreeOperation l, AtomicTreeOperation r) {
			int             order = prioritizeOpType(l).CompareTo(prioritizeOpType(r));
			if (order == 0) order = l.lod .CompareTo(r.lod ) * (l is DeleteNodeOp ? -1 : 1); // prefer deleting higher lods first for delete ops, since we always delete the whole subtree at once
			if (order == 0) order = l.dist.CompareTo(r.dist);
			return order;
		}
		void considerOp (AtomicTreeOperation op) {
			if (runningOp == null && (curOp == null || prioritizeNodes(op, curOp) < 0)) {
				curOp = op;

				if (curOp.CanCompleteInstantly()) { // Try to apply operation instantly if possible
					curOp.Schedule(this);
					curOp.Apply(this);
					curOp = null;
				}
			}
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
				Debug.Log("Applying atomic tree OP "+ runningOp.ToString());

				runningOp.Apply(this);
				runningOp = null;
			}
		}

		class CreateNodeOp : AtomicTreeOperation {
			readonly TerrainNode node;
			readonly TerrainNode parent;
			readonly int childIndx;

			public override string ToString () {
				var coord = (int3)round(node.Pos / node.Size);
				return string.Format("Creating Node (#{0} {1},{2},{3})", lod, coord.x, coord.y, coord.z);
			}

			TerrainGenerator.GetVoxelsJob voxelsJob;
			TerrainMesher.MeshingJob meshingJob;
			TerrainMesher.MeshingJob parentMeshingJob;
			//TerrainMesher.SeamMeshingJob[] seamJobs;
			
			public CreateNodeOp (int lod, float dist, TerrainNode node, TerrainNode parent, int childIndx) : base(lod, dist) {
				this.node = node;
				this.parent = parent;
				this.childIndx = childIndx;
			}

			public override void Schedule (TerrainOctree octree) {
				voxelsJob = octree.generator.SheduleGetVoxels(node.Pos, node.Size);
				
				meshingJob = octree.mesher.SheduleMeshNode(node.Size, node.GetChildrenMask(), voxelsJob);

				if (parent != null && parent.IsCreated) {
					int childrenMask = parent.GetChildrenMask();
					UnityEngine.Assertions.Assert.IsTrue((childrenMask & (1 << childIndx)) == 0);
					childrenMask |= 1 << childIndx;
				
					parentMeshingJob = octree.mesher.SheduleMeshNode(parent.Size, childrenMask, parent.Voxels);
				}
				
				//var seams = new List<TerrainMesher.SeamMeshingJob>();
				//{
				//	var job = octree.mesher.SheduleMeshSeam(octree, node.Pos, node.Size, lod, meshingJob);
				//	seams.Add(job);
				//}
				//foreach (var n in octree.GetAllTouchingNodesForCreateNode(node.Pos, node.Size, lod)) {
				//	var job = octree.mesher.SheduleMeshSeam(octree, n.Pos, n.Size, n.Lod, meshingJob);
				//	seams.Add(job);
				//}
				//
				//seamJobs = seams.ToArray();
			}
			public override bool CanCompleteInstantly () => false;
			public override bool IsCompleted () => voxelsJob.IsCompleted() && meshingJob.IsCompleted() && (parentMeshingJob?.IsCompleted() ?? true)
				//&& seamJobs.All(x => x.IsCompleted())
				;
			public override void Apply (TerrainOctree octree) {
				if (!node.IsDestroyed) { // if we want to rebuild the tree
					Debug.Assert(!(parent?.IsDestroyed ?? false));

					voxelsJob.Apply(node);
					meshingJob.Apply(node);
					parentMeshingJob?.Apply(parent);

					if (parent != null) {
						//Debug.Assert(parent.Children[childIndx] == null);
						//parent.Children[childIndx] = n;
					} else {
						Debug.Assert(octree.root == node);
						//octree.root = n;
					}

					//foreach (var j in seamJobs)
					//	j.Apply();
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
			readonly TerrainNode node;
			readonly TerrainNode parent;
			readonly int childIndx;

			TerrainMesher.MeshingJob parentMeshingJob;
			
			public override string ToString () {
				var pos = parent.Children[childIndx].Pos;
				var size = parent.Children[childIndx].Size;
				var coord = (int3)round(pos / size);
				return string.Format("Deleting Node (#{0} {1},{2},{3})", lod, coord.x, coord.y, coord.z);
			}

			public DeleteNodeOp (int lod, float dist, TerrainNode node, TerrainNode parent, int childIndx) : base(lod, dist) {
				this.node = node;
				this.parent = parent;
				this.childIndx = childIndx;
			}
			public override void Schedule (TerrainOctree octree) {
				if (parent.IsCreated && node.IsCreated) {
					int childrenMask = parent.GetChildrenMask();
					UnityEngine.Assertions.Assert.IsTrue((childrenMask & (1 << childIndx)) != 0);
					childrenMask &= ~(1 << childIndx);
				
					parentMeshingJob = octree.mesher.SheduleMeshNode(parent.Size, childrenMask, parent.Voxels);
				}
			}
			public override bool CanCompleteInstantly () => !(parent.IsCreated && node.IsCreated);
			public override bool IsCompleted () => (parentMeshingJob?.IsCompleted() ?? true);
			public override void Apply (TerrainOctree octree) {
				if (!parent.IsDestroyed) {
					destroyNode(parent.Children[childIndx]);
				}
				parentMeshingJob?.Apply(parent);

				parent.Children[childIndx] = null;
				Dispose();
			}
			public override void Dispose () {
				parentMeshingJob?.Dispose();
			}
		}
		
		class MoveRootOp : AtomicTreeOperation {
			readonly TerrainNode oldRoot;
			readonly float3 newPos;

			public override string ToString () {
				return string.Format("Moving Root Node");
			}

			TerrainNode[] newChildren;
			
			public MoveRootOp (TerrainNode oldRoot, int lod, float dist, float3 newPos) : base(lod, dist) {
				this.oldRoot = oldRoot;
				this.newPos = newPos;
			}

			TerrainNode[] getNewChildren () {
				var children = new TerrainNode[8];
				for (int i=0; i<8; ++i) {
					int3 childIndx = TerrainNode.ChildOctants[i];
					
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
				newChildren = getNewChildren();
			}
			public override bool CanCompleteInstantly () => true;
			public override bool IsCompleted () => true;
			public override void Apply (TerrainOctree octree) {
				if (!oldRoot.IsDestroyed) { // if we want to rebuild the tree
					var newRoot = octree.createNode(lod, newPos, oldRoot.Size);
					octree.root = newRoot;

					moveChildren(newRoot);
				}
				Dispose();
			}
			public override void Dispose () {
			}
		}
		#endregion

		#region octree lookups and node neighbourship logic
		TerrainNode _lookupOctree (TerrainNode n, float3 worldPos, int minLod) {
			if (n.Lod < minLod)
				return null;
			
			var pos = worldPos;
			pos -= n.Pos;
			pos /= n.Size/2;
			var posChild = (int3)floor(pos);
			if (all(posChild >= 0 & posChild <= 1)) {
				
				var child = n.GetChild(posChild);
				if (child != null) {
					var childLookup = _lookupOctree(child, worldPos, minLod);
					if (childLookup != null)	
						return childLookup; // in child octant
				}
				return n; // in octant that does not have a child
			} else {
				return null; // not in this Nodes octants
			}
		}
		public TerrainNode LookupOctree (float3 worldPos, int minLod=-1) { // Octree lookup which smallest Node contains the world space postion
			if (root == null) return null;
			return _lookupOctree(root, worldPos, minLod);
		}

		public TerrainNode GetNeighbourTree (TerrainNode n, int3 dir) { // find neighbouring nodes of the octree, returns leaf the node of equal or larger size neighbouring this node in the direction specified by dir [-1, 0, +1] for each axis
			var posInNeighbour = n.Pos + n.Size/2 + (float3)dir * (n.Size + VoxelSize*TerrainNode.VOXEL_COUNT)/2;

			Gizmos.color = Color.yellow;
			Gizmos.DrawWireCube(posInNeighbour, (float3)1);

			return LookupOctree(posInNeighbour, n.Lod);
		}

		//
		//class VirtualNode {
		//	public float3	pos;
		//	public float	size;
		//	public int		lod;
		//
		//	public Voxels	voxels;
		//	public Cells	cells;
		//};
		//TerrainNode _lookupOctreeForCreateNode (TerrainNode n, float3 nodePos, int nodeLod, float3 worldPos, int minLod) {
		//	if (n.Lod < minLod)
		//		return null;
		//	
		//	var pos = worldPos;
		//	pos -= n.Pos;
		//	pos /= n.Size/2;
		//	var posChild = (int3)floor(pos);
		//
		//	if (all(posChild >= 0 & posChild <= 1)) {
		//		
		//		var child = n.GetChild(posChild);
		//		if (child != null) {
		//			var childLookup = _lookupOctreeForCreateNode(child, nodePos, nodeLod, worldPos, minLod);
		//			if (childLookup != null)	
		//				return childLookup; // in child octant
		//		} else if ((n.Lod - 1) == nodeLod && all(posChild == nodePos)) {
		//			return 
		//		}
		//		return n; // in octant that does not have a child
		//	} else {
		//		return null; // not in this Nodes octants
		//	}
		//}
		//public TerrainNode LookupOctreeForCreateNode (float3 nodePos, int nodeLod, float3 worldPos, int minLod=-1) { // Octree lookup which smallest Node contains the world space postion
		//	if (root == null) return null;
		//	return _lookupOctreeForCreateNode(root, nodePos, nodeLod, worldPos, minLod);
		//}
		//
		//public TerrainNode GetNeighbourTreeForCreateNode (TerrainNode n, float3 nodePos, float nodeSize, int nodeLod, int3 dir) { // find neighbouring nodes of the octree, returns leaf the node of equal or larger size neighbouring this node in the direction specified by dir [-1, 0, +1] for each axis
		//	var posInNeighbour = n.Pos + n.Size/2 + (float3)dir * (n.Size + VoxelSize*TerrainNode.VOXEL_COUNT)/2;
		//	
		//	Gizmos.color = Color.yellow;
		//	Gizmos.DrawWireCube(posInNeighbour, (float3)1);
		//
		//	return LookupOctreeForCreateNode(nodePos, nodeSize, nodeLod, posInNeighbour, nodeLod);
		//}
		//public TerrainNode GetNeighbourTreeForCreateNode (float3 nodePos, float nodeSize, int nodeLod, int3 dir) { // find neighbouring nodes of the octree, returns leaf the node of equal or larger size neighbouring this node in the direction specified by dir [-1, 0, +1] for each axis
		//	var posInNeighbour = nodePos + nodeSize/2 + (float3)dir * (nodeSize + VoxelSize*TerrainNode.VOXEL_COUNT)/2;
		//
		//	Gizmos.color = Color.yellow;
		//	Gizmos.DrawWireCube(posInNeighbour, (float3)1);
		//
		//	return LookupOctree(posInNeighbour, nodeLod);
		//}

		public static readonly int3[] NeighbourDirs = new int3[] {
			int3(-1,-1,-1), int3( 0,-1,-1), int3(+1,-1,-1),
			int3(-1, 0,-1), int3( 0, 0,-1), int3(+1, 0,-1),
			int3(-1,+1,-1), int3( 0,+1,-1), int3(+1,+1,-1),

			int3(-1,-1, 0), int3( 0,-1, 0), int3(+1,-1, 0),
			int3(-1, 0, 0),	                int3(+1, 0, 0),
			int3(-1,+1, 0), int3( 0,+1, 0), int3(+1,+1, 0),

			int3(-1,-1,+1), int3( 0,-1,+1), int3(+1,-1,+1),
			int3(-1, 0,+1), int3( 0, 0,+1), int3(+1, 0,+1),
			int3(-1,+1,+1), int3( 0,+1,+1), int3(+1,+1,+1),
		};
		
		HashSet<TerrainNode> GetAllTouchingNodes (TerrainNode n) {
			var touching = new HashSet<TerrainNode>();
			
			for (int octant=0; octant<8; ++octant) {
				if (n.Children[octant] == null) { // for empty octants
					for (int child=0; child<8; ++child) {
						if (n.Children[child] != null) {
							int3 dir = TerrainNode.ChildOctants[octant] - TerrainNode.ChildOctants[child];
							TerrainNode.GetNodesInDir(n.Children[child], dir, touching);
						}
					}
				}
			}

			for (int i=0; i<NeighbourDirs.Length; ++i) {
				int3 dir = NeighbourDirs[i];

				var neigh = GetNeighbourTree(n, dir);
				if (neigh != null) {
					if (neigh.Lod > n.Lod) {
						touching.Add(neigh);
					} else {
						TerrainNode.GetNodesInDir(neigh, -dir, touching);
					}
				}
			}

			return touching;
		}
		//HashSet<TerrainNode> GetAllTouchingNodesForCreateNode (float3 nodePos, float nodeSize, int nodeLod) {
		//	var touching = new HashSet<TerrainNode>();
		//	
		//	for (int i=0; i<NeighbourDirs.Length; ++i) {
		//		int3 dir = NeighbourDirs[i];
		//
		//		var neigh = GetNeighbourTreeForCreateNode(nodePos, nodeSize, nodeLod, dir);
		//		if (neigh != null) {
		//			if (neigh.Lod > nodeLod) {
		//				touching.Add(neigh);
		//			} else {
		//				TerrainNode.GetNodesInDir(neigh, -dir, touching);
		//			}
		//		}
		//	}
		//
		//	return touching;
		//}
		#endregion

		#region Debug Visualizations
		public static readonly Color[] _drawColors = new Color[] {
			Color.blue, Color.cyan, Color.green, Color.red, Color.yellow, Color.magenta, Color.gray,
		};
		public static Color _GetLodColor (int lod) => _drawColors[clamp(lod % _drawColors.Length, 0, _drawColors.Length-1)];

		void drawTree (TerrainNode n) {
			Gizmos.color = _GetLodColor(n.Lod);
			Gizmos.DrawWireCube(n.Center, n.IsCreated ? (float3)n.Size : (float3)n.Size * 0.5f);
			
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

			//if (test != null) {
			//	var testN = LookupOctree(testPos);
			//	
			//	if (testN != null) {
			//		var touching = GetAllTouchingNodes(testN);
			//		foreach (var n in touching) {
			//			Gizmos.color = Color.yellow;
			//			Gizmos.DrawWireCube(n.Pos + n.Size/2, (float3)n.Size * 0.99f);
			//		}
			//
			//		Gizmos.color = Color.red;
			//		Gizmos.DrawWireCube(testN.Pos + testN.Size/2, (float3)testN.Size * 0.99f);
			//	}
			//}
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
