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
		public const float VOXEL_SIZE = 1f;
		public const int VOXELS = 32;
		public const float SIZE = VOXELS * VOXEL_SIZE;
		
		public readonly int3 Index; // lower corner not center
		
		public float LatestDist = 0;

		public float3 Corner => (float3)Index * SIZE;
		public float3 Center => ((float3)Index + 0.5f) * SIZE;
		
		public GameObject Go;
		public Mesh mesh;

		public NativeArray<Voxel> Voxels;
		//public TerrainMesher.Data MesherData;

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
		}

		public void DrawGizmos () {
			Gizmos.color = Color.green;
			Gizmos.DrawWireCube(Center, (float3)SIZE);
		}
	}
}