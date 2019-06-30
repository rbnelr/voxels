using static Unity.Mathematics.math;
using Unity.Mathematics;
using UnityEngine;
using Unity.Collections;
using System.Collections.Generic;
using UnityEngine.Profiling;

namespace OctreeGeneration {
	public struct OctreeCoord : System.IEquatable<OctreeCoord> { // Unique coordinate for each octree node position in the world
		// 0 is cube of size VoxelSize * ChunkVoxels
		// 1 is 2x the size aso.
		public readonly int		lod;
		// index of the cubes of this lod level
		// (0,0,0) is the one with low corner on the orign (the one that spans (0,0,0) to (size,size,size) where size = VoxelSize * ChunkVoxels * 2 ^ lod)
		// indecies are scaled by 2 to allow the root node to shift by half of it's size and still have a valid OctreeCoord
		public readonly int3	index;

		public OctreeCoord (int lod, int3 index) {
			this.lod = lod;
			this.index = index;
		}

		public static OctreeCoord FromWorldPos (float3 posWorld, int lod, float VoxelSize, int ChunkVoxels) {
			return new OctreeCoord(lod, (int3)floor(posWorld / ((ChunkVoxels << lod) * VoxelSize * 0.5f)));
		}
		public float3 ToWorldCube (float VoxelSize, int ChunkVoxels, out float size) { // center, size from OctreeCoord
			size = (ChunkVoxels << lod) * VoxelSize;
			return (float3)(index + 1) * size / 2;
		}
		public int3 ToWorldCubeInt (int ChunkVoxels, out int size) { // center, size from OctreeCoord
			size = (ChunkVoxels << lod);
			return (index + 1) * size / 2;
		}
			
		public override string ToString () {
			return string.Format("({0}, ({1}, {2}, {3}))", lod, index.x, index.y, index.z);
		}
		
		bool System.IEquatable<OctreeCoord>.Equals(OctreeCoord r) {
			return lod == r.lod && all(index == r.index);
		}
		public override int GetHashCode () {
			// https://stackoverflow.com/questions/1646807/quick-and-simple-hash-code-combinations
			int hash = 1009;
			hash = (hash * 9176) + lod;
			hash = (hash * 9176) + index.x;
			hash = (hash * 9176) + index.y;
			hash = (hash * 9176) + index.z;
			return hash;
		}
		
		public static bool operator== (OctreeCoord l, OctreeCoord r) {
			return l.lod == r.lod && all(l.index == r.index);
		}
		public static bool operator!= (OctreeCoord l, OctreeCoord r) {
			return !(l == r);
		}
		
		public override bool Equals (object obj) { // SLOW to call because it boxes OctreeCoord
			if (obj == null || GetType() != obj.GetType())
				return false;
		
			return this == (OctreeCoord)obj;
		}
	};

	public class TerrainNode {
		public readonly OctreeCoord coord; // TerrainNode has a fixed location after it is created
		
		// this represents the Terrain Octree, TerrainNodes are also cached, so TerrainNodes can exits outside the tree, they should be disabled in that case
		public TerrainNode Parent = null;
		public TerrainNode[] Children = null; // Can only have all children or none, this way there are no holes and no overlaps in the world, We disable this node and enable the child nodes when all of them are done with generating their mesh

		public static readonly int3[] ChildOrder = new int3[8] { int3(0,0,0), int3(1,0,0),  int3(0,1,0), int3(1,1,0),   int3(0,0,1), int3(1,0,1),  int3(0,1,1), int3(1,1,1) };
		public TerrainNode GetChild (int3 octant) { // [0,1]
			if (Children == null)
				return null;
			return Children[ octant.z * 4 + octant.y * 2 + octant.x ];
		}

		public bool IsLeaf => Children == null;
		public bool InTree;
		
		public GameObject go;
		public Mesh mesh = null;

		public GameObject seamGo;
		public Mesh seamMesh = null;

		public bool IsDestroyed => go == null;

		public Voxels voxels;
		
		public bool needsVoxelize = true;
		public bool needsRemesh = true;

		public float latestDistToPlayer;

		public DualContouring.Cell[,,] DCCells;

		public TerrainNode (OctreeCoord coord, float3 pos, GameObject TerrainNodePrefab, Transform parent) {
			this.coord = coord;
			
			go = Object.Instantiate(TerrainNodePrefab, pos, Quaternion.identity, parent);
			seamGo = go.transform.Find("Seam").gameObject;
		}

		public void Destroy () {

			if (mesh != null)
				Object.Destroy(mesh);
			mesh = null;

			if (seamMesh != null)
				Object.Destroy(seamMesh);
			seamMesh = null;
			
			Object.Destroy(seamMesh);
			seamMesh = null;

			Object.Destroy(go);
			go = null;

			voxels?.Dispose();
		}
		
		public void AssignVoxels (Voxels voxels) {
			voxels.Use();

			if (this.voxels != null)
				this.voxels.Dispose();
			this.voxels = voxels;
			needsVoxelize = false;
		}
		public void AssignMesh (
					NativeList<float3> vertices,	ref List<Vector3> verticesBuf,
					NativeList<float3> normals,		ref List<Vector3> normalsBuf,
					NativeList<float2> uvs,			ref List<Vector2> uvBuf,
					NativeList<Color> colors,		ref List<Color> colorsBuf,
					NativeList<int> triangles,		ref List<int> trianglesBuf
				) {

			if (mesh == null) {
				mesh = new Mesh();
				mesh.name = "TerrainChunk Mesh";
				mesh.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32;
				go.GetComponent<MeshFilter>().mesh = mesh;
			}

			Profiler.BeginSample("TerrainChunk.AssignMesh");
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

			needsRemesh = false;
		}
		
		bool complete; // we have a mesh or all our children are complete
		bool visible => go.activeInHierarchy;

		public static void UpdateTreeVisibility (TerrainNode root) {
			updateCompletenessRecurse(root);
			updateVisibilityRecurse(root);
		}
		public static void updateCompletenessRecurse (TerrainNode node) {
			node.complete = node.mesh != null;
			if (node.Children != null)
				for (int i=0; i<8; ++i) {
					updateCompletenessRecurse(node.Children[i]);
					node.complete = node.complete || node.Children[i].complete;
				}
		}
		public static void updateVisibilityRecurse (TerrainNode node, bool parentVisible=false) {
			bool visible = isVisibile(node, parentVisible);
			node.go.SetActive(visible);

			if (node.Children != null)
				for (int i=0; i<8; ++i)
					updateVisibilityRecurse(node.Children[i], visible);
		}
		public static bool isVisibile (TerrainNode node, bool parentVisible) {
			if (parentVisible)
				return false; // our parent is visible, so we cannot be visible

			if (node.Children == null)
				return true; // our parent is invisible and we are a leaf node, so we need to be visible

			for (int i=0; i<8; ++i) {
				if (!node.Children[i].complete)
					return true; // one of our children is not complete, so we need to be visible (and our children invisible)
			}

			return false; // all of our children are complete, so we need to be invisible (and our children visible)
		}
	}
}