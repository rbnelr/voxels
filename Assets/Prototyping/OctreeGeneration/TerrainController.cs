using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace OctreeGeneration {
	public class TerrainNode {
		public readonly static Vector3Int[] childrenPos = new Vector3Int[] {
			new Vector3Int(-1,-1,-1),
			new Vector3Int( 1,-1,-1),
			new Vector3Int(-1, 1,-1),
			new Vector3Int( 1, 1,-1),
			new Vector3Int(-1,-1, 1),
			new Vector3Int( 1,-1, 1),
			new Vector3Int(-1, 1, 1),
			new Vector3Int( 1, 1, 1),
		};
		public TerrainNode getChild (int x, int y, int z) { // x,y,z: [0,1]
			return children[z*4 + y*2 + x];
		}

		public TerrainNode[] children;
		public TerrainNode parent;
		public int depth;

		public Vector3 pos; // center of cube, must always be the center of the 8 sub cubes of the parent node
		public float size; // must always be 1/2 of the size of the parent cube

		public GameObject go;
		public Mesh mesh;

		public Voxel[,,] voxels;

		public bool needsRemesh;
	}
	
	public class TerrainController : MonoBehaviour {
		
		public GameObject TerrainChunkPrefab;
		
		public TerrainGenerator generator;
		public GameObject player;
		Vector3 playerPos { get { return player.transform.position; } }

		public GameObject test;
		Vector3 testPos { get { return test.transform.position; } }
		public Vector3Int testDir;

		public float VoxelSize = 1f;
		public int ChunkVoxels = 32;
		
		[Range(0, 15)]
		public int MaxLod = 7;

		TerrainNode root;
		
		TerrainNode _lookupOctree (TerrainNode n, Vector3 worldPos, int depth, int maxDepth) {
			if (depth > maxDepth)
				return null;
			
			var hs = n.size/2;
			var pos = worldPos;
			pos -= n.pos - new Vector3(hs,hs,hs);
			pos /= hs;
			var posChild = VectorExt.FloorToInt(pos);
			if (	posChild.x >= 0 && posChild.x <= 1 &&
					posChild.y >= 0 && posChild.y <= 1 &&
					posChild.z >= 0 && posChild.z <= 1) {
				
				var child = n.getChild(posChild.x, posChild.y, posChild.z);
				if (child != null) {
					var childLookup = _lookupOctree(child, worldPos, depth+1, maxDepth);
					if (childLookup != null)	
						return childLookup; // in child octant
				}
				return n; // in octant that does not have a child
			} else {
				return null; // not in this nodes octants
			}
		}
		public TerrainNode LookupOctree (Vector3 worldPos, int maxDepth=int.MaxValue) { // Octree lookup which smallest Node contains the world space postion
			if (root == null) return null;
			return _lookupOctree(root, worldPos, 0, maxDepth);
		}
		public TerrainNode GetNeighbourTree (TerrainNode n, int x, int y, int z) {
			var posInNeighbour = n.pos + new Vector3(x,y,z) * (n.size + VoxelSize)/2;
			return LookupOctree(posInNeighbour, n.depth);
		}

		TerrainNode newNode (float size, Vector3 pos, int depth, TerrainNode parent=null) {
			var n = new TerrainNode {
				children = new TerrainNode[8],
				parent = parent,
				depth = depth,

				pos = pos,
				size = size,
				
				go = Instantiate(TerrainChunkPrefab, pos, Quaternion.identity, this.gameObject.transform),
				mesh = new Mesh(),

				needsRemesh = true,
			};

			n.mesh.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32;
			n.go.GetComponent<MeshFilter>().mesh = n.mesh;

			TerrainVoxelizer.Voxelize(n, ChunkVoxels, generator);
			
			if (n.parent != null)
				n.parent.needsRemesh = true;

			return n;
		}
		void deleteNode (TerrainNode n) { // deletes all children recursivly
			Destroy(n.go);
			Destroy(n.mesh); // is this needed?
			for (int i=0; i<8; ++i)
				if (n.children[i] != null)
					deleteNode(n.children[i]);

			if (n.parent != null)
				n.parent.needsRemesh = true;
		}
		void updateNodeLod (TerrainNode node, int depth=0) { // Creates and Deletes Nodes when needed according to Lod function
			if (depth == MaxLod) return;
			
			for (int i=0; i<8; ++i) {
				var child = node.children[i];

				var size = node.size / 2;
				var pos = node.pos + (Vector3)TerrainNode.childrenPos[i] * size / 2;
				
				if (child != null) {
					Debug.Assert(child.pos == pos);
					Debug.Assert(child.size == size);
				}

				var closest = pos + VectorExt.Clamp(playerPos - pos, new Vector3(-size/2,-size/2,-size/2), new Vector3(size/2,size/2,size/2));
				var dist = (playerPos - closest).magnitude;
				
				int nodeLod = MaxLod - depth;
				var desiredLod = Mathf.FloorToInt(calcLod(dist));

				bool needChild = desiredLod < nodeLod;

				if (needChild && child == null) {
					child = newNode(size, pos, depth+1, node);
				} else if (!needChild && child != null) {
					deleteNode(child);
					child = null;
				}

				if (child != null) {
					updateNodeLod(child, depth +1);
				}
				
				node.children[i] = child;
			}
		}
		
		int calcLod (float dist) {
			float m = 0.5f;
			float n = 16f;
			float l = 6f;

			float a = -l / ( Mathf.Log(m) - Mathf.Log(n) );
			float b = (l * Mathf.Log(m)) / ( Mathf.Log(m) - Mathf.Log(n) );
		
			float Lod0ChunkSize = VoxelSize * ChunkVoxels;
			var lod = Mathf.FloorToInt( a * Mathf.Log(dist / Lod0ChunkSize) + b );
			return lod;
		}

		int remeshLimiter = 0;

		void updateNodeMesh (TerrainNode node) {
			if (node.needsRemesh && remeshLimiter == 0) {
				TerrainMesher.Meshize(node, ChunkVoxels, this);
				node.needsRemesh = false;
				remeshLimiter++;
			}
			
			for (int i=0; i<8; ++i) {
				if (node.children[i] != null)
					updateNodeMesh(node.children[i]);
			}
		}

		float _prevVoxelSize;
		int _prevChunkVoxels;
		int _prevMaxLod;

		private void Update () {
			if (	VoxelSize != _prevVoxelSize || 
					ChunkVoxels != _prevChunkVoxels ||
					MaxLod != _prevMaxLod ) {

				if (root != null)
					deleteNode(root);
				root = null;
				_prevVoxelSize = VoxelSize;
				_prevChunkVoxels = ChunkVoxels;
				_prevMaxLod = MaxLod;
			}

			float Lod0NodeSize = VoxelSize * ChunkVoxels;
			float RootNodeSize = Lod0NodeSize * Mathf.Pow(2f, MaxLod);

			if (root == null) {
				root = newNode(RootNodeSize, new Vector3(0,0,0), 0);
			}

			updateNodeLod(root);

			remeshLimiter = 0;
			updateNodeMesh(root);
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
		int _countNodes = 0;
		void drawNode (TerrainNode n, int depth=0) {
			Gizmos.color = drawColors[Mathf.Clamp(MaxLod - depth + 1, 0, drawColors.Length-1)];
			Gizmos.DrawWireCube(n.pos, new Vector3(n.size, n.size, n.size));
			_countNodes++;

			for (int i=0; i<8; ++i) {
				if (n.children[i] != null) {
					drawNode(n.children[i], depth+1);
				}
			}
		}
		void OnDrawGizmosSelected () {
			//_countNodes = 0;
			//if (root != null)
			//	drawNode(root);
		}

		void OnDrawGizmos () {
			//{ // debug test octree lookup
			//	var n = LookupOctree(testPos);
			//	if (n != null) {
			//		Gizmos.color = Color.red;
			//		Gizmos.DrawWireCube(n.pos, new Vector3(n.size*0.9f, n.size*0.9f, n.size*0.9f));
			//
			//		n = GetNeighbourTree(n, testDir.x, testDir.y, testDir.z);
			//		if (n != null) {
			//			Gizmos.color = Color.blue;
			//			Gizmos.DrawWireCube(n.pos, new Vector3(n.size*0.9f, n.size*0.9f, n.size*0.9f));
			//		}
			//	}
			//}
			
			_countNodes = 0;
			if (root != null)
				drawNode(root);
		}

		void OnGUI () {
			float Lod0NodeSize = VoxelSize * ChunkVoxels;
			float RootNodekSize = Lod0NodeSize * Mathf.Pow(2f, MaxLod);
			
			GUI.Label(new Rect(0,  0, 500,30), "Terrain Nodes: "+ _countNodes);
			GUI.Label(new Rect(0, 30, 500,30), "Root Node Size: "+ RootNodekSize);
		}
	}
}
