using System.Collections.Generic;
using Unity.Collections;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.Profiling;
using static Unity.Mathematics.math;
using static OctreeGeneration.VoxelUtil;

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

	public struct Voxel {
		public float value;
		public float3 gradient;
		
		public static Voxel Lerp (Voxel a, Voxel b, float t) {
			a.value = a.value + (b.value - a.value) * t;
			a.gradient = a.gradient + (b.gradient - a.gradient) * t;
			return a;
		}

		public override string ToString () {
			return value.ToString();
		}
	}

	public class Chunk {
		public const float VOXEL_SIZE = 1f; // Size of one voxel cell
		public const int VOXELS = 8; // Voxels per axis
		public const float SIZE = VOXELS * VOXEL_SIZE; // size of the chunk on one axis
		
		public readonly int3 Index; // unique index of chunk
		
		public float LatestDist = 0;

		public float3 Corner => (float3)Index * SIZE; // world position (lower corner of chunk)
		public float3 Center => ((float3)Index + 0.5f) * SIZE; // world position (center of chunk)
		
		public GameObject Go;
		public Mesh mesh;

		public NativeArray<Voxel> Voxels;
		//public TerrainMesher.Data MesherData;
		public NativeList<int> SurfaceEdges;
		public bool done = false; //

		public bool IsDestroyed => Go == null;
		
		public Chunk (int3 index, GameObject Prefab, Transform goHierachy) {
			this.Index = index;
			
			Go = Object.Instantiate(Prefab, Corner, Quaternion.identity, goHierachy);

			{
				mesh = new Mesh();
				mesh.name = "Chunk Mesh";
				mesh.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32;
				Go.GetComponent<MeshFilter>().mesh = mesh;
			}
		}

		public void Dispose () {
			
			if (mesh != null)
				Object.Destroy(mesh);
			mesh = null;
			
			Object.Destroy(Go);
			Go = null;

			if (Voxels.IsCreated)
				Voxels.Dispose();
			if (SurfaceEdges.IsCreated)
				SurfaceEdges.Dispose();
		}
		
		const int EDGES_PER_AXIS = (Chunk.VOXELS + 2) * (Chunk.VOXELS + 2) * (Chunk.VOXELS + 1);
		const int EDGES_TOTAL = EDGES_PER_AXIS * 3;
		public void DrawGizmos () {
			Gizmos.color = Color.green;
			Gizmos.DrawWireCube(Center, (float3)SIZE);

			if (done) {
				Gizmos.color = Color.red;
				for (int i=0; i<SurfaceEdges.Length; ++i) {
					int index = SurfaceEdges[i];
			
					int axis = index / EDGES_PER_AXIS;
					index %= EDGES_PER_AXIS;
				
					int3 size = VOXELS + 2;
					size[axis] -= 1;
			
					int3 ia = flatTo3dIndex(index, size);
					int3 ib = ia;
					ib[axis] += 1;
				
					Gizmos.DrawLine(((float3)ia - 0.5f) * VOXEL_SIZE + Corner, ((float3)ib - 0.5f) * VOXEL_SIZE + Corner);
				}
			}
		}
	}
}