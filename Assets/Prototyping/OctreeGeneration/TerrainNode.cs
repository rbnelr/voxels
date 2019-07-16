using System.Collections.Generic;
using Unity.Collections;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.Profiling;
using static Unity.Mathematics.math;

namespace OctreeGeneration {
	public static class VoxelUtil {
		public static int _3dToFlatIndex (int3 _3dIndex, int3 arrSize) {
			return _3dIndex.z * arrSize.y * arrSize.x + _3dIndex.y * arrSize.x + _3dIndex.x;
		}
		public static int3 flatTo3dIndex (int flatIndex, int3 arrSize) {
			int3 _3dIndex;
			_3dIndex.x = flatIndex % arrSize.x;
			flatIndex /= arrSize.x;
			_3dIndex.y = flatIndex % arrSize.y;
			flatIndex /= arrSize.y;
			_3dIndex.z = flatIndex;

			return _3dIndex;
		}
	}
	
	public class TerrainNode {
		public const int VOXEL_COUNT = 32;

		// TerrainNode has a fixed location after it is created
		public readonly int Lod;
		public readonly float3 Pos; // lower corner not center
		public readonly float Size;

		public float3 Center => Pos + Size/2;
		
		public TerrainNode[] Children = new TerrainNode[8];

		// Gameobjects always instanciated
		public GameObject Go;
		public GameObject SeamGo;
		public Mesh mesh = null;
		public Mesh SeamMesh = null;

		public Voxels Voxels = null;
		//public TerrainMesher.DualContouring.Data DC;

		public bool IsDestroyed => Go == null; // Node was destroyed and removed from the tree, should no longer be used (this can be observed true when nodes are destroyed while still being used by a job for ex.)
		public bool IsCreated => Voxels != null; // if false => this node was put into the tree, but does not have voxels or a mesh yet

		public static readonly int3[] ChildOctants = new int3[8] { int3(0,0,0), int3(1,0,0),  int3(0,1,0), int3(1,1,0),   int3(0,0,1), int3(1,0,1),  int3(0,1,1), int3(1,1,1) };
		public static readonly int3[] ChildDirs = new int3[8] { int3(-1,-1,-1), int3(+1,-1,-1),  int3(-1,+1,-1), int3(+1,+1,-1),   int3(-1,-1,+1), int3(+1,-1,+1),  int3(-1,+1,+1), int3(+1,+1,+1) };
		
		public TerrainNode (int lod, float3 pos, float size, GameObject TerrainNodePrefab, Transform goHierachy) {
			this.Lod = lod;
			this.Pos = pos;
			this.Size = size;
			
			Go = Object.Instantiate(TerrainNodePrefab, pos, Quaternion.identity, goHierachy);
			SeamGo = Go.transform.Find("Seam").gameObject;

			{
				mesh = new Mesh();
				mesh.name = "TerrainNode Mesh";
				mesh.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32;
				Go.GetComponent<MeshFilter>().mesh = mesh;
			}
			{
				SeamMesh = new Mesh();
				SeamMesh.name = "TerrainNode Seam Mesh";
				SeamMesh.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32;
				SeamGo.GetComponent<MeshFilter>().mesh = SeamMesh;
			}
		}

		public TerrainNode GetChild (int3 octant) { // [0,1]
			return Children[ octant.z * 4 + octant.y * 2 + octant.x ];
		}
		
		public static int GetChildrenMask (TerrainNode[] children) {
			int mask = 0;
			for (int i=0; i<8; i++) {
				if (children[i]?.IsCreated ?? false)
					mask |= 1 << i;
			}
			return mask;
		}
		public int GetChildrenMask () {
			return GetChildrenMask(Children);
		}
		
		public static void GetNodesInDir (TerrainNode n, int3 dir, HashSet<TerrainNode> touching) {
			int3 dirMask = abs(dir);

			for (int i=0; i<8; ++i) {
				if (all((ChildDirs[i] * dirMask) == dir)) { // child octant interfaces with the requested dir
					if (n.Children[i] != null) {
						GetNodesInDir(n.Children[i], dir, touching); // child exists -> recurse into child
					} else {
						touching.Add(n); // child does not exist -> this nodes space touches
					}
				}
			}
		} 
		public HashSet<TerrainNode> GetNodesInDir (int3 dir) { // get nodes that are touching the face, edge or corner specified by dir (this node included) ([-1, 0, +1] for each axis, edge has 3 non zero components, edge has 2, face has 1)
			var touching = new HashSet<TerrainNode>();
			GetNodesInDir(this, dir, touching);
			return touching;
		}
		public bool CanTouchInDir (int3 dir) {
			int3 dirMask = abs(dir);

			for (int i=0; i<8; ++i) {
				if (all((ChildDirs[i] * dirMask) == dir)) { // child octant interfaces with the requested dir
					if (Children[i] == null) {
						return true; // child does not exist -> this nodes space touches
					}
				}
			}

			return false;
		}

		public void Destroy () {
			
			if (SeamMesh != null)
				Object.Destroy(SeamMesh);
			SeamMesh = null;

			if (mesh != null)
				Object.Destroy(mesh);
			mesh = null;
			
			Object.Destroy(SeamGo);
			SeamGo = null;

			Object.Destroy(Go);
			Go = null;

			Voxels?.DecRef();
		}
		
		public void SetVoxels (Voxels voxels) {
			voxels.IncRef();

			if (this.Voxels != null)
				this.Voxels.DecRef();

			this.Voxels = voxels;
		}
	}
}