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

	/// <summary>
	/// Used to uniquely identify octree nodes
	/// </summary>
	public struct OctreeCoord : System.IEquatable<OctreeCoord> {
		public readonly int		lod;
		public readonly int3	pos; // in voxel space, ie. worldPos / VoxelSize
		
		public int3	size => TerrainNode.VOXEL_COUNT << lod; // in voxel space, ie. worldSize / VoxelSize

		public OctreeCoord (int lod, int3 pos) {
			this.lod = lod;
			this.pos = pos;
		}
			
		public override string ToString () {
			return string.Format("({0}, ({1}, {2}, {3}))", lod, pos.x, pos.y, pos.z);
		}
		
		// Needed to be used as Hashmap key
		bool System.IEquatable<OctreeCoord>.Equals(OctreeCoord r) {
			return lod == r.lod && all(pos == r.pos);
		}
		public override int GetHashCode () {
			// https://stackoverflow.com/questions/1646807/quick-and-simple-hash-code-combinations
			int h = 1009;
			h = (h * 9176) + lod;
			h = (h * 9176) + pos.GetHashCode();
			return h;
		}
		
		public static bool operator== (OctreeCoord l, OctreeCoord r) {
			return l.lod == r.lod && all(l.pos == r.pos);
		}
		public static bool operator!= (OctreeCoord l, OctreeCoord r) {
			return l.lod != r.lod || any(l.pos != r.pos);
		}
		
		public override bool Equals (object obj) { // SLOW to call because it boxes OctreeCoord
			if (obj == null || GetType() != obj.GetType())
				return false;
		
			return this == (OctreeCoord)obj;
		}
	};
	
	/// <summary>
	/// A Node in the terrain octree
	/// Its location is immutable
	/// Exists inside the octree, never outside
	/// Is incomplete when created, since voxel and mesh creation are async, always gets created in the tree when the async ops are sheduled, since neighbourship queries are needed for the seam creation, which are really hard without a tree
	/// Actual game object gets only created when as soon as the mesh is done
	/// </summary>
	public class TerrainNode {
		public const int VOXEL_COUNT = 32;

		 // TerrainNode has a fixed location after it is created
		public readonly OctreeCoord Coord;
		public readonly float3 Pos; // lower corner not center
		public readonly float Size;

		public int Lod => Coord.lod;
		public float3 Center => Pos + Size/2;
		
		public TerrainNode[] Children = new TerrainNode[8];


		public GameObject Go;
		public Mesh mesh = null;

		public GameObject SeamGo;
		public Mesh SeamMesh = null;

		public bool IsDestroyed => Go == null;

		public Voxels Voxels;
		public Cells  Cells;
		
		public static readonly int3[] ChildOctants = new int3[8] { int3(0,0,0), int3(1,0,0),  int3(0,1,0), int3(1,1,0),   int3(0,0,1), int3(1,0,1),  int3(0,1,1), int3(1,1,1) };
		public static readonly int3[] ChildDirs = new int3[8] { int3(-1,-1,-1), int3(+1,-1,-1),  int3(-1,+1,-1), int3(+1,+1,-1),   int3(-1,-1,+1), int3(+1,-1,+1),  int3(-1,+1,+1), int3(+1,+1,+1) };
		
		public TerrainNode (OctreeCoord coord, float VoxelSize) {
			this.Lod = lod;
			this.Pos = pos;
			this.Size = size;
			
			Go = Object.Instantiate(TerrainNodePrefab, pos, Quaternion.identity, goHierachy);
			SeamGo = Go.transform.Find("Seam").gameObject;
		}

		public TerrainNode GetChild (int3 octant) { // [0,1]
			return Children[ octant.z * 4 + octant.y * 2 + octant.x ];
		}
		
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