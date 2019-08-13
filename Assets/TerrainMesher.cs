using System.Collections.Generic;
using Unity.Burst;
using Unity.Collections;
using Unity.Jobs;
using Unity.Mathematics;
using static Unity.Mathematics.math;
using UnityEngine;
using UnityEngine.Profiling;
using static VoxelUtil;

public class MeshData {
	public NativeList<float3> vertices;
	public NativeList<float3> normals;
	public NativeList<float2> uv;
	public NativeList<Color>  colors;
	public NativeList<int>    triangles;
			
	static List<Vector3>	verticesBuf  = new List<Vector3>();
	static List<Vector3>	normalsBuf   = new List<Vector3>();
	static List<Vector2>	uvBuf        = new List<Vector2>();
	static List<Color>		colorsBuf    = new List<Color>();
	static List<int>		trianglesBuf = new List<int>();

	public MeshData () {
		int ArraySize = Chunk.VOXELS + 1;
		int vertexAlloc = ArraySize * ArraySize * 6;
				
		vertices  = new NativeList<float3> (vertexAlloc, Allocator.Persistent);
		normals   = new NativeList<float3> (vertexAlloc, Allocator.Persistent);
		uv        = new NativeList<float2> (vertexAlloc, Allocator.Persistent);
		colors    = new NativeList<Color>  (vertexAlloc, Allocator.Persistent);
		triangles = new NativeList<int>	   (vertexAlloc, Allocator.Persistent);
	}

	public void Dispose () {
		if (vertices  .IsCreated) vertices  .Dispose();
		if (normals   .IsCreated) normals   .Dispose();
		if (uv        .IsCreated) uv        .Dispose();
		if (colors    .IsCreated) colors    .Dispose();
		if (triangles .IsCreated) triangles .Dispose();
	}
		
	public void SetMesh (Chunk chunk, Mesh mesh) {
		
		Profiler.BeginSample("TerrainNode.AssignMesh");
		mesh.Clear();
			Profiler.BeginSample("vertices");
				mesh.SetVerticesNative(vertices, ref verticesBuf);
			Profiler.EndSample();
			Profiler.BeginSample("normals");
				mesh.SetNormalsNative(normals, ref normalsBuf);
			Profiler.EndSample();
			Profiler.BeginSample("uv");
				mesh.SetUvsNative(0, uv, ref uvBuf);
			Profiler.EndSample();
			Profiler.BeginSample("colors");
				mesh.SetColorsNative(colors, ref colorsBuf);
			Profiler.EndSample();
			Profiler.BeginSample("triangles");
				mesh.SetTrianglesNative(triangles, 0, ref trianglesBuf);
			Profiler.EndSample();
		Profiler.EndSample();
		
		chunk.MeshCollider.sharedMesh = mesh;
	}
}

public class TerrainMesher : MonoBehaviour {
		
	public float DCIterStrength = 1f;
	public int DCMaxIterations = 5;

	const float ISO = 0.0f; // TODO: make variable for testing?
		
	const int VOXELS = Chunk.VOXELS + 2; // Voxels actually generated per chunk
		
	// Dual contouring edges are the connections between voxels
	// If there is a ISO crossing on this line (one voxel < ISO other >= ISO) this edge is a surface edge and will generate a quad
	const int EDGES_PER_AXIS = VOXELS * VOXELS * (VOXELS - 1); // How many edges in chunk (lines between voxels) per direction of edge (xyz)
	const int EDGES_TOTAL = EDGES_PER_AXIS * 3; // How many edges in chunk total
		
	// Dual contouring cells contain a vertex that is places based on the surface edges
	// the corners of the cell are voxels, the edges are the edges above
	const int CELLS = Chunk.VOXELS + 1;
	const int CELLS_TOTAL = CELLS * CELLS * CELLS;
		
	public Job StartJob (Chunk c, TerrainGenerator.Job terrGen) {
		var j = new Job { chunk=c };
			
		c.SurfaceEdgePositions = new NativeList<int>(Chunk.VOXELS * Chunk.VOXELS, Allocator.Persistent);
		c.SurfaceEdges = new NativeList<Edge>(Chunk.VOXELS * Chunk.VOXELS, Allocator.Persistent);
		c.SurfaceCells = new NativeList<int>(Chunk.VOXELS * Chunk.VOXELS, Allocator.Persistent);
		c.Cells = new NativeArray<Cell>(CELLS_TOTAL, Allocator.Persistent, NativeArrayOptions.ClearMemory);
		j.MeshData = new MeshData();

		var findSurface = new FindSurfaceEdgesJob {
			Voxels = c.Voxels,
		};
		var calcEdges = new CalcEdgesJob {
			Voxels = c.Voxels,
			SurfaceEdgePositions = c.SurfaceEdgePositions,

			SurfaceEdges = c.SurfaceEdges,
			SurfaceCells = c.SurfaceCells,
			Cells = c.Cells,
		};
		var calcVertices = new CalcVerticesJob {
			DCIterStrength = DCIterStrength,
			DCMaxIterations = DCMaxIterations,
			SurfaceEdges = c.SurfaceEdges,
			SurfaceCells = c.SurfaceCells,

			Cells = c.Cells,
		};
		var genMesh = new GenerateMeshJob {
			SurfaceEdgePositions = c.SurfaceEdgePositions,
			SurfaceEdges = c.SurfaceEdges,
			Cells = c.Cells,
				
			vertices  = j.MeshData.vertices ,	
			normals	  = j.MeshData.normals	,	
			uv		  = j.MeshData.uv		,	
			colors	  = j.MeshData.colors	,	
			triangles = j.MeshData.triangles,	
		};
			
		var findSurfaceH = findSurface  .ScheduleAppend(c.SurfaceEdgePositions, EDGES_TOTAL, 64, terrGen?.Handle ?? default);
		var calcEdgesH	 = calcEdges    .Schedule(findSurfaceH);
		var calcVertsH	 = calcVertices .Schedule(c.SurfaceCells, 64, calcEdgesH);
		j.FinalJob		 = genMesh      .Schedule(calcVertsH);
		return j;
	}
		
	public class Job {
		public Chunk chunk;
		public JobHandle FinalJob;
		public MeshData MeshData;
			
		public bool IsCompleted => FinalJob.IsCompleted;
		public void Complete () {
			FinalJob.Complete();
			if (!chunk.IsDestroyed)
				MeshData.SetMesh(chunk, chunk.mesh);
			MeshData.Dispose();
		}
	}

	public struct Edge {
		public bool flip; // needs to be flipped to get correct front facing dir
		public float3 pos; // position of approximated iso crossing
		public float3 normal; // normalized gradient at approximated iso crossing
	}
		
	public unsafe struct Cell {
		public float3 vertex;

		public int surfaceEdges;
		public fixed int edges[12];
	}

	[BurstCompile]
	public struct FindSurfaceEdgesJob : IJobParallelForFilter {
		[ReadOnly] public NativeArray<Voxel> Voxels;
			
		private static readonly int[] Offsets = new int[] {
			1, (Chunk.VOXELS + 2), (Chunk.VOXELS + 2) * (Chunk.VOXELS + 2)
		};

		public bool Execute (int edgePosFlat) {
			#if false
			int axis = edgePosFlat / EDGES_PER_AXIS;
			edgePosFlat %= EDGES_PER_AXIS;
				
			int3 size = VOXELS;
			size[axis] -= 1;

			int3 ia = flatTo3dIndex(edgePosFlat, size);
			int3 ib = ia;
			ib[axis] += 1;

			float a = Voxels[_3dToFlatIndex(ia, Chunk.VOXELS + 2)].value;
			float b = Voxels[_3dToFlatIndex(ib, Chunk.VOXELS + 2)].value;
				
			return a < ISO != b < ISO;
			#else
			int axis = edgePosFlat / EDGES_PER_AXIS;
			edgePosFlat %= EDGES_PER_AXIS;
				
			int3 size = VOXELS;
			size[axis] -= 1;

			int3 ia = flatTo3dIndex(edgePosFlat, size);
				
			int indxA = _3dToFlatIndex(ia, Chunk.VOXELS + 2);
			int indxB = indxA + Offsets[axis];

			float a = Voxels[indxA].value;
			float b = Voxels[indxB].value;
				
			return a < ISO != b < ISO;
			#endif
		}
	}
		
	[BurstCompile]
	public struct CalcEdgesJob : IJob {
		[ReadOnly] public NativeArray<Voxel> Voxels;
		[ReadOnly] public NativeList<int> SurfaceEdgePositions;
		public NativeList<Edge> SurfaceEdges;
		public NativeList<int> SurfaceCells;
		public NativeArray<Cell> Cells;
			
		public unsafe void Execute () {
			for (int i=0; i<SurfaceEdgePositions.Length; ++i) {
				int edgePosFlat = SurfaceEdgePositions[i];
					
				int axis = edgePosFlat / EDGES_PER_AXIS;
				edgePosFlat %= EDGES_PER_AXIS;
				
				int3 size = VOXELS;
				size[axis] -= 1;

				{
					int3 ia = flatTo3dIndex(edgePosFlat, size);
					int3 ib = ia;
					ib[axis] += 1;

					var a = Voxels[_3dToFlatIndex(ia, Chunk.VOXELS + 2)];
					var b = Voxels[_3dToFlatIndex(ib, Chunk.VOXELS + 2)];
				
					float t = unlerp(a.value, b.value, ISO); // a.value != b.value because then the edge would no be a surface edge

					// flip faces for y axis, I can't figure out where i made an assumption of a right handed coord system, but for some reason the y edge faces are the wrong way around
					bool flip = axis == 1;

					Edge e;
					e.flip = (a.value > b.value) ^ flip; 
					e.pos = lerp((float3)ia, (float3)ib, t) - 0.5f;
					e.normal = normalizesafe(lerp(a.gradient, b.gradient, t));
						
					SurfaceEdges.Add(e);
				}
		
				int3 edgePos = flatTo3dIndex(edgePosFlat, size);
					
				int j = axis > 0 ? 0 : 1;
				int k = axis < 2 ? 2 : 1;

				for (int cell_i=0; cell_i<4; ++cell_i) {
					int3 cellPos = edgePos;
					cellPos[j] -= cell_i & 1;
					cellPos[k] -= cell_i >> 1;
					if (cellPos[j] >= 0 && cellPos[k] >= 0 && cellPos[j] < CELLS && cellPos[k] < CELLS) {
						int cellPosFlat = _3dToFlatIndex(cellPos, CELLS);
						var cell = Cells[cellPosFlat];

						if (cell.surfaceEdges == 0)
							SurfaceCells.Add(cellPosFlat);

						cell.edges[cell.surfaceEdges++] = i;

						Cells[cellPosFlat] = cell;
					}
				}
			}
		}
	}

	[BurstCompile]
	public struct CalcVerticesJob : IJobParallelForDefer {
		[ReadOnly] public float DCIterStrength;
		[ReadOnly] public int DCMaxIterations;
		[ReadOnly] public NativeList<Edge> SurfaceEdges;
		[ReadOnly] public NativeList<int> SurfaceCells;
		public NativeArray<Cell> Cells;
		
		unsafe float3 MassPoint (Cell cell, out float3 normal) {
			float3 avgPos = 0;
			float3 avgNormal = 0;
			
			for (int i=0; i<cell.surfaceEdges; ++i) {
				var edgeIndex = cell.edges[i];
				var edge = SurfaceEdges[edgeIndex];
					
				avgPos += edge.pos;
				avgNormal += edge.normal;
			}

			avgPos /= cell.surfaceEdges;
			avgNormal /= cell.surfaceEdges;

			normal = avgNormal;
			return avgPos;
		}

		unsafe float3 DualContourIterative (Cell cell, int3 cellPos) {
			// Instead of using a QEF solver, use a iterative method
			// This is my approach of solving this, this is basicly gradient descent which is used in machine learning
			// We know we want the best fit point based on a set of points with normals (called hermite?) which can be thought of as defining a plane
			// There should (usually) be a point somewhere (maybe outside the cell) that is the global minimum of distances to these planes
			// It seems Augusto Schmitz came up with something similar http://www.inf.ufrgs.br/~comba/papers/thesis/diss-leonardo.pdf - called Schimtz Particle Method by mattbick2003 - https://www.reddit.com/r/Unity3D/comments/bw6x1l/an_update_on_the_job_system_dual_contouring/
			
			float3 particle = MassPoint(cell, out float3 normal);
			//float3 particle = (float3)cellPos + 0.5f;
			//return particle;

			//cell.normal = normal;
			
			int iter = 0;
			while (iter++ < DCMaxIterations) {
				float3 sumForce = 0;
				
				for (int i=0; i<cell.surfaceEdges; ++i) {
					var edgeIndex = cell.edges[i];
					var edge = SurfaceEdges[edgeIndex];
						
					var posRel = particle - edge.pos;
							
					float signedDistance = dot(edge.normal, posRel);
					float signedSqrError = signedDistance * abs(signedDistance);
				
					float3 force = signedSqrError * -edge.normal;
				
					sumForce += force;
				}
				
				sumForce /= cell.surfaceEdges;
				
				particle += sumForce * DCIterStrength;
				
				particle = clamp(particle, (float3)cellPos - 0.5f, (float3)cellPos + 0.5f);
			}

			return particle;
		}
			
		public void Execute (int i) {
			int indexFlat = SurfaceCells[i];
					
			int3 cellPos = flatTo3dIndex(indexFlat, CELLS);
		
			var cell = Cells[indexFlat];
			cell.vertex = DualContourIterative(cell, cellPos);
			Cells[indexFlat] = cell;
		}
	}

	[BurstCompile]
	public struct GenerateMeshJob : IJob {
		[ReadOnly] public NativeList<int> SurfaceEdgePositions;
		[ReadOnly] public NativeList<Edge> SurfaceEdges;
		[ReadOnly] public NativeArray<Cell> Cells;
			
		[WriteOnly] public NativeList<float3> vertices;
		[WriteOnly] public NativeList<float3> normals;
		[WriteOnly] public NativeList<float2> uv;
		[WriteOnly] public NativeList<Color>  colors;
		public NativeList<int>    triangles;
			
		Cell GetCell (int3 edgePos, int j, int k, int cell_i) {
			int3 cellPos = edgePos;
			cellPos[j] -= cell_i & 1;
			cellPos[k] -= cell_i >> 1;

			int cellPosFlat = _3dToFlatIndex(cellPos, CELLS);

			return Cells[cellPosFlat];
		}
			
		public void EmitTriangle (Cell a, Cell b, Cell c, Color col) {
			if (all(a.vertex == b.vertex) || all(a.vertex == c.vertex))
				return; // degenerate triangle
		
			vertices.Add(a.vertex * Chunk.VOXEL_SIZE);
			vertices.Add(b.vertex * Chunk.VOXEL_SIZE);
			vertices.Add(c.vertex * Chunk.VOXEL_SIZE);
		
			var flatNormal = normalize(cross(b.vertex - a.vertex, c.vertex - a.vertex));
		
			//normals.Add(lerp(flatNormal, a.normal, NormalSmooth));
			//normals.Add(lerp(flatNormal, b.normal, NormalSmooth));
			//normals.Add(lerp(flatNormal, c.normal, NormalSmooth));
		
			normals.Add(flatNormal);
			normals.Add(flatNormal);
			normals.Add(flatNormal);
		
			uv.Add(float2(0.5f));
			uv.Add(float2(0.5f));
			uv.Add(float2(0.5f));
		
			colors.Add(col);
			colors.Add(col);
			colors.Add(col);
		
			int indx = triangles.Length;
			triangles.Add(indx++);
			triangles.Add(indx++);
			triangles.Add(indx++);
		}

		public void Execute () {
			for (int i=0; i<SurfaceEdgePositions.Length; ++i) {
				int edgePosFlat = SurfaceEdgePositions[i];
				var edge = SurfaceEdges[i];
					
				int axis = edgePosFlat / EDGES_PER_AXIS;
				edgePosFlat %= EDGES_PER_AXIS;
				
				int3 size = VOXELS;
				size[axis] -= 1;
		
				int3 edgePos = flatTo3dIndex(edgePosFlat, size);
					
				int j = axis > 0 ? 0 : 1;
				int k = axis < 2 ? 2 : 1;

				if (edgePos[j] > 0 && edgePos[k] > 0 && edgePos[j] < CELLS && edgePos[k] < CELLS) {
					var a = GetCell(edgePos, j,k, 0);
					var b = GetCell(edgePos, j,k, 1);
					var c = GetCell(edgePos, j,k, 2);
					var d = GetCell(edgePos, j,k, 3);
						
					if (edge.flip) {
						EmitTriangle(b, a, d, Color.white);
						EmitTriangle(d, a, c, Color.white);
					} else {
						EmitTriangle(a, b, c, Color.white);
						EmitTriangle(c, b, d, Color.white);
					}
				}
			}
		}
	}
}
