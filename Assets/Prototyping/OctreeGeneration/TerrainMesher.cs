using Unity.Jobs;
using Unity.Mathematics;
using static Unity.Mathematics.math;
using Unity.Collections;
using UnityEngine;
using Unity.Burst;
using System.Collections.Generic;
using UnityEngine.Profiling;
using System;
using static OctreeGeneration.VoxelUtil;
using System.Threading;

namespace OctreeGeneration {
	
	public class TerrainMesher : MonoBehaviour {
		
		List<TerrainNode> _sortedNodes;
		TerrainOctree _octree;
		
		public float DCIterStrength = 1f;
		public int DCMaxIterations = 5;
		float prevDCIterStrength;
		int prevDCIterations;
		
		readonly int3[] neighbourDirs = new int3[] {
			int3(-1,-1,-1), int3( 0,-1,-1), int3(+1,-1,-1),
			int3(-1, 0,-1), int3( 0, 0,-1), int3(+1, 0,-1),
			int3(-1,+1,-1), int3( 0,+1,-1), int3(+1,+1,-1),

			int3(-1,-1, 0), int3( 0,-1, 0), int3(+1,-1, 0),
			int3(-1, 0, 0),	                int3(+1, 0, 0),
			int3(-1,+1, 0), int3( 0,+1, 0), int3(+1,+1, 0),

			int3(-1,-1,+1), int3( 0,-1,+1), int3(+1,-1,+1),
			int3(-1, 0,+1), int3( 0, 0,+1), int3(+1, 0,+1),
			int3(-1,+1,+1), int3( 0,+1,+1), int3(+1,+1,+1),
		};
		Dictionary<int3, TerrainNode> neighbours = new Dictionary<int3, TerrainNode>();
		
		void FlagSeamRemesh (TerrainNode n) { // TODO: This flags too many nodes, because only some of them actually neighbour the node that caused the flagging
			n.needsSeamRemesh = true;
			if (n.Children != null) {
				for (int i=0; i<8; ++i) {
					FlagSeamRemesh(n.Children[i]);
				}
			}
		}
		
		List<RunningNodeCalcJob> runningJobs = new List<RunningNodeCalcJob>();

		public int MaxJobs = 3;

		public void ManualUpdateStartJobs (List<TerrainNode> sortedNodes, TerrainOctree octree) {
			if (DCIterStrength != prevDCIterStrength || DCMaxIterations != prevDCIterations) {

			}
			prevDCIterStrength = DCIterStrength;
			prevDCIterations = DCMaxIterations;

			_sortedNodes = sortedNodes;
			_octree = octree;

			Profiler.BeginSample("StartJob loop");
			for (int i=0; i<sortedNodes.Count; ++i) {
				var node = sortedNodes[i];

				bool remesh =	node.needsRemesh && // remesh was flagged
								node.voxels != null; // we have voxels yet (if these voxels are up to date or if there if already a voxelize job is handled by the octree)
				//bool seamRemesh = node.needsSeamRemesh &&
				//				  node.voxels != null;

				if (	remesh && // A remesh was flagged
						runningJobs.Find(x => x.node == node) == null && // not running yet
						runningJobs.Count < MaxJobs // not too many jobs yet
						) {

					int childrenMask = 0;
					for (int j=0; j<8; ++j)
						if (node.Children[j] != null && node.Children[j].mesh != null)
							childrenMask |= 1 << j;
					
					runningJobs.Add( RunningNodeCalcJob.Start(node, childrenMask, octree.VoxelSize, octree.NodeVoxels, DCIterStrength, DCMaxIterations) );
				}
				
				//if (seamRemesh) {
				//	for (int j=0; j<neighbourDirs.Length; ++j) {
				//		var neighb = _octree.GetNeighbourTree(node, neighbourDirs[j]);
				//		if (neighb != null) {
				//			
				//			if (remesh) {
				//				FlagSeamRemesh(neighb);
				//			}
				//
				//			if (neighb.IsLeaf)
				//				neighbours.Add(neighbourDirs[j], neighb);
				//		}
				//	}
				//
				//	dc.CalcNodeSeam(node, neighbours, _octree.VoxelSize, _octree.NodeVoxels);
				//}
				//
				//neighbours.Clear();
			}
			Profiler.EndSample();
		}
		public void ManualUpdateFinishJobs (List<TerrainNode> sortedNodes, TerrainOctree octree) {
			
			Profiler.BeginSample("Finish Jobs");
			for (int i=0; i<runningJobs.Count; ++i) {
				var job = runningJobs[i];

				if (job.JobHandle.IsCompleted) {
					job.Finish();
					runningJobs.RemoveAtSwapBack(i);
				} else {
					++i;
				}
			}
			Profiler.EndSample();
		}
		
		void OnGUI () {
			GUI.Label(new Rect(0, 60, 500,30), "Mesher    Jobs: ");

			for (int i=0; i<runningJobs.Count; ++i) {
				var job = runningJobs[i];
				int3 name = (int3)floor(job.node.pos / job.node.size);

				GUI.color = TerrainOctree.drawColors[clamp(job.node.lod % TerrainOctree.drawColors.Length, 0, TerrainOctree.drawColors.Length-1)];

				GUI.Label(new Rect(100 + i*100, 40, 100,30), name.ToString());
			}
		}

		////

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
		
		[BurstCompile]
		struct CalcNodeJob : IJob {
			[ReadOnly] public int childrenMask;
			[ReadOnly] public float NodeSize;
			[ReadOnly] public int NodeVoxels;
			[ReadOnly] public float iso;
			[ReadOnly] public float DCIterStrength;
			[ReadOnly] public int DCMaxIterations;
			[ReadOnly] public NativeArray<Voxel> Voxels;
		
			public NativeArray<Cell> Cells;
			public NativeList<Edge> Edges;
			
			[WriteOnly] public NativeList<float3> vertices;
			[WriteOnly] public NativeList<float3> normals;
			[WriteOnly] public NativeList<float2> uv;
			[WriteOnly] public NativeList<Color>  colors;
			            public NativeList<int>    triangles;
			
			int AddEdge (int axis, int3 index, bool signA, Voxel voxA, Voxel voxB, float3 posA, float3 posB, float iso) {
				var edge = new Edge();

				float t = unlerp(voxA.distance, voxB.distance, iso); // approximate position of the isosurface by linear interpolation

				edge.axis = axis;
				edge.index = index;

				edge.flipFace = signA;

				edge.pos = lerp(posA, posB, t);
				edge.normal = normalizesafe( lerp(voxA.gradient, voxB.gradient, t) );

				int indx = Edges.Length;
				Edges.Add(edge);
				return indx;
			}

			unsafe void SetEdge (int3 cellIndex, int edge, int edgeIndex) {
				int i = _3dToFlatIndex(cellIndex, NodeVoxels);
				var cell = Cells[i];

				cell.active = true;
				cell.edges[edge] = edgeIndex + 1; // store index in edge list + 1, 0 means edge is inactive

				Cells[i] = cell;
			}

			void FindActive (int x, int y, int z) {
				int ArraySize = NodeVoxels + 1;

				int3 index = int3(x,y,z);

				if ((childrenMask & (1 <<_3dToFlatIndex(index / (NodeVoxels/2), 2))) != 0)
					return;

				var posA = index;
				var posB = index + int3(1,0,0);
				var posC = index + int3(0,1,0);
				var posD = index + int3(0,0,1);
						
				bool voxBValid = all(posB < ArraySize);
				bool voxCValid = all(posC < ArraySize);
				bool voxDValid = all(posD < ArraySize);
						
				Voxel voxA;
				Voxel voxB = default;
				Voxel voxC = default;
				Voxel voxD = default;

							   voxA = Voxels[_3dToFlatIndex(posA, ArraySize)];
				if (voxBValid) voxB = Voxels[_3dToFlatIndex(posB, ArraySize)];
				if (voxCValid) voxC = Voxels[_3dToFlatIndex(posC, ArraySize)];
				if (voxDValid) voxD = Voxels[_3dToFlatIndex(posD, ArraySize)];

				bool signA =              (voxA.distance < iso);
				bool edgeX = voxBValid && (voxA.distance < iso) != (voxB.distance < iso);
				bool edgeY = voxCValid && (voxA.distance < iso) != (voxC.distance < iso);
				bool edgeZ = voxDValid && (voxA.distance < iso) != (voxD.distance < iso);

				int NV = NodeVoxels;

				if (edgeX) {
					var edgeIndex = AddEdge(0, index, signA, voxA, voxB, posA, posB, iso);
					if (z < NV && y < NV) SetEdge(int3(x, y  , z  ),  0, edgeIndex); // mark active edge for this cell
					if (z < NV && y >  1) SetEdge(int3(x, y-1, z  ),  1, edgeIndex); // mark active edge for the cells that edge also neighbours
					if (z >  1 && y < NV) SetEdge(int3(x, y  , z-1),  2, edgeIndex);
					if (z >  1 && y >  1) SetEdge(int3(x, y-1, z-1),  3, edgeIndex);
				}
				if (edgeY) {
					var edgeIndex = AddEdge(1, index, !signA, voxA, voxC, posA, posC, iso); // !signA to flip y faces because unity is left handed y up and i usually think right-handed with z up, somewhere my though process caused the y faces to be flipped
					if (z < NV && x < NV) SetEdge(int3(x  , y, z  ),  4, edgeIndex);
					if (z < NV && x >  0) SetEdge(int3(x-1, y, z  ),  5, edgeIndex);
					if (z >  0 && x < NV) SetEdge(int3(x  , y, z-1),  6, edgeIndex);
					if (z >  0 && x >  0) SetEdge(int3(x-1, y, z-1),  7, edgeIndex);
				}
				if (edgeZ) {
					var edgeIndex = AddEdge(2, index, signA, voxA, voxD, posA, posD, iso);
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
						var edge = Edges[edgeIndex -1];
						
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
							var edge = Edges[edgeIndex -1];
						
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
			
			void emitTriangle (Cell a, Cell b, Cell c, float cellSize, Color col) {
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
			
			void emitTriangle (int3 indxA, int3 indxB, int3 indxC, float cellSize) {
				var a = Cells[_3dToFlatIndex(indxA, NodeVoxels)];
				var b = Cells[_3dToFlatIndex(indxB, NodeVoxels)];
				var c = Cells[_3dToFlatIndex(indxC, NodeVoxels)];

				emitTriangle(a, b, c, cellSize, Color.white);
			}

			public void Execute () {
				int ArraySize = NodeVoxels + 1;
				
				// Find active edges and cells
				for (int z=0; z<ArraySize; ++z) {
					for (int y=0; y<ArraySize; ++y) {
						for (int x=0; x<ArraySize; ++x) {
							FindActive(x,y,z);
						}
					}
				}

				//int dbgCount = 0;
				
				// Calculate vertices positions
				for (int z=0; z<NodeVoxels; ++z) {
					for (int y=0; y<NodeVoxels; ++y) {
						for (int x=0; x<NodeVoxels; ++x) {
							int3 index = int3(x,y,z);

							int i = _3dToFlatIndex(index, NodeVoxels);

							Cell cell = Cells[i];
							if (cell.active) {

								float3 vertex = DualContourIterative(index, cell);

								//vertex = clamp(vertex, (float3)index, (float3)(index + 1));

								cell.vertex = vertex;
								Cells[i] = cell;

								//dbgCount++;
							}
						}
					}
				}

				//if (dbgCount > 0)
				//	Thread.Sleep(500);
				
				float size = NodeSize / NodeVoxels;

				// Output the face for each active edge
				for (int i=0; i<Edges.Length; ++i) {
					var cell0 = Edges[i].GetCellIndex(0);
					var cell1 = Edges[i].GetCellIndex(1);
					var cell2 = Edges[i].GetCellIndex(2);
					var cell3 = Edges[i].GetCellIndex(3);
				
					if (	all(cell0 >= 0 & cell0 < NodeVoxels) &&
							all(cell1 >= 0 & cell1 < NodeVoxels) &&
							all(cell2 >= 0 & cell2 < NodeVoxels) &&
							all(cell3 >= 0 & cell3 < NodeVoxels)) {
						if (Edges[i].flipFace) {
							emitTriangle(cell0, cell1, cell2, size);
							emitTriangle(cell2, cell1, cell3, size);
						} else {
							emitTriangle(cell1, cell0, cell3, size);
							emitTriangle(cell3, cell0, cell2, size);
						}
					}
				}
			}
		}
		
		class RunningNodeCalcJob {
			public TerrainNode node;
			public JobHandle JobHandle;
			public CalcNodeJob job;

			public static RunningNodeCalcJob Start (TerrainNode node, int childrenMask, float VoxelSize, int NodeVoxels, float DCIterStrength, int DCMaxIterations) {
				int ArraySize = NodeVoxels + 1;
				
				int edgeAlloc = ArraySize * ArraySize * 4;
				int vertexAlloc = ArraySize * ArraySize * 6;
			
				node.voxels.IncRef();

				var job = new RunningNodeCalcJob { node = node };

				job.job = new CalcNodeJob {
					childrenMask = childrenMask,
					NodeSize = node.size,
					NodeVoxels = NodeVoxels,
					iso = 0.0f,
					DCIterStrength = DCIterStrength,
					DCMaxIterations = DCMaxIterations,
					Voxels = node.voxels.native,
					
					Cells = new NativeArray<Cell>(NodeVoxels * NodeVoxels * NodeVoxels, Allocator.Persistent),
					Edges = new NativeList<Edge>(edgeAlloc, Allocator.Persistent),
			
					vertices  = new NativeList<float3> (vertexAlloc, Allocator.Persistent),
					normals   = new NativeList<float3> (vertexAlloc, Allocator.Persistent),
					uv        = new NativeList<float2> (vertexAlloc, Allocator.Persistent),
					colors    = new NativeList<Color>  (vertexAlloc, Allocator.Persistent),
					triangles = new NativeList<int>	   (vertexAlloc, Allocator.Persistent),
				};

				job.JobHandle = job.job.Schedule();

				return job;
			}

			public void Finish () {
				Profiler.BeginSample("FinishJob");
				JobHandle.Complete();

				if (!node.IsDestroyed)
					node.AssignMesh(job.vertices, job.normals, job.uv, job.colors, job.triangles);
				
				job.Cells.Dispose();
				job.Edges.Dispose();
			
				job.vertices  .Dispose();
				job.normals   .Dispose();
				job.uv        .Dispose();
				job.colors    .Dispose();
				job.triangles .Dispose();

				node.needsRemesh = false;

				node.voxels.DecRef();
				Profiler.EndSample();
			}
		}
		
		void OnDestroy () {
			Profiler.BeginSample("OnDestroy");
			foreach (var job in runningJobs)
				job.Finish(); // block main thread
			runningJobs.Clear();
			Profiler.EndSample();
		}
	}

	public struct DualContouring {
		
		#if false
		void ProcessEdgeCell (int3 index, int axis, TerrainNode node, int NodeVoxels, float iso) {
			
			var posA = index;
			var posB = index;
			posB[axis] += 1;

			var voxA = node.voxels.native[_3dToFlatIndex(posA, NodeVoxels+1)];
			var voxB = node.voxels.native[_3dToFlatIndex(posB, NodeVoxels+1)];

			bool signA = voxA.distance < iso ^ axis == 1; // flip y faces because unity is left handed y up and i usually think right-handed with z up, somewhere my though process caused the y faces to be flipped
			bool edge = (voxA.distance < iso) != (voxB.distance < iso);

			if (edge) {
				if (all(index == 0) && axis == 0) {
					int a = 5;
				}

				AddEdge(axis, index, signA, voxA, voxB, posA, posB, iso);
			}
		}

		bool GetCellOrNeighbourCell (int3 index, TerrainNode node, Dictionary<int3, TerrainNode> neighbours, int NodeVoxels, out Cell cell) {
			var neighbOffs = select(-1, 0, index >= 0);
			neighbOffs = select(neighbOffs, +1, index >= NodeVoxels);
			
			neighbours.TryGetValue(neighbOffs, out TerrainNode neighb);
			
			if (neighb == null || neighb.DCCells == null) {
				if (any(index < 0) || any(index >= NodeVoxels) || node.DCCells == null) {
					cell = default;
					return false;
				}

				cell = node.DCCells[index.z, index.y, index.x];
				return true;
			}
			
			if (node.coord.lod != neighb.coord.lod) {
				int a = 5;
			}

			int3 nodePos   = node  .coord.ToWorldCubeInt(NodeVoxels, out int nodeSize);
			int3 neighbPos = neighb.coord.ToWorldCubeInt(NodeVoxels, out int neighbSize);

			int3 pos = index;
			pos *= nodeSize / NodeVoxels;
			pos += nodePos - nodeSize/2;
			pos -= neighbPos - neighbSize/2;
			pos /= neighbSize / NodeVoxels;
			
			cell = neighb.DCCells[pos.z, pos.y, pos.x];

			if (!cell.active) {
				// TODO: rare speacial case
				cell = new Cell();
				//CalcVertex(index, ref cell, node, NodeVoxels);
				cell.active = true;
				cell.vertex = (float3)pos + 0.5f;
			}
			
			cell.vertex *= neighbSize / NodeVoxels;
			cell.vertex += neighbPos - neighbSize/2;
			cell.vertex -= nodePos - nodeSize/2;
			cell.vertex /= nodeSize / NodeVoxels;
			return true;
		}

		public void CalcNodeSeam (TerrainNode node, Dictionary<int3, TerrainNode> neighbours, float VoxelSize, int NodeVoxels) {

			vertices	= new List<Vector3>();
			normals		= new List<Vector3>();
			uv			= new List<Vector2>();
			colors		= new List<Color>();
			triangles	= new List<int>();
	
			float iso = 0f;
			
			int ArraySize = NodeVoxels + 1;
			
			cells = new Cell[NodeVoxels, NodeVoxels, NodeVoxels]; // assume zeroed
			edges = new List<Edge>();

			int CV = NodeVoxels;
			
			bool n100 = neighbours.TryGetValue(int3(1,0,0), out TerrainNode n100_) && n100_.coord.lod == node.coord.lod;
			bool n010 = neighbours.TryGetValue(int3(0,1,0), out TerrainNode n010_) && n010_.coord.lod == node.coord.lod;
			bool n001 = neighbours.TryGetValue(int3(0,0,1), out TerrainNode n001_) && n001_.coord.lod == node.coord.lod;
			
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
						if ((faceY || faceZ) && x < CV) ProcessEdgeCell(int3(x,y,z), 0, node, NodeVoxels, iso);
						if ((faceX || faceZ) && y < CV) ProcessEdgeCell(int3(x,y,z), 1, node, NodeVoxels, iso);
						if ((faceX || faceY) && z < CV) ProcessEdgeCell(int3(x,y,z), 2, node, NodeVoxels, iso);
					}
				}
			}

			//for (int y=0; y<CV+1; ++y) {
			//	for (int x=0; x<CV+1; ++x) {
			//		if (         x<CV)        ProcessEdgeCell(int3(x,y, 0), 0, node, NodeVoxels, iso); // x edges on z- plane
			//		if (!n100 && x<CV)        ProcessEdgeCell(int3(x,y,CV), 0, node, NodeVoxels, iso); // x edges on z+ plane
			//		//if (y<CV)        ProcessEdgeCell(int3(x,y, 0), 1, node, NodeVoxels, iso); // y edges on z- plane
			//		//if (y<CV)        ProcessEdgeCell(int3(x,y,CV), 1, node, NodeVoxels, iso); // y edges on z+ plane
			//	}
			//}
			//for (int z=0; z<CV+1; ++z) {
			//	for (int x=0; x<CV+1; ++x) {
			//		if (         x<CV && z>0) ProcessEdgeCell(int3(x, 0,z), 0, node, NodeVoxels, iso); // x edges on y- plane, x edges on z plane already processed
			//		if (!n100 && x<CV && z>0) ProcessEdgeCell(int3(x,CV,z), 0, node, NodeVoxels, iso); // x edges on y+ plane, x edges on z plane already processed
			//		//if (z<CV)        ProcessEdgeCell(int3(x, 0,z), 2, node, NodeVoxels, iso); // z edges on y- plane
			//		//if (z<CV)        ProcessEdgeCell(int3(x,CV,z), 2, node, NodeVoxels, iso); // z edges on y+ plane
			//	}
			//}
			//for (int z=0; z<CV+1; ++z) {
			//	for (int y=0; y<CV+1; ++y) {
			//		//if (y<CV && z>0) ProcessEdgeCell(int3( 0,y,z), 1, node, NodeVoxels, iso); // y edges on x- plane, y edges on z plane already processed
			//		//if (y<CV && z>0) ProcessEdgeCell(int3(CV,y,z), 1, node, NodeVoxels, iso); // y edges on x+ plane, y edges on z plane already processed
			//		//if (z<CV && y>0) ProcessEdgeCell(int3( 0,y,z), 2, node, NodeVoxels, iso); // z edges on x- plane, z edges on y plane already processed
			//		//if (z<CV && y>0) ProcessEdgeCell(int3(CV,y,z), 2, node, NodeVoxels, iso); // z edges on x+ plane, z edges on y plane already processed
			//	}
			//}

			float nodeSize;
			var nodePos = node.coord.ToWorldCube(VoxelSize, NodeVoxels, out nodeSize);
			
			float size = nodeSize / NodeVoxels;
			float3 origin = nodeSize * -0.5f;
			
			for (int i=0; i<edges.Count; ++i) {
				var cell0Index = edges[i].GetCellIndex(0);
				var cell1Index = edges[i].GetCellIndex(1);
				var cell2Index = edges[i].GetCellIndex(2);
				var cell3Index = edges[i].GetCellIndex(3);

				bool cell0B = GetCellOrNeighbourCell(cell0Index, node, neighbours, NodeVoxels, out Cell cell0);
				bool cell1B = GetCellOrNeighbourCell(cell1Index, node, neighbours, NodeVoxels, out Cell cell1);
				bool cell2B = GetCellOrNeighbourCell(cell2Index, node, neighbours, NodeVoxels, out Cell cell2);
				bool cell3B = GetCellOrNeighbourCell(cell3Index, node, neighbours, NodeVoxels, out Cell cell3);

				if (cell0B && cell1B && cell2B && cell3B) {
					if (edges[i].flipFace) {
						emitTriangle(cell0, cell1, cell2, size, origin, Color.green);
						emitTriangle(cell2, cell1, cell3, size, origin, Color.blue);
					} else {
						emitTriangle(cell1, cell0, cell3, size, origin, Color.green);
						emitTriangle(cell3, cell0, cell2, size, origin, Color.blue);
					}
				}
			}
			
			if (node.seamMesh == null) {
				node.seamMesh = new Mesh();
				node.seamMesh.name = "TerrainNode Seam Mesh";
				node.seamMesh.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32;
				node.seamGo.GetComponent<MeshFilter>().mesh = node.seamMesh;
			}
			node.seamMesh.Clear();
			
			node.seamMesh.SetVertices(vertices);
			node.seamMesh.SetNormals(normals);
			node.seamMesh.SetUVs(0, uv);
			node.seamMesh.SetColors(colors);
			node.seamMesh.SetTriangles(triangles, 0);

			node.needsSeamRemesh = false;
		}
		#endif
	}
}
