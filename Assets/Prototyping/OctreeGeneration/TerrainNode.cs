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
		
		// this represents the Terrain Octree, TerrainNodes are also cached, so TerrainNodes can exits outside the tree, they should be disabled in that case
		public TerrainNode[] Children = new TerrainNode[8];

		public static readonly int3[] ChildOctants = new int3[8] { int3(0,0,0), int3(1,0,0),  int3(0,1,0), int3(1,1,0),   int3(0,0,1), int3(1,0,1),  int3(0,1,1), int3(1,1,1) };
		public static readonly int3[] ChildDirs = new int3[8] { int3(-1,-1,-1), int3(+1,-1,-1),  int3(-1,+1,-1), int3(+1,+1,-1),   int3(-1,-1,+1), int3(+1,-1,+1),  int3(-1,+1,+1), int3(+1,+1,+1) };

		public TerrainNode GetChild (int3 octant) { // [0,1]
			return Children[ octant.z * 4 + octant.y * 2 + octant.x ];
		}
		
		public GameObject Go;
		public Mesh mesh = null;

		public GameObject SeamGo;
		public Mesh SeamMesh = null;

		public bool IsDestroyed => Go == null;

		public Voxels Voxels;
		public Cells  Cells;
		
		public static int GetChildrenMask (TerrainNode[] children) {
			int mask = 0;
			for (int i=0; i<8; i++)
				if (children[i] != null)
					mask |= 1 << i;
			return mask;
		}
		public int GetChildrenMask () {
			return GetChildrenMask(Children);
		}
		
		public static void GetNodesInDir (TerrainNode n, int3 dir, HashSet<TerrainNode> touching) {
			touching.Add(n);

			int3 dirMask = abs(dir);

			for (int i=0; i<8; ++i) {
				if (n.Children[i] != null && all((ChildDirs[i] * dirMask) == dir)) // any children in the dir
					GetNodesInDir(n.Children[i], dir, touching);
			}
		} 
		public HashSet<TerrainNode> GetNodesInDir (int3 dir) { // get nodes that are touching the face, edge or corner specified by dir (this node included) ([-1, 0, +1] for each axis, edge has 3 non zero components, edge has 2, face has 1)
			var touching = new HashSet<TerrainNode>();
			GetNodesInDir(this, dir, touching);
			return touching;
		} 

		public TerrainNode (int lod, float3 pos, float size, GameObject TerrainNodePrefab, Transform goHierachy) {
			this.Lod = lod;
			this.Pos = pos;
			this.Size = size;
			
			Go = Object.Instantiate(TerrainNodePrefab, pos, Quaternion.identity, goHierachy);
			SeamGo = Go.transform.Find("Seam").gameObject;
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
		
		static List<Vector3>	verticesBuf  = new List<Vector3>();
		static List<Vector3>	normalsBuf   = new List<Vector3>();
		static List<Vector2>	uvBuf        = new List<Vector2>();
		static List<Color>		colorsBuf    = new List<Color>();
		static List<int>		trianglesBuf = new List<int>();

		public static Mesh SetMesh (Mesh mesh, GameObject go, ref TerrainMesher.Mesh mesherMesh) {

			if (mesh == null) {
				mesh = new Mesh();
				mesh.name = "TerrainNode Mesh";
				mesh.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32;
				go.GetComponent<MeshFilter>().mesh = mesh;
			}

			Profiler.BeginSample("TerrainNode.AssignMesh");
			mesh.Clear();
				Profiler.BeginSample("vertices");
					mesh.SetVerticesNative(mesherMesh.vertices, ref verticesBuf);
				Profiler.EndSample();
				Profiler.BeginSample("normals");
					mesh.SetNormalsNative(mesherMesh.normals, ref normalsBuf);
				Profiler.EndSample();
				Profiler.BeginSample("uv");
					mesh.SetUvsNative(0, mesherMesh.uv, ref uvBuf);
				Profiler.EndSample();
				Profiler.BeginSample("colors");
					mesh.SetColorsNative(mesherMesh.colors, ref colorsBuf);
				Profiler.EndSample();
				Profiler.BeginSample("triangles");
					mesh.SetTrianglesNative(mesherMesh.triangles, 0, ref trianglesBuf);
				Profiler.EndSample();
			Profiler.EndSample();

			return mesh;
		}
	}
}