using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using Unity.Burst;
using Unity.Collections;
using Unity.Jobs;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.Profiling;
using static OctreeGeneration.VoxelUtil;
using static Unity.Mathematics.math;

namespace OctreeGeneration {
	
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
			vertices  .Dispose();
			normals   .Dispose();
			uv        .Dispose();
			colors    .Dispose();
			triangles .Dispose();
		}
			
		public void SetMesh (Mesh mesh) {
		
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
			var j = new Job();
			
			c.SurfaceEdges = new NativeList<int>(EDGES_TOTAL, Allocator.Persistent);

			var findSurface = new FindSurfaceEdgesJob {
				Voxels = c.Voxels
			};
			
			j.FindActive = findSurface.ScheduleAppend(c.SurfaceEdges, EDGES_TOTAL, VOXELS * VOXELS, terrGen?.Handle ?? default);
			return j;
		}
		
		public class Job {
			public JobHandle FindActive;
			
			public bool IsCompleted => FindActive.IsCompleted;
			public void Complete () => FindActive.Complete();
		}

		public struct Edge {
			public float3 pos; // position of approximated iso crossing
			public float3 normal; // normalized gradient at approximated iso crossing
		}

		[BurstCompile]
		public struct FindSurfaceEdgesJob : IJobParallelForFilter {
			[ReadOnly] public NativeArray<Voxel> Voxels;
			
			public bool Execute (int i) {
				int axis = i / EDGES_PER_AXIS;
				i %= EDGES_PER_AXIS;
				
				int3 size = VOXELS;
				size[axis] -= 1;

				int3 ia = flatTo3dIndex(i, size);
				int3 ib = ia;
				ib[axis] += 1;

				float a = Voxels[_3dToFlatIndex(ia, Chunk.VOXELS + 2)].value;
				float b = Voxels[_3dToFlatIndex(ib, Chunk.VOXELS + 2)].value;

				a -= ISO;
				b -= ISO;

				return a < 0 != b < 0;
			}
		}
		
		//[BurstCompile]
		//public struct FindSurfaceCellsJob : IJob {
		//	[ReadOnly] public NativeList<int> SurfaceEdges;
		//	[WriteOnly] public NativeMultiHashMap<int, int> SurfaceCellEdges;
		//	
		//	void AddCellEdge (int cellIndex, int edgeIndex) {
		//		SurfaceCellEdges[cellIndex]
		//	}
		//	public void Execute () {
		//		for (int i=0; i<SurfaceEdges.Length; ++i) {
		//			int index = i;
		//			
		//			int axis = index / EDGES_PER_AXIS;
		//			index %= EDGES_PER_AXIS;
		//		
		//			int3 size = Chunk.VOXELS + 1;
		//			size[axis] -= 1;
		//
		//			int3 ia = flatTo3dIndex(index, size);
		//
		//			SurfaceCellEdges.Add(i, i);
		//		}
		//	}
		//}

		#if false
			public DataNative data;
			
			unsafe void SetEdge (int3 cellIndex, int edge, int edgeIndex) {
				if (!data.GetActiveCell(cellIndex, out Cell cell)) cell = default;

				cell.edges[edge] = edgeIndex + 1; // store index in edge list + 1, 0 means edge is inactive

				data.SetActiveCell(cellIndex, cell);
			}

			void FindActive (int x, int y, int z) {
				int3 index = int3(x,y,z);

				if ((childrenMask & (1 <<_3dToFlatIndex(index / (Chunk.VOXEL_COUNT/2), 2))) != 0)
					return;

				var posA = index;
				var posB = index + int3(1,0,0);
				var posC = index + int3(0,1,0);
				var posD = index + int3(0,0,1);
						
				bool voxBValid = all(posB < Chunk.VOXEL_COUNT+1);
				bool voxCValid = all(posC < Chunk.VOXEL_COUNT+1);
				bool voxDValid = all(posD < Chunk.VOXEL_COUNT+1);
						
				Voxel voxA;
				Voxel voxB = default;
				Voxel voxC = default;
				Voxel voxD = default;

								voxA = Voxels[_3dToFlatIndex(posA, Chunk.VOXEL_COUNT+1)];
				if (voxBValid) voxB = Voxels[_3dToFlatIndex(posB, Chunk.VOXEL_COUNT+1)];
				if (voxCValid) voxC = Voxels[_3dToFlatIndex(posC, Chunk.VOXEL_COUNT+1)];
				if (voxDValid) voxD = Voxels[_3dToFlatIndex(posD, Chunk.VOXEL_COUNT+1)];

				bool signA =              (voxA.value < iso);
				bool edgeX = voxBValid && (voxA.value < iso) != (voxB.value < iso);
				bool edgeY = voxCValid && (voxA.value < iso) != (voxC.value < iso);
				bool edgeZ = voxDValid && (voxA.value < iso) != (voxD.value < iso);

				int NV = Chunk.VOXEL_COUNT;

				if (edgeX) {
					var edgeIndex = AddEdge(data.edges, 0, index, signA, voxA, voxB, posA, posB, iso);
					if (z < NV && y < NV) SetEdge(int3(x, y  , z  ),  0, edgeIndex); // mark active edge for this cell
					if (z < NV && y >  1) SetEdge(int3(x, y-1, z  ),  1, edgeIndex); // mark active edge for the cells that edge also neighbours
					if (z >  1 && y < NV) SetEdge(int3(x, y  , z-1),  2, edgeIndex);
					if (z >  1 && y >  1) SetEdge(int3(x, y-1, z-1),  3, edgeIndex);
				}
				if (edgeY) {
					var edgeIndex = AddEdge(data.edges, 1, index, !signA, voxA, voxC, posA, posC, iso); // !signA to flip y faces because unity is left handed y up and i usually think right-handed with z up, somewhere my though process caused the y faces to be flipped
					if (z < NV && x < NV) SetEdge(int3(x  , y, z  ),  4, edgeIndex);
					if (z < NV && x >  0) SetEdge(int3(x-1, y, z  ),  5, edgeIndex);
					if (z >  0 && x < NV) SetEdge(int3(x  , y, z-1),  6, edgeIndex);
					if (z >  0 && x >  0) SetEdge(int3(x-1, y, z-1),  7, edgeIndex);
				}
				if (edgeZ) {
					var edgeIndex = AddEdge(data.edges, 2, index, signA, voxA, voxD, posA, posD, iso);
					if (y < NV && x < NV) SetEdge(int3(x  , y  , z),  8, edgeIndex);
					if (y < NV && x >  0) SetEdge(int3(x-1, y  , z),  9, edgeIndex);
					if (y >  0 && x < NV) SetEdge(int3(x  , y-1, z), 10, edgeIndex);
					if (y >  0 && x >  0) SetEdge(int3(x-1, y-1, z), 11, edgeIndex);
				}
			}
			
			unsafe float3 massPoint (int3 cellPos, Cell cell, out float3 normal) {
				float3 avgPos = 0;
				float3 avgNormal = 0;
				int count = 0;
			
				for (int i=0; i<12; ++i) {
					var edgeIndex = cell.getEdgeIndex(i);
					if (edgeIndex > 0) {
						var edge = data.edges[edgeIndex -1];
						
						avgPos += edge.pos;
						avgNormal += edge.normal;
						count++;
					}
				}

				avgPos /= count;
				avgNormal /= count;

				normal = avgNormal;
				return avgPos;
			}

			unsafe float3 DualContourIterative (int3 cellPos, Cell cell) {
				// Instead of using a QEF solver, use a iterative method
				// This is my approach of solving this, this is basicly gradient descent which is used in machine learning
				// We know we want the best fit point based on a set of points with normals (called hermite?) which can be thought of as defining a plane
				// There should (usually) be a point somewhere (maybe outside the cell) that is the global minimum of distances to these planes
				// It seems Augusto Schmitz came up with something similar http://www.inf.ufrgs.br/~comba/papers/thesis/diss-leonardo.pdf - called Schimtz Particle Method by mattbick2003 - https://www.reddit.com/r/Unity3D/comments/bw6x1l/an_update_on_the_job_system_dual_contouring/
			
				float3 particle = massPoint(cellPos, cell, out float3 normal);
				//float3 particle = (float3)cellPos + 0.5f;
				//return particle;

				//cell.normal = normal;
			
				int iter = 0;
				while (iter++ < DCMaxIterations) {
					float3 sumForce = 0;
				
					int count = 0;
				
					for (int i=0; i<12; ++i) {
						var edgeIndex = cell.getEdgeIndex(i);
						if (edgeIndex > 0) {
							var edge = data.edges[edgeIndex -1];
						
							var posRel = particle - edge.pos;
							
							float signedDistance = dot(edge.normal, posRel);
							float signedSqrError = signedDistance * abs(signedDistance);
				
							float3 force = signedSqrError * -edge.normal;
				
							sumForce += force;
							count++;
						}
					}
				
					sumForce /= count;
				
					particle += sumForce * DCIterStrength;
				
					particle = clamp(particle, (float3)cellPos, (float3)(cellPos + 1));
				}

				return particle;
			
			Edge CalcEdge (Voxel voxA, Voxel voxB, float3 posA, float3 posB) {
				float diff = voxB.value - voxA.value;

				float t = (Iso - voxA.value) / diff; // approximate position of the isosurface by linear interpolation
				
				return new Edge {
					//active = step(t, 0f) * (1f - step(t, 1f)),
					active = t >= 0f && t < 1f,
					pos = lerp(posA, posB, t),
					normal = normalizesafe( lerp(voxA.gradient, voxB.gradient, t) )
				};
			}

			struct Edges {
				public Edge ex00;
				public Edge ex10;
				public Edge ex01;
				public Edge ex11;
				public Edge ey00;
				public Edge ey10;
				public Edge ey01;
				public Edge ey11;
				public Edge ez00;
				public Edge ez10;
				public Edge ez01;
				public Edge ez11;

				public Edge this [int i] {
					get {
						switch (i) {
							case  0: return ex00;
							case  1: return ex10;
							case  2: return ex01;
							case  3: return ex11;
							case  4: return ey00;
							case  5: return ey10;
							case  6: return ey01;
							case  7: return ey11;
							case  8: return ez00;
							case  9: return ez10;
							case 10: return ez01;
							case 11: return ez11;
							default: return default;
						}
					}
				}
			}

			float3 CalcCell (int x, int y, int z) {
				int3 index = int3(x,y,z);
				
				int3 i000 = index              ;
				int3 i100 = index + int3(1,0,0);
				int3 i010 = index + int3(0,1,0);
				int3 i110 = index + int3(1,1,0);
				int3 i001 = index + int3(0,0,1);
				int3 i101 = index + int3(1,0,1);
				int3 i011 = index + int3(0,1,1);
				int3 i111 = index + int3(1,1,1);

				Voxel v000 = Voxels[_3dToFlatIndex(i000, Chunk.VOXELS + 2)];
				Voxel v100 = Voxels[_3dToFlatIndex(i100, Chunk.VOXELS + 2)];
				Voxel v010 = Voxels[_3dToFlatIndex(i010, Chunk.VOXELS + 2)];
				Voxel v110 = Voxels[_3dToFlatIndex(i110, Chunk.VOXELS + 2)];
				Voxel v001 = Voxels[_3dToFlatIndex(i001, Chunk.VOXELS + 2)];
				Voxel v101 = Voxels[_3dToFlatIndex(i101, Chunk.VOXELS + 2)];
				Voxel v011 = Voxels[_3dToFlatIndex(i011, Chunk.VOXELS + 2)];
				Voxel v111 = Voxels[_3dToFlatIndex(i111, Chunk.VOXELS + 2)];
				
				Edges edges;
				edges.ex00 = CalcEdge(v000, v100,  i000, i100);
				edges.ex10 = CalcEdge(v010, v110,  i010, i110);
				edges.ex01 = CalcEdge(v001, v101,  i001, i101);
				edges.ex11 = CalcEdge(v011, v111,  i011, i111);
				
				edges.ey00 = CalcEdge(v000, v010,  i000, i010);
				edges.ey10 = CalcEdge(v100, v110,  i100, i110);
				edges.ey01 = CalcEdge(v001, v011,  i001, i011);
				edges.ey11 = CalcEdge(v101, v111,  i101, i111);
				
				edges.ez00 = CalcEdge(v000, v001,  i000, i001);
				edges.ez10 = CalcEdge(v100, v101,  i100, i101);
				edges.ez01 = CalcEdge(v010, v011,  i010, i011);
				edges.ez11 = CalcEdge(v110, v111,  i110, i111);
				
				return DualContourIterative(index, ref edges);
			}
			
			float3 MassPoint (int3 cellPos, ref Edges edges, out float3 normal) {
				float3 avgPos = 0;
				float3 avgNormal = 0;
				float count = 0;
			
				for (int i=0; i<12; ++i) {
					var edge = edges[i];
					if (edge.active) {
						avgPos += edge.pos;
						avgNormal += edge.normal;
						count++;
					}
				}

				avgPos /= count;
				avgNormal /= count;

				normal = avgNormal;
				return avgPos;
			}

			float3 DualContourIterative (int3 cellPos, ref Edges edges) {
				// Instead of using a QEF solver, use a iterative method
				// This is my approach of solving this, this is basicly gradient descent which is used in machine learning
				// We know we want the best fit point based on a set of points with normals (called hermite?) which can be thought of as defining a plane
				// There should (usually) be a point somewhere (maybe outside the cell) that is the global minimum of distances to these planes
				// It seems Augusto Schmitz came up with something similar http://www.inf.ufrgs.br/~comba/papers/thesis/diss-leonardo.pdf - called Schimtz Particle Method by mattbick2003 - https://www.reddit.com/r/Unity3D/comments/bw6x1l/an_update_on_the_job_system_dual_contouring/
			
				float3 particle = MassPoint(cellPos, ref edges, out float3 normal);
				//float3 particle = (float3)cellPos + 0.5f;
				//return particle;

				//cell.normal = normal;
			
				int iter = 0;
				while (iter++ < DCMaxIterations) {
					float3 sumForce = 0;
				
					int count = 0;
				
					for (int i=0; i<12; ++i) {
						var edge = edges[i];
						if (edge.active) {
							var posRel = particle - edge.pos;
							
							float signedDistance = dot(edge.normal, posRel);
							float signedSqrError = signedDistance * abs(signedDistance);
				
							float3 force = signedSqrError * -edge.normal;
				
							sumForce += force;
							count++;
						}
					}
				
					sumForce /= count;
				
					particle += sumForce * DCIterStrength;
				
					particle = clamp(particle, (float3)cellPos, (float3)(cellPos + 1));
				}

				return particle;
			}
			
			public void Execute () {
				int cells = Chunk.VOXELS + 1;

				// Find active edges and cells
				for (int z=0; z<cells; ++z) {
					for (int y=0; y<cells; ++y) {
						for (int x=0; x<cells; ++x) {
							CalcCell(x,y,z);
						}
					}
				}
				
				// Calculate vertices positions
				for (int z=0; z<Chunk.VOXEL_COUNT; ++z) {
					for (int y=0; y<Chunk.VOXEL_COUNT; ++y) {
						for (int x=0; x<Chunk.VOXEL_COUNT; ++x) {
							int3 index = int3(x,y,z);
							if (data.GetActiveCell(index, out Cell cell)) {

								float3 vertex = DualContourIterative(index, cell);

								//vertex = clamp(vertex, (float3)index, (float3)(index + 1));

								cell.vertex = vertex;
								data.SetActiveCell(index, cell);
							}
						}
					}
				}
				
				float size = NodeSize / Chunk.VOXEL_COUNT;

				// Output the face for each active edge
				for (int i=0; i<data.edges.Length; ++i) {
					var cell0 = data.edges[i].GetCellIndex(0);
					var cell1 = data.edges[i].GetCellIndex(1);
					var cell2 = data.edges[i].GetCellIndex(2);
					var cell3 = data.edges[i].GetCellIndex(3);
				
					if (	all(cell0 >= 0 & cell0 < Chunk.VOXEL_COUNT) &&
							all(cell1 >= 0 & cell1 < Chunk.VOXEL_COUNT) &&
							all(cell2 >= 0 & cell2 < Chunk.VOXEL_COUNT) &&
							all(cell3 >= 0 & cell3 < Chunk.VOXEL_COUNT)) {
						if (data.edges[i].flipFace) {
							data.EmitTriangle(cell0, cell1, cell2, size);
							data.EmitTriangle(cell2, cell1, cell3, size);
						} else {
							data.EmitTriangle(cell1, cell0, cell3, size);
							data.EmitTriangle(cell3, cell0, cell2, size);
						}
					}
				}
			}

			public void EmitTriangle (Cell a, Cell b, Cell c, float cellSize, Color col) {
				if (all(a.vertex == b.vertex) || all(a.vertex == c.vertex))
					return; // degenerate triangle
		
				vertices.Add(a.vertex * cellSize);
				vertices.Add(b.vertex * cellSize);
				vertices.Add(c.vertex * cellSize);
		
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

			public void EmitTriangle (int3 indxA, int3 indxB, int3 indxC, float cellSize) {
				GetActiveCell(indxA, out Cell a);
				GetActiveCell(indxB, out Cell b);
				GetActiveCell(indxC, out Cell c);
		
				EmitTriangle(a, b, c, cellSize, Color.white);
			}
		};

		public class MeshingJob {
			Chunk node;

			public JobHandle? jobHandle = null;
			CalcNodeJob job;
			
			public MeshingJob (Chunk node, int childrenMask, float iso, float DCIterStrength, int DCMaxIterations) {
				job = new CalcNodeJob {
					NodeSize = node.Size,
					childrenMask = childrenMask,
					iso = iso,
					DCIterStrength = DCIterStrength,
					DCMaxIterations = DCMaxIterations,
				};

				this.node = node;
				
				node.Voxels.IncRef();
				job.Voxels = node.Voxels.native;

				node.MesherData = new Data { job = this };
				node.MesherData.IncRef(); // for putting in job
				node.MesherData.IncRef(); // for putting in node

				job.data = node.MesherData.native;

				jobHandle = job.Schedule(node.Voxels.job != null ? node.Voxels.job.JobHandle.Value : default);
			}
			public bool IsCompleted () => jobHandle.Value.IsCompleted;
			public void Apply (Chunk node) {
				jobHandle.Value.Complete();

				node.MesherData.SetMesh(node.mesh);
				
				Dispose();
			}

			public void Dispose () {
				if (jobHandle != null) {
					jobHandle.Value.Complete();
					
					node.Voxels.DecRef();
					node.MesherData.DecRef();
					node.MesherData.job = null;
					
					jobHandle = null;
				}
			}
		}
		
		public class SeamMeshingJob {
			Chunk node;
			Chunk[] neighbours;

			MeshingJob meshingJob;

			JobHandle? jobHandle;
			CalcNodeSeamJob job;
			
			public SeamMeshingJob (TerrainOctree octree, Chunk node) {
				
				neighbours = new Chunk[3*3*3];
				//var neighboursNative = new NativeArray<Node>(3*3*3, Allocator.Persistent);
				
				job = new CalcNodeSeamJob {
					VoxelSize = octree.VoxelSize,
					iso = ISO,
				};

				this.node = node;
				
				node.Voxels.IncRef();
				node.MesherData.IncRef();

				meshingJob = node.MesherData.job;
				
				job.Node.Lod = node.Lod;
				job.Node.Pos = (int3)round(node.Pos / octree.VoxelSize);
				job.Node.Size = Chunk.VOXEL_COUNT << node.Lod;
				job.Node.Voxels = node.Voxels.native;
				job.Node.data = node.MesherData.native;

				for (int i=0; i<3*3*3; ++i) {
					int3 dir = flatTo3dIndex(i, 3) - 1;
					if (all(dir == 0))
						continue;
		
					var neigh = octree.GetNeighbourTree(node, dir);
					
					if (!neigh.IsCreated)
						neigh = null;

					Node neighStruct = default;
					if (neigh == null) {
						neighStruct.Lod = -1;
					} else {
						
						if (neigh.MesherData.job != null) {
							Debug.Assert(meshingJob == null);
							meshingJob = neigh.MesherData.job;
						}

						neigh.Voxels.IncRef();
						neigh.MesherData.IncRef();
						
						neighStruct.Lod = neigh.Lod;
						neighStruct.Pos = (int3)round(neigh.Pos / octree.VoxelSize);
						neighStruct.Size = Chunk.VOXEL_COUNT << neigh.Lod;
						neighStruct.Voxels = neigh.Voxels.native;
						neighStruct.data = neigh.MesherData.native;
					}
		
					neighbours[i] = neigh;
					job.Neighbours[i] = neighStruct;
				}

				//job.Neighbours = neighboursNative;
				
				jobHandle = job.Schedule(meshingJob?.jobHandle ?? default);
			}
			public bool IsCompleted () => jobHandle.Value.IsCompleted;
			public void Apply () {
				jobHandle.Value.Complete();
				
				node.MesherData.SetMesh(node.SeamMesh);

				for (int i=0; i<3*3*3; ++i)
					neighbours[i].MesherData.SetMesh(neighbours[i].SeamMesh);
				
				Dispose();
			}
		
			public void Dispose () {
				if (jobHandle != null) {
					jobHandle.Value.Complete();
				}
				
				// Alloc always happens currently, not just when sheduled
				node.Voxels.DecRef();
				node.MesherData.DecRef();

				for (int i=0; i<3*3*3; ++i) {
					neighbours[i].Voxels.DecRef();
					neighbours[i].MesherData.DecRef();
				}

				//job.Neighbours.Dispose();
			}
		}
		
		static int AddEdge (NativeList<Edge> edges, int axis, int3 index, bool signA, Voxel voxA, Voxel voxB, float3 posA, float3 posB, float iso) {
			var edge = new Edge();

			float t = unlerp(voxA.value, voxB.value, iso); // approximate position of the isosurface by linear interpolation

			edge.axis = axis;
			edge.index = index;

			edge.flipFace = signA;

			edge.pos = lerp(posA, posB, t);
			edge.normal = normalizesafe( lerp(voxA.gradient, voxB.gradient, t) );

			int indx = edges.Length;
			edges.Add(edge);
			return indx;
		}
	
		[BurstCompile]
		public struct CalcNodeJob : IJob {
			[ReadOnly] public int childrenMask;
			[ReadOnly] public float NodeSize;
			[ReadOnly] public float iso;
			[ReadOnly] public float DCIterStrength;
			[ReadOnly] public int DCMaxIterations;
			[ReadOnly] public NativeArray<Voxel> Voxels;
		
			public DataNative data;
			
			unsafe void SetEdge (int3 cellIndex, int edge, int edgeIndex) {
				if (!data.GetActiveCell(cellIndex, out Cell cell)) cell = default;

				cell.edges[edge] = edgeIndex + 1; // store index in edge list + 1, 0 means edge is inactive

				data.SetActiveCell(cellIndex, cell);
			}

			void FindActive (int x, int y, int z) {
				int3 index = int3(x,y,z);

				if ((childrenMask & (1 <<_3dToFlatIndex(index / (Chunk.VOXEL_COUNT/2), 2))) != 0)
					return;

				var posA = index;
				var posB = index + int3(1,0,0);
				var posC = index + int3(0,1,0);
				var posD = index + int3(0,0,1);
						
				bool voxBValid = all(posB < Chunk.VOXEL_COUNT+1);
				bool voxCValid = all(posC < Chunk.VOXEL_COUNT+1);
				bool voxDValid = all(posD < Chunk.VOXEL_COUNT+1);
						
				Voxel voxA;
				Voxel voxB = default;
				Voxel voxC = default;
				Voxel voxD = default;

								voxA = Voxels[_3dToFlatIndex(posA, Chunk.VOXEL_COUNT+1)];
				if (voxBValid) voxB = Voxels[_3dToFlatIndex(posB, Chunk.VOXEL_COUNT+1)];
				if (voxCValid) voxC = Voxels[_3dToFlatIndex(posC, Chunk.VOXEL_COUNT+1)];
				if (voxDValid) voxD = Voxels[_3dToFlatIndex(posD, Chunk.VOXEL_COUNT+1)];

				bool signA =              (voxA.value < iso);
				bool edgeX = voxBValid && (voxA.value < iso) != (voxB.value < iso);
				bool edgeY = voxCValid && (voxA.value < iso) != (voxC.value < iso);
				bool edgeZ = voxDValid && (voxA.value < iso) != (voxD.value < iso);

				int NV = Chunk.VOXEL_COUNT;

				if (edgeX) {
					var edgeIndex = AddEdge(data.edges, 0, index, signA, voxA, voxB, posA, posB, iso);
					if (z < NV && y < NV) SetEdge(int3(x, y  , z  ),  0, edgeIndex); // mark active edge for this cell
					if (z < NV && y >  1) SetEdge(int3(x, y-1, z  ),  1, edgeIndex); // mark active edge for the cells that edge also neighbours
					if (z >  1 && y < NV) SetEdge(int3(x, y  , z-1),  2, edgeIndex);
					if (z >  1 && y >  1) SetEdge(int3(x, y-1, z-1),  3, edgeIndex);
				}
				if (edgeY) {
					var edgeIndex = AddEdge(data.edges, 1, index, !signA, voxA, voxC, posA, posC, iso); // !signA to flip y faces because unity is left handed y up and i usually think right-handed with z up, somewhere my though process caused the y faces to be flipped
					if (z < NV && x < NV) SetEdge(int3(x  , y, z  ),  4, edgeIndex);
					if (z < NV && x >  0) SetEdge(int3(x-1, y, z  ),  5, edgeIndex);
					if (z >  0 && x < NV) SetEdge(int3(x  , y, z-1),  6, edgeIndex);
					if (z >  0 && x >  0) SetEdge(int3(x-1, y, z-1),  7, edgeIndex);
				}
				if (edgeZ) {
					var edgeIndex = AddEdge(data.edges, 2, index, signA, voxA, voxD, posA, posD, iso);
					if (y < NV && x < NV) SetEdge(int3(x  , y  , z),  8, edgeIndex);
					if (y < NV && x >  0) SetEdge(int3(x-1, y  , z),  9, edgeIndex);
					if (y >  0 && x < NV) SetEdge(int3(x  , y-1, z), 10, edgeIndex);
					if (y >  0 && x >  0) SetEdge(int3(x-1, y-1, z), 11, edgeIndex);
				}
			}
			
			unsafe float3 massPoint (int3 cellPos, Cell cell, out float3 normal) {
				float3 avgPos = 0;
				float3 avgNormal = 0;
				int count = 0;
			
				for (int i=0; i<12; ++i) {
					var edgeIndex = cell.getEdgeIndex(i);
					if (edgeIndex > 0) {
						var edge = data.edges[edgeIndex -1];
						
						avgPos += edge.pos;
						avgNormal += edge.normal;
						count++;
					}
				}

				avgPos /= count;
				avgNormal /= count;

				normal = avgNormal;
				return avgPos;
			}

			unsafe float3 DualContourIterative (int3 cellPos, Cell cell) {
				// Instead of using a QEF solver, use a iterative method
				// This is my approach of solving this, this is basicly gradient descent which is used in machine learning
				// We know we want the best fit point based on a set of points with normals (called hermite?) which can be thought of as defining a plane
				// There should (usually) be a point somewhere (maybe outside the cell) that is the global minimum of distances to these planes
				// It seems Augusto Schmitz came up with something similar http://www.inf.ufrgs.br/~comba/papers/thesis/diss-leonardo.pdf - called Schimtz Particle Method by mattbick2003 - https://www.reddit.com/r/Unity3D/comments/bw6x1l/an_update_on_the_job_system_dual_contouring/
			
				float3 particle = massPoint(cellPos, cell, out float3 normal);
				//float3 particle = (float3)cellPos + 0.5f;
				//return particle;

				//cell.normal = normal;
			
				int iter = 0;
				while (iter++ < DCMaxIterations) {
					float3 sumForce = 0;
				
					int count = 0;
				
					for (int i=0; i<12; ++i) {
						var edgeIndex = cell.getEdgeIndex(i);
						if (edgeIndex > 0) {
							var edge = data.edges[edgeIndex -1];
						
							var posRel = particle - edge.pos;
							
							float signedDistance = dot(edge.normal, posRel);
							float signedSqrError = signedDistance * abs(signedDistance);
				
							float3 force = signedSqrError * -edge.normal;
				
							sumForce += force;
							count++;
						}
					}
				
					sumForce /= count;
				
					particle += sumForce * DCIterStrength;
				
					particle = clamp(particle, (float3)cellPos, (float3)(cellPos + 1));
				}

				return particle;
			}
			
			public void Execute () {
				// Find active edges and cells
				for (int z=0; z<Chunk.VOXEL_COUNT+1; ++z) {
					for (int y=0; y<Chunk.VOXEL_COUNT+1; ++y) {
						for (int x=0; x<Chunk.VOXEL_COUNT+1; ++x) {
							FindActive(x,y,z);
						}
					}
				}
				
				// Calculate vertices positions
				for (int z=0; z<Chunk.VOXEL_COUNT; ++z) {
					for (int y=0; y<Chunk.VOXEL_COUNT; ++y) {
						for (int x=0; x<Chunk.VOXEL_COUNT; ++x) {
							int3 index = int3(x,y,z);
							if (data.GetActiveCell(index, out Cell cell)) {

								float3 vertex = DualContourIterative(index, cell);

								//vertex = clamp(vertex, (float3)index, (float3)(index + 1));

								cell.vertex = vertex;
								data.SetActiveCell(index, cell);
							}
						}
					}
				}
				
				float size = NodeSize / Chunk.VOXEL_COUNT;

				// Output the face for each active edge
				for (int i=0; i<data.edges.Length; ++i) {
					var cell0 = data.edges[i].GetCellIndex(0);
					var cell1 = data.edges[i].GetCellIndex(1);
					var cell2 = data.edges[i].GetCellIndex(2);
					var cell3 = data.edges[i].GetCellIndex(3);
				
					if (	all(cell0 >= 0 & cell0 < Chunk.VOXEL_COUNT) &&
							all(cell1 >= 0 & cell1 < Chunk.VOXEL_COUNT) &&
							all(cell2 >= 0 & cell2 < Chunk.VOXEL_COUNT) &&
							all(cell3 >= 0 & cell3 < Chunk.VOXEL_COUNT)) {
						if (data.edges[i].flipFace) {
							data.EmitTriangle(cell0, cell1, cell2, size);
							data.EmitTriangle(cell2, cell1, cell3, size);
						} else {
							data.EmitTriangle(cell1, cell0, cell3, size);
							data.EmitTriangle(cell3, cell0, cell2, size);
						}
					}
				}
			}
		}
		
		public struct Node {
			[ReadOnly] public int3 Pos; // without VoxelSize multiplied
			[ReadOnly] public int Size; // without VoxelSize multiplied, ie. TerrainNode.VOXEL_COUNT << lod
			[ReadOnly] public int Lod;
			public DataNative data;
			public NativeArray<Voxel> Voxels;

			public bool IsNull () => Lod < 0;
		};
		
		// Unity does not allow managed arrays in burst jobs,
		//  does not allow NativeArrays<NativeArrays>,
		//  unsafe structs do not allow fixed arrays of structs,
		//  NativeArray in Node is also a managed type, so i can't even do pointer casting with Marshalling,
		//  so I had to resort to this atrocity
		// The standart approach here seems to be, to combine the nativearrays you need into one, but that makes little sense in an octree,
		//  I can't reasonably allocate all memory for a dynamic octree in one array
		//  i guess the best approach would be to copy all the required memory on job creation, but is that really the only way?
		public struct NodesArray {
			Node _000, _001, _002;
			Node _010, _011, _012;
			Node _020, _021, _022;
			
			Node _100, _101, _102;
			Node _110, _111, _112;
			Node _120, _121, _122;
			
			Node _200, _201, _202;
			Node _210, _211, _212;
			Node _220, _221, _222;
			
			public Node this[int index] {
				// What the f*** did I just bring upon this cursed land?
				get {
					switch (index) {
						case  0: return _000;
						case  1: return _001;
						case  2: return _002;
						case  3: return _010;
						case  4: return _011;
						case  5: return _012;
						case  6: return _020;
						case  7: return _021;
						case  8: return _022;
						case  9: return _100;
						case 10: return _101;
						case 11: return _102;
						case 12: return _110;
						case 13: return _111;
						case 14: return _112;
						case 15: return _120;
						case 16: return _121;
						case 17: return _122;
						case 18: return _200;
						case 19: return _201;
						case 20: return _202;
						case 21: return _210;
						case 22: return _211;
						case 23: return _212;
						case 24: return _220;
						case 25: return _221;
						case 26: return _222;
					}
					return default;
				}
				set {
					switch (index) {
						case  0: _000 = value; return;
						case  1: _001 = value; return;
						case  2: _002 = value; return;
						case  3: _010 = value; return;
						case  4: _011 = value; return;
						case  5: _012 = value; return;
						case  6: _020 = value; return;
						case  7: _021 = value; return;
						case  8: _022 = value; return;
						case  9: _100 = value; return;
						case 10: _101 = value; return;
						case 11: _102 = value; return;
						case 12: _110 = value; return;
						case 13: _111 = value; return;
						case 14: _112 = value; return;
						case 15: _120 = value; return;
						case 16: _121 = value; return;
						case 17: _122 = value; return;
						case 18: _200 = value; return;
						case 19: _201 = value; return;
						case 20: _202 = value; return;
						case 21: _210 = value; return;
						case 22: _211 = value; return;
						case 23: _212 = value; return;
						case 24: _220 = value; return;
						case 25: _221 = value; return;
						case 26: _222 = value; return;
						default: return;
					}
				}
			}
		};

		[BurstCompile]
		public struct CalcNodeSeamJob : IJob {
			[ReadOnly] public float VoxelSize;
			[ReadOnly] public float iso;
			public Node Node;
			
			//[ReadOnly] public NativeArray<Node> Neighbours; // [3,3,3]
			public NodesArray Neighbours;
			
			bool GetNeighbour (int3 dir, out Node neighbour) {
				neighbour = Neighbours[_3dToFlatIndex(dir+1, 3)];
				return !neighbour.IsNull();
			}
			
			void ProcessEdgeCell (int3 index, int axis) {
				
				var posA = index;
				var posB = index;
				posB[axis] += 1;
			
				var voxA = Node.Voxels[_3dToFlatIndex(posA, Chunk.VOXEL_COUNT+1)];
				var voxB = Node.Voxels[_3dToFlatIndex(posB, Chunk.VOXEL_COUNT+1)];
			
				bool signA = voxA.value < iso ^ axis == 1; // flip y faces because unity is left handed y up and i usually think right-handed with z up, somewhere my though process caused the y faces to be flipped
				bool edge = (voxA.value < iso) != (voxB.value < iso);
			
				if (edge) {
					AddEdge(Node.data.seamEdges, axis, index, signA, voxA, voxB, posA, posB, iso);
				}
			}
			
			bool GetCellOrNeighbourCell (int3 index, out Cell cell) {
				var neighbOffs = select(-1, 0, index >= 0);
				neighbOffs = select(neighbOffs, +1, index >= Chunk.VOXEL_COUNT);
				
				if (!GetNeighbour(neighbOffs, out Node neighb)) {
					if (any(index < 0) || any(index >= Chunk.VOXEL_COUNT)) {
						cell = default;
						return false;
					}
			
					Node.data.GetActiveCell(index, out cell);
					return true;
				}
			
				int3 pos = index;
				pos *= Node.Size / Chunk.VOXEL_COUNT;
				pos += Node.Pos - Node.Size/2;
				pos -= neighb.Pos - neighb.Size/2;
				pos /= neighb.Size / Chunk.VOXEL_COUNT;
			
				bool cellActive = neighb.data.GetActiveCell(pos, out cell);
			
				if (!cellActive) {
					// TODO: rare special case
					cell = new Cell();
					//CalcVertex(index, ref cell, node);
					cell.vertex = (float3)pos + 0.5f;
				}
			
				cell.vertex *= neighb.Size / Chunk.VOXEL_COUNT;
				cell.vertex += neighb.Pos - neighb.Size/2;
				cell.vertex -= Node.Pos - Node.Size/2;
				cell.vertex /= Node.Size / Chunk.VOXEL_COUNT;
				return true;
			}
			
			public void Execute () {
				//cells = new Cell[TerrainNode.VOXEL_COUNT, TerrainNode.VOXEL_COUNT, TerrainNode.VOXEL_COUNT]; // assume zeroed
				//edges = new List<Edge>();
			
				int CV = Chunk.VOXEL_COUNT;
			
				bool n100 = GetNeighbour(int3(1,0,0), out Node n100_) && n100_.Lod == Node.Lod;
				bool n010 = GetNeighbour(int3(0,1,0), out Node n010_) && n010_.Lod == Node.Lod;
				bool n001 = GetNeighbour(int3(0,0,1), out Node n001_) && n001_.Lod == Node.Lod;
			
				for (int z=0; z<CV+1; ++z) {
					for (int y=0; y<CV+1; ++y) {
						for (int x=0; x<CV+1; ++x) {
							// Is this a seam cell at all
							bool faceX = x == 0 || x == CV;
							bool faceY = y == 0 || y == CV;
							bool faceZ = z == 0 || z == CV;
						
							if (!faceX && !faceY && !faceZ)
								continue;
						
							// Determine if we own this seam cell
							if (x == CV && n100) continue;
							if (y == CV && n010) continue;
							if (z == CV && n001) continue;
						
							// Generate relevant edges for seam face
							if ((faceY || faceZ) && x < CV) ProcessEdgeCell(int3(x,y,z), 0);
							if ((faceX || faceZ) && y < CV) ProcessEdgeCell(int3(x,y,z), 1);
							if ((faceX || faceY) && z < CV) ProcessEdgeCell(int3(x,y,z), 2);
						}
					}
				}
				
				float size = Node.Size / Chunk.VOXEL_COUNT;
			
				for (int i=0; i<Node.data.seamEdges.Length; ++i) {
					var cell0Index = Node.data.seamEdges[i].GetCellIndex(0);
					var cell1Index = Node.data.seamEdges[i].GetCellIndex(1);
					var cell2Index = Node.data.seamEdges[i].GetCellIndex(2);
					var cell3Index = Node.data.seamEdges[i].GetCellIndex(3);
			
					bool cell0B = GetCellOrNeighbourCell(cell0Index, out Cell cell0);
					bool cell1B = GetCellOrNeighbourCell(cell1Index, out Cell cell1);
					bool cell2B = GetCellOrNeighbourCell(cell2Index, out Cell cell2);
					bool cell3B = GetCellOrNeighbourCell(cell3Index, out Cell cell3);
			
					if (cell0B && cell1B && cell2B && cell3B) {
						if (Node.data.seamEdges[i].flipFace) {
							Node.data.EmitTriangle(cell0, cell1, cell2, size, Color.green);
							Node.data.EmitTriangle(cell2, cell1, cell3, size, Color.blue);
						} else {
							Node.data.EmitTriangle(cell1, cell0, cell3, size, Color.green);
							Node.data.EmitTriangle(cell3, cell0, cell2, size, Color.blue);
						}
					}
				}
			}
		}
				#endif
	}
}
