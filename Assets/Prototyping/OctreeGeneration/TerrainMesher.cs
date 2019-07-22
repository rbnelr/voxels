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
	
	public class TerrainMesher : MonoBehaviour {
		
		public float DCIterStrength = 1f;
		public int DCMaxIterations = 5;
		float prevDCIterStrength;
		int prevDCIterations;

		const float ISO = 0.0f; // TODO: make variable for testing?
		
		public MeshingJob SheduleMeshNode (TerrainNode node, int childrenMask) {
			return new MeshingJob(node, childrenMask, ISO, DCIterStrength, DCMaxIterations);
		}
		public SeamMeshingJob SheduleMeshSeam (TerrainOctree octree, TerrainNode node) {
			return new SeamMeshingJob(octree, node);
		}
		
		public class Data {
			public DataNative native;
			public int refCount = 0;
			public TerrainMesher.MeshingJob job;

			public Data () {
				native.Alloc();
			}

			public void IncRef () {
				refCount++;
			}
			public void DecRef () {
				refCount--;
				if (refCount == 0)
					native.Dispose();
			}
			
			static List<Vector3>	verticesBuf  = new List<Vector3>();
			static List<Vector3>	normalsBuf   = new List<Vector3>();
			static List<Vector2>	uvBuf        = new List<Vector2>();
			static List<Color>		colorsBuf    = new List<Color>();
			static List<int>		trianglesBuf = new List<int>();

			public void SetMesh (Mesh mesh) {
		
				Profiler.BeginSample("TerrainNode.AssignMesh");
				mesh.Clear();
					Profiler.BeginSample("vertices");
						mesh.SetVerticesNative(native.vertices, ref verticesBuf);
					Profiler.EndSample();
					Profiler.BeginSample("normals");
						mesh.SetNormalsNative(native.normals, ref normalsBuf);
					Profiler.EndSample();
					Profiler.BeginSample("uv");
						mesh.SetUvsNative(0, native.uv, ref uvBuf);
					Profiler.EndSample();
					Profiler.BeginSample("colors");
						mesh.SetColorsNative(native.colors, ref colorsBuf);
					Profiler.EndSample();
					Profiler.BeginSample("triangles");
						mesh.SetTrianglesNative(native.triangles, 0, ref trianglesBuf);
					Profiler.EndSample();
				Profiler.EndSample();
			}
		}
		public struct DataNative {
			public NativeArray<Cell> cells;
			public NativeList<Edge> edges;
			public NativeList<Edge> seamEdges;
			
			public NativeList<float3> vertices;
			public NativeList<float3> normals;
			public NativeList<float2> uv;
			public NativeList<Color>  colors;
			public NativeList<int>    triangles;

			public void Alloc () {
				int ArraySize = TerrainNode.VOXEL_COUNT + 1;
				
				int cellsAlloc = TerrainNode.VOXEL_COUNT * TerrainNode.VOXEL_COUNT * TerrainNode.VOXEL_COUNT;
				int edgeAlloc = ArraySize * ArraySize * 4;
				int vertexAlloc = ArraySize * ArraySize * 6;

				cells = new NativeArray<Cell>(cellsAlloc, Allocator.Persistent);
				edges = new NativeList<Edge>(edgeAlloc, Allocator.Persistent);
				seamEdges = new NativeList<Edge>(ArraySize * 4, Allocator.Persistent);
				
				vertices  = new NativeList<float3> (vertexAlloc, Allocator.Persistent);
				normals   = new NativeList<float3> (vertexAlloc, Allocator.Persistent);
				uv        = new NativeList<float2> (vertexAlloc, Allocator.Persistent);
				colors    = new NativeList<Color>  (vertexAlloc, Allocator.Persistent);
				triangles = new NativeList<int>	   (vertexAlloc, Allocator.Persistent);
			}

			public void Dispose () {
				cells.Dispose();
				edges.Dispose();
				seamEdges.Dispose();
				
				vertices  .Dispose();
				normals   .Dispose();
				uv        .Dispose();
				colors    .Dispose();
				triangles .Dispose();
			}


			//
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
			
			public bool GetActiveCell (int3 index, out Cell cell) {
				cell = cells[ _3dToFlatIndex(index, TerrainNode.VOXEL_COUNT) ];
				return cell.active;
			}
			public void SetActiveCell (int3 index, Cell cell) {
				cell.active = true;
				cells[ _3dToFlatIndex(index, TerrainNode.VOXEL_COUNT) ] = cell;
			}

			public void EmitTriangle (int3 indxA, int3 indxB, int3 indxC, float cellSize) {
				GetActiveCell(indxA, out Cell a);
				GetActiveCell(indxB, out Cell b);
				GetActiveCell(indxC, out Cell c);
		
				EmitTriangle(a, b, c, cellSize, Color.white);
			}
		};

		public class MeshingJob {
			TerrainNode node;

			public JobHandle? jobHandle = null;
			CalcNodeJob job;
			
			public MeshingJob (TerrainNode node, int childrenMask, float iso, float DCIterStrength, int DCMaxIterations) {
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
			public void Apply (TerrainNode node) {
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
			TerrainNode node;
			TerrainNode[] neighbours;

			MeshingJob meshingJob;

			JobHandle? jobHandle;
			CalcNodeSeamJob job;
			
			public SeamMeshingJob (TerrainOctree octree, TerrainNode node) {
				
				neighbours = new TerrainNode[3*3*3];
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
				job.Node.Size = TerrainNode.VOXEL_COUNT << node.Lod;
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
						neighStruct.Size = TerrainNode.VOXEL_COUNT << neigh.Lod;
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
		
		public unsafe struct Cell {
			public bool		active;
			public float3	vertex;

			// ex. Y is the direction the edge is going, 00 01 10 11 are the XZ components, 0 is lower 1 is the higher edge on that axis rel to cell
			//public int[] = {
			//			edgeX00, edgeX10, edgeX01, edgeX11,
			//			edgeY00, edgeY10, edgeY01, edgeY11,
			//			edgeZ00, edgeZ10, edgeZ01, edgeZ11 }
			public fixed int edges[12];

			public int getEdgeIndex (int i) {
				return edges[i -1];
			}
		}

		public struct Edge {
			public int axis;
			public int3 index; // where the edge is in the grid

			public bool flipFace;

			public float3 pos; // position of approximated iso crossing
			public float3 normal; // normalized gradient at approximated iso crossing

			public int3 GetCellIndex (int edge) {
				var cellIndex = index;
				cellIndex[axis == 0 ? 1 : 0] -= (edge >> 0) & 1;
				cellIndex[axis == 2 ? 1 : 2] -= (edge >> 1) & 1;
				return cellIndex;
			}
		}
		
		static int AddEdge (NativeList<Edge> edges, int axis, int3 index, bool signA, Voxel voxA, Voxel voxB, float3 posA, float3 posB, float iso) {
			var edge = new Edge();

			float t = unlerp(voxA.distance, voxB.distance, iso); // approximate position of the isosurface by linear interpolation

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

				if ((childrenMask & (1 <<_3dToFlatIndex(index / (TerrainNode.VOXEL_COUNT/2), 2))) != 0)
					return;

				var posA = index;
				var posB = index + int3(1,0,0);
				var posC = index + int3(0,1,0);
				var posD = index + int3(0,0,1);
						
				bool voxBValid = all(posB < TerrainNode.VOXEL_COUNT+1);
				bool voxCValid = all(posC < TerrainNode.VOXEL_COUNT+1);
				bool voxDValid = all(posD < TerrainNode.VOXEL_COUNT+1);
						
				Voxel voxA;
				Voxel voxB = default;
				Voxel voxC = default;
				Voxel voxD = default;

								voxA = Voxels[_3dToFlatIndex(posA, TerrainNode.VOXEL_COUNT+1)];
				if (voxBValid) voxB = Voxels[_3dToFlatIndex(posB, TerrainNode.VOXEL_COUNT+1)];
				if (voxCValid) voxC = Voxels[_3dToFlatIndex(posC, TerrainNode.VOXEL_COUNT+1)];
				if (voxDValid) voxD = Voxels[_3dToFlatIndex(posD, TerrainNode.VOXEL_COUNT+1)];

				bool signA =              (voxA.distance < iso);
				bool edgeX = voxBValid && (voxA.distance < iso) != (voxB.distance < iso);
				bool edgeY = voxCValid && (voxA.distance < iso) != (voxC.distance < iso);
				bool edgeZ = voxDValid && (voxA.distance < iso) != (voxD.distance < iso);

				int NV = TerrainNode.VOXEL_COUNT;

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
				for (int z=0; z<TerrainNode.VOXEL_COUNT+1; ++z) {
					for (int y=0; y<TerrainNode.VOXEL_COUNT+1; ++y) {
						for (int x=0; x<TerrainNode.VOXEL_COUNT+1; ++x) {
							FindActive(x,y,z);
						}
					}
				}
				
				// Calculate vertices positions
				for (int z=0; z<TerrainNode.VOXEL_COUNT; ++z) {
					for (int y=0; y<TerrainNode.VOXEL_COUNT; ++y) {
						for (int x=0; x<TerrainNode.VOXEL_COUNT; ++x) {
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
				
				float size = NodeSize / TerrainNode.VOXEL_COUNT;

				// Output the face for each active edge
				for (int i=0; i<data.edges.Length; ++i) {
					var cell0 = data.edges[i].GetCellIndex(0);
					var cell1 = data.edges[i].GetCellIndex(1);
					var cell2 = data.edges[i].GetCellIndex(2);
					var cell3 = data.edges[i].GetCellIndex(3);
				
					if (	all(cell0 >= 0 & cell0 < TerrainNode.VOXEL_COUNT) &&
							all(cell1 >= 0 & cell1 < TerrainNode.VOXEL_COUNT) &&
							all(cell2 >= 0 & cell2 < TerrainNode.VOXEL_COUNT) &&
							all(cell3 >= 0 & cell3 < TerrainNode.VOXEL_COUNT)) {
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
			
				var voxA = Node.Voxels[_3dToFlatIndex(posA, TerrainNode.VOXEL_COUNT+1)];
				var voxB = Node.Voxels[_3dToFlatIndex(posB, TerrainNode.VOXEL_COUNT+1)];
			
				bool signA = voxA.distance < iso ^ axis == 1; // flip y faces because unity is left handed y up and i usually think right-handed with z up, somewhere my though process caused the y faces to be flipped
				bool edge = (voxA.distance < iso) != (voxB.distance < iso);
			
				if (edge) {
					AddEdge(Node.data.seamEdges, axis, index, signA, voxA, voxB, posA, posB, iso);
				}
			}
			
			bool GetCellOrNeighbourCell (int3 index, out Cell cell) {
				var neighbOffs = select(-1, 0, index >= 0);
				neighbOffs = select(neighbOffs, +1, index >= TerrainNode.VOXEL_COUNT);
				
				if (!GetNeighbour(neighbOffs, out Node neighb)) {
					if (any(index < 0) || any(index >= TerrainNode.VOXEL_COUNT)) {
						cell = default;
						return false;
					}
			
					Node.data.GetActiveCell(index, out cell);
					return true;
				}
			
				int3 pos = index;
				pos *= Node.Size / TerrainNode.VOXEL_COUNT;
				pos += Node.Pos - Node.Size/2;
				pos -= neighb.Pos - neighb.Size/2;
				pos /= neighb.Size / TerrainNode.VOXEL_COUNT;
			
				bool cellActive = neighb.data.GetActiveCell(pos, out cell);
			
				if (!cellActive) {
					// TODO: rare special case
					cell = new Cell();
					//CalcVertex(index, ref cell, node);
					cell.vertex = (float3)pos + 0.5f;
				}
			
				cell.vertex *= neighb.Size / TerrainNode.VOXEL_COUNT;
				cell.vertex += neighb.Pos - neighb.Size/2;
				cell.vertex -= Node.Pos - Node.Size/2;
				cell.vertex /= Node.Size / TerrainNode.VOXEL_COUNT;
				return true;
			}
			
			public void Execute () {
				//cells = new Cell[TerrainNode.VOXEL_COUNT, TerrainNode.VOXEL_COUNT, TerrainNode.VOXEL_COUNT]; // assume zeroed
				//edges = new List<Edge>();
			
				int CV = TerrainNode.VOXEL_COUNT;
			
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
				
				float size = Node.Size / TerrainNode.VOXEL_COUNT;
			
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
	}
}
