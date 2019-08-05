using System.Collections.Generic;
using Unity.Collections;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.Profiling;
using static Unity.Mathematics.math;
using static VoxelUtil;

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
	public const float VOXEL_SIZE = 2f; // Size of one voxel cell
	public const int VOXELS = 32; // Voxels per axis
	public const float SIZE = VOXELS * VOXEL_SIZE; // size of the chunk on one axis
		
	public readonly int3 Index; // unique index of chunk
		
	public float LatestDist = 0;

	public float3 Corner => (float3)Index * SIZE; // world position (lower corner of chunk)
	public float3 Center => ((float3)Index + 0.5f) * SIZE; // world position (center of chunk)
		
	public GameObject Go;
	public Mesh mesh;

	public NativeArray<Voxel> Voxels;
	//public TerrainMesher.Data MesherData;
	public NativeList<int> SurfaceEdgePositions;
	public NativeList<TerrainMesher.Edge> SurfaceEdges;
	public NativeArray<TerrainMesher.Cell> Cells;
	public NativeList<int> SurfaceCells;
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
		if (SurfaceEdgePositions.IsCreated)
			SurfaceEdgePositions.Dispose();
		if (SurfaceEdges.IsCreated)
			SurfaceEdges.Dispose();
		if (Cells.IsCreated)
			Cells.Dispose();
		if (SurfaceCells.IsCreated)
			SurfaceCells.Dispose();
	}
		
	///////////////
	const int _VOXELS_ = Chunk.VOXELS + 2; // Voxels actually generated per chunk
		
	// Dual contouring edges are the connections between voxels
	// If there is a ISO crossing on this line (one voxel < ISO other >= ISO) this edge is a surface edge and will generate a quad
	const int EDGES_PER_AXIS = _VOXELS_ * _VOXELS_ * (_VOXELS_ - 1); // How many edges in chunk (lines between voxels) per direction of edge (xyz)
	const int EDGES_TOTAL = EDGES_PER_AXIS * 3; // How many edges in chunk total
		
	// Dual contouring cells contain a vertex that is places based on the surface edges
	// the corners of the cell are voxels, the edges are the edges above
	const int CELLS = Chunk.VOXELS + 1;
	const int CELLS_TOTAL = CELLS * CELLS * CELLS;
		
	public void DrawGizmos () {
		Gizmos.color = Color.green;
		Gizmos.DrawWireCube(Center, (float3)SIZE);

		if (done && false) {
				
			Gizmos.color = Color.blue;
			for (int i=0; i<SurfaceCells.Length; ++i) {
				int index = SurfaceCells[i];
				var cell = Cells[index];
					
				int3 pos = flatTo3dIndex(index, CELLS);
					
				Gizmos.DrawWireCube((float3)pos * VOXEL_SIZE + Corner, (float3)VOXEL_SIZE);
				Gizmos.DrawWireCube(cell.vertex * VOXEL_SIZE + Corner, (float3)0.1f);
			}

			Gizmos.color = Color.red;
			for (int i=0; i<SurfaceEdgePositions.Length; ++i) {
				int index = SurfaceEdgePositions[i];
				var edge = SurfaceEdges[i];
			
				int axis = index / EDGES_PER_AXIS;
				index %= EDGES_PER_AXIS;
				
				int3 size = VOXELS + 2;
				size[axis] -= 1;
			
				int3 ia = flatTo3dIndex(index, size);
				int3 ib = ia;
				ib[axis] += 1;
				
				Gizmos.DrawLine(((float3)ia - 0.5f) * VOXEL_SIZE + Corner, ((float3)ib - 0.5f) * VOXEL_SIZE + Corner);
			}
			Gizmos.color = Color.black;
			for (int i=0; i<SurfaceEdgePositions.Length; ++i) {
				int index = SurfaceEdgePositions[i];
				var edge = SurfaceEdges[i];
				
				int axis = index / EDGES_PER_AXIS;
				index %= EDGES_PER_AXIS;
				
				int3 size = VOXELS + 2;
				size[axis] -= 1;
				
				int3 ia = flatTo3dIndex(index, size);
				int3 ib = ia;
				ib[axis] += 1;
				
				Gizmos.DrawRay(edge.pos * Chunk.VOXEL_SIZE + Corner, edge.normal * 0.4f * VOXEL_SIZE);
			}
		}
	}
}
