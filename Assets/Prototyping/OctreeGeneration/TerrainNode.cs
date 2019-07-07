using static Unity.Mathematics.math;
using Unity.Mathematics;
using UnityEngine;
using Unity.Collections;
using System.Collections.Generic;
using UnityEngine.Profiling;

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
		 // TerrainNode has a fixed location after it is created
		public readonly int lod;
		public readonly float3 pos; // lower corner not center
		public readonly float size;
		
		// this represents the Terrain Octree, TerrainNodes are also cached, so TerrainNodes can exits outside the tree, they should be disabled in that case
		public TerrainNode Parent = null;
		public TerrainNode[] Children = new TerrainNode[8];

		public static readonly int3[] ChildOrder = new int3[8] { int3(0,0,0), int3(1,0,0),  int3(0,1,0), int3(1,1,0),   int3(0,0,1), int3(1,0,1),  int3(0,1,1), int3(1,1,1) };
		public TerrainNode GetChild (int3 octant) { // [0,1]
			return Children[ octant.z * 4 + octant.y * 2 + octant.x ];
		}
		
		public GameObject go;
		public Mesh mesh = null;

		public GameObject seamGo;
		public Mesh seamMesh = null;

		public bool IsDestroyed => go == null;

		public Voxels voxels;
		
		public bool needsVoxelize = true;
		public bool needsRemesh = true;
		public bool needsSeamRemesh = true;

		public float latestDistToPlayer;

		//public DualContouring.Cell[,,] DCCells;

		public TerrainNode (int lod, float3 pos, float size, GameObject TerrainNodePrefab, Transform parent) {
			this.lod = lod;
			this.pos = pos;
			this.size = size;
			
			go = Object.Instantiate(TerrainNodePrefab, pos, Quaternion.identity, parent);
			seamGo = go.transform.Find("Seam").gameObject;
		}

		public void Destroy () {
			
			if (seamMesh != null)
				Object.Destroy(seamMesh);
			seamMesh = null;

			if (mesh != null)
				Object.Destroy(mesh);
			mesh = null;
			
			Object.Destroy(seamGo);
			seamGo = null;

			Object.Destroy(go);
			go = null;

			voxels?.DecRef();
		}
		
		public void AssignVoxels (Voxels voxels) {
			voxels.IncRef();

			if (this.voxels != null)
				this.voxels.DecRef();

			this.voxels = voxels;

			needsVoxelize = false;
		}
		
		static List<Vector3>	verticesBuf  = new List<Vector3>();
		static List<Vector3>	normalsBuf   = new List<Vector3>();
		static List<Vector2>	uvBuf        = new List<Vector2>();
		static List<Color>		colorsBuf    = new List<Color>();
		static List<int>		trianglesBuf = new List<int>();

		static void _AssignMesh (GameObject go, Mesh mesh, NativeList<float3> vertices, NativeList<float3> normals, NativeList<float2> uvs, NativeList<Color> colors, NativeList<int> triangles) {

			if (mesh == null) {
				mesh = new Mesh();
				mesh.name = "TerrainNode Mesh";
				mesh.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32;
				go.GetComponent<MeshFilter>().mesh = mesh;
			}

			Profiler.BeginSample("TerrainNode.AssignMesh");
			mesh.Clear();
				Profiler.BeginSample("vertices");
					mesh.SetVerticesNative(vertices, ref verticesBuf);
				Profiler.EndSample();
				Profiler.BeginSample("normals");
					mesh.SetNormalsNative(normals, ref normalsBuf);
				Profiler.EndSample();
				Profiler.BeginSample("uv");
					mesh.SetUvsNative(0, uvs, ref uvBuf);
				Profiler.EndSample();
				Profiler.BeginSample("colors");
					mesh.SetColorsNative(colors, ref colorsBuf);
				Profiler.EndSample();
				Profiler.BeginSample("triangles");
					mesh.SetTrianglesNative(triangles, 0, ref trianglesBuf);
				Profiler.EndSample();
			Profiler.EndSample();
		}
		
		public void AssignMesh (NativeList<float3> vertices, NativeList<float3> normals, NativeList<float2> uvs, NativeList<Color> colors, NativeList<int> triangles) {
			_AssignMesh(go, mesh, vertices, normals, uvs, colors, triangles);

			needsRemesh = false;
		}
		public void AssignSeamMesh (NativeList<float3> vertices, NativeList<float3> normals, NativeList<float2> uvs, NativeList<Color> colors, NativeList<int> triangles) {
			_AssignMesh(seamGo, seamMesh, vertices, normals, uvs, colors, triangles);

			needsSeamRemesh = false;
		}
	}
}