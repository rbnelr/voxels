using Unity.Jobs;
using Unity.Mathematics;
using static Unity.Mathematics.math;
using Unity.Collections;
using UnityEngine;
using Unity.Burst;
using System.Collections.Generic;
using UnityEngine.Profiling;
using System;

namespace OctreeGeneration {
	
	[RequireComponent(typeof(TerrainOctree), typeof(TerrainVoxelizer))]
	public class TerrainMesher : MonoBehaviour {
		
		List<TerrainNode> _sortedNodes;
		TerrainOctree _octree;
		
		DualContouring dc = new DualContouring();
		
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

		public void ManualUpdateStartJobs (List<TerrainNode> sortedNodes, TerrainOctree octree) {
			_sortedNodes = sortedNodes;
			_octree = octree;
			
			dc.NormalSmooth = 1;
			dc.DCIterStrength = octree.DCIterStrength;
			dc.DCMaxIterations = octree.DCMaxIterations;

			Profiler.BeginSample("StartJob loop");
			for (int i=0; i<sortedNodes.Count; ++i) {
				var node = sortedNodes[i];
				
				bool remesh =	node.needsRemesh && // remesh was flagged
								node.voxels != null; // we have voxels yet (if these voxels are up to date or if there if already a voxelize job is handled by the octree)
				bool seamRemesh = node.needsSeamRemesh &&
								  node.voxels != null &&
								  node.IsLeaf;

				if (remesh) {
					dc.CalcNode(node, octree.VoxelSize, octree.ChunkVoxels);
				}
				
				if (seamRemesh) {
					for (int j=0; j<neighbourDirs.Length; ++j) {
						var neighb = _octree.GetNeighbourTree(node, neighbourDirs[j]);
						if (neighb != null) {
							
							if (remesh) {
								FlagSeamRemesh(neighb);
							}

							if (neighb.IsLeaf)
								neighbours.Add(neighbourDirs[j], neighb);
						}
					}

					dc.CalcNodeSeam(node, neighbours, _octree.VoxelSize, _octree.ChunkVoxels);
				}

				neighbours.Clear();
			}
			Profiler.EndSample();
		}
		public void ManualUpdateFinishJobs (List<TerrainNode> sortedNodes, TerrainOctree octree) {
			
		}
	}

	public struct DualContouring {
		
		public float NormalSmooth;
		public float DCIterStrength;
		public int DCMaxIterations;

		public unsafe struct Cell {
			public bool		active;
			public float3	vertex;

			// ex. Y is the direction the edge is going, 00 01 10 11 are the XZ components, 0 is lower 1 is the higher edge on that axis rel to cell
			//public int[] = {
			//			edgeX00, edgeX10, edgeX01, edgeX11,
			//			edgeY00, edgeY10, edgeY01, edgeY11,
			//			edgeZ00, edgeZ10, edgeZ01, edgeZ11 }
			public fixed int edges[12];

			public void SetEdge (int edge, int edgeIndex) {
				active = true;
				edges[edge] = edgeIndex + 1; // store index in edge list + 1, 0 means edge is inactive
			}

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

		Cell[,,] cells;
		List<Edge> edges;

		int AddEdge (int axis, int3 index, bool signA, Voxel voxA, Voxel voxB, float3 posA, float3 posB, float iso) {
			var edge = new Edge();

			float t = unlerp(voxA.distance, voxB.distance, iso); // approximate position of the isosurface by linear interpolation

			edge.axis = axis;
			edge.index = index;

			edge.flipFace = signA;

			edge.pos = lerp(posA, posB, t);
			edge.normal = normalizesafe( lerp(voxA.gradient, voxB.gradient, t) );

			int indx = edges.Count;
			edges.Add(edge);
			return indx;
		}

		IEnumerable<Edge> getActiveCellEdges (Cell cell) {
			for (int i=0; i<12; ++i) {
				var edgeIndex = cell.getEdgeIndex(i);
				if (edgeIndex > 0) {
					var edge = edges[edgeIndex -1];
					yield return edge;
				}
			}
		}

		unsafe float3 massPoint (int3 cellPos, ref Cell cell, out float3 normal) {
			float3 avgPos = 0;
			float3 avgNormal = 0;
			int count = 0;
			
			foreach (var edge in getActiveCellEdges(cell)) {
				avgPos += edge.pos;
				avgNormal += edge.normal;
				count++;
			}

			avgPos /= count;
			avgNormal /= count;

			normal = avgNormal;
			return avgPos;
		}
		
		unsafe void SurfaceNets (int3 cellPos, ref Cell cell) {
			cell.vertex = massPoint(cellPos, ref cell, out float3 normal);
			//cell.normal = normal;
		}
		
		// https://www.boristhebrave.com/2018/04/15/dual-contouring-tutorial/
		
		unsafe void DualContourNumeric (int3 cellPos, ref Cell cell) {
			int res = 10;

			float3 minPos = 0;
			float minError = float.PositiveInfinity;

			for (int z=0; z<res; ++z) {
				for (int y=0; y<res; ++y) {
					for (int x=0; x<res; ++x) {
						float3 pos = (float3)int3(x,y,z) / res + cellPos;

						float error = 0;

						foreach (var edge in getActiveCellEdges(cell)) {

							var posRel = pos - edge.pos;

							float signedDistance = dot(edge.normal, posRel);

							float sqrError = signedDistance*signedDistance;

							error += sqrError;
						}

						{
							var posRel = pos - ((float3)cellPos + 0.5f);

							float sqrError = lengthsq(posRel) * 0.05f;

							error += sqrError;
						}

						if (error < minError) {
							minPos = pos;
							minError = error;
						}
					}
				}
			}

			cell.vertex = minPos;
		}

		unsafe void DualContourIterative (int3 cellPos, ref Cell cell, TerrainNode node, int ChunkVoxels) {
			// Instead of using a QEF solver, use a iterative method
			// This is my approach of solving this, this is basicly gradient descent which is used in machine learning
			// We know we want the best fit point based on a set of points with normals (called hermite?) which can be thought of as defining a plane
			// There should (usually) be a point somewhere (maybe outside the cell) that is the global minimum of distances to these planes
			// It seems Augusto Schmitz came up with something similar http://www.inf.ufrgs.br/~comba/papers/thesis/diss-leonardo.pdf - called Schimtz Particle Method by mattbick2003 - https://www.reddit.com/r/Unity3D/comments/bw6x1l/an_update_on_the_job_system_dual_contouring/
			
			float3 particle = massPoint(cellPos, ref cell, out float3 normal);
			//float3 particle = (float3)cellPos + 0.5f;
			//return;

			//cell.normal = normal;
			
			int iter = 0;
			while (iter++ < DCMaxIterations) {
				float3 sumForce = 0;
				
				int count = 0;
				
				foreach (var edge in getActiveCellEdges(cell)) {
					var posRel = particle - edge.pos;
				
					float signedDistance = dot(edge.normal, posRel);
					float signedSqrError = signedDistance * abs(signedDistance);
				
					float3 force = signedSqrError * -edge.normal;
				
					sumForce += force;
					count++;
				}

				sumForce /= count;
				
				#if false
				{
					var posRel = particle - ((float3)cellPos + 0.5f);
					
					float dist = length(posRel);
					float3 dir = dist > 0.001f ? posRel / dist : 0;
				
					dist -= 0.45f;
					dist = max(dist, 0); // only pull perticle to center of cell when its off by a lot
					
					float str = dist*dist * 0.05f;
				
					sumForce += -dir * str;
				}
				#endif

				particle += sumForce * DCIterStrength;
				
				particle = clamp(particle, (float3)cellPos, (float3)(cellPos + 1));
			}

			cell.vertex = particle;
		}

		unsafe void CalcVertex (int3 cellPos, ref Cell cell, TerrainNode node, int ChunkVoxels) {
			//SurfaceNets(cellPos, ref cell);
			//DualContourNumeric(cellPos, ref cell);
			DualContourIterative(cellPos, ref cell, node, ChunkVoxels);

			cell.vertex = clamp(cell.vertex, (float3)cellPos, (float3)(cellPos + 1));
		}
		
		List<Vector3> vertices;
		List<Vector3> normals;
		List<Vector2> uv;
		List<Color> colors;
		List<int> triangles;

		void emitTriangle (Cell a, Cell b, Cell c, float cellSize, float3 origin, Color col) {
			if (all(a.vertex == b.vertex) || all(a.vertex == c.vertex))
				return; // degenerate triangle
			
			vertices.Add(a.vertex * cellSize + origin);
			vertices.Add(b.vertex * cellSize + origin);
			vertices.Add(c.vertex * cellSize + origin);

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

			int indx = triangles.Count;
			triangles.Add(indx++);
			triangles.Add(indx++);
			triangles.Add(indx++);
		}

		void emitTriangle (int3 indxA, int3 indxB, int3 indxC, float cellSize, float3 origin) {
			var a = cells[indxA.z,indxA.y,indxA.x];
			var b = cells[indxB.z,indxB.y,indxB.x];
			var c = cells[indxC.z,indxC.y,indxC.x];

			emitTriangle(a, b, c, cellSize, origin, Color.white);
		}
		
		public void CalcNode (TerrainNode node, float VoxelSize, int ChunkVoxels) {
			
			vertices	= new List<Vector3>();
			normals		= new List<Vector3>();
			uv			= new List<Vector2>();
			colors		= new List<Color>();
			triangles	= new List<int>();
	
			float iso = 0f;
			
			int ArraySize = ChunkVoxels + 1;
			
			cells = new Cell[ChunkVoxels, ChunkVoxels, ChunkVoxels]; // assume zeroed
			edges = new List<Edge>();
			
			node.DCCells = cells;

			// Three pass algo
			// Single pass might be possible

			// Pass 1: 
			// Check the three edges in the negative directions for each cell (this will process each edge)
			// Check each edge for iso level crossings and if any crosses happen, mark the cells it touches as active
			// Aproximate the iso crossing on the active edge by liner mapping iso levels and interpolate the gradient
			// finally output the active edges to a list and save their indices in the active cell
			for (int z=0; z<ArraySize; ++z) {
				for (int y=0; y<ArraySize; ++y) {
					for (int x=0; x<ArraySize; ++x) {
						int3 index = int3(x,y,z);

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

						               voxA = node.voxels.native[Voxels._3dToFlatIndex(posA, ChunkVoxels)];
						if (voxBValid) voxB = node.voxels.native[Voxels._3dToFlatIndex(posB, ChunkVoxels)];
						if (voxCValid) voxC = node.voxels.native[Voxels._3dToFlatIndex(posC, ChunkVoxels)];
						if (voxDValid) voxD = node.voxels.native[Voxels._3dToFlatIndex(posD, ChunkVoxels)];

						bool signA =              (voxA.distance < iso);
						bool edgeX = voxBValid && (voxA.distance < iso) != (voxB.distance < iso);
						bool edgeY = voxCValid && (voxA.distance < iso) != (voxC.distance < iso);
						bool edgeZ = voxDValid && (voxA.distance < iso) != (voxD.distance < iso);

						int CV = ChunkVoxels;

						if (edgeX) {
							var edgeIndex = AddEdge(0, index, signA, voxA, voxB, posA, posB, iso);
							if (z < CV && y < CV) cells[z  , y  , x].SetEdge( 0, edgeIndex); // mark active edge for this cell
							if (z < CV && y >  1) cells[z  , y-1, x].SetEdge( 1, edgeIndex); // mark active edge for the cells that edge also neighbours
							if (z >  1 && y < CV) cells[z-1, y  , x].SetEdge( 2, edgeIndex);
							if (z >  1 && y >  1) cells[z-1, y-1, x].SetEdge( 3, edgeIndex);
						}
						if (edgeY) {
							var edgeIndex = AddEdge(1, index, !signA, voxA, voxC, posA, posC, iso); // !signA to flip y faces because unity is left handed y up and i usually think right-handed with z up, somewhere my though process caused the y faces to be flipped
							if (z < CV && x < CV) cells[z  , y, x  ].SetEdge( 4, edgeIndex);
							if (z < CV && x >  0) cells[z  , y, x-1].SetEdge( 5, edgeIndex);
							if (z >  0 && x < CV) cells[z-1, y, x  ].SetEdge( 6, edgeIndex);
							if (z >  0 && x >  0) cells[z-1, y, x-1].SetEdge( 7, edgeIndex);
						}
						if (edgeZ) {
							var edgeIndex = AddEdge(2, index, signA, voxA, voxD, posA, posD, iso);
							if (y < CV && x < CV) cells[z, y  , x  ].SetEdge( 8, edgeIndex);
							if (y < CV && x >  0) cells[z, y  , x-1].SetEdge( 9, edgeIndex);
							if (y >  0 && x < CV) cells[z, y-1, x  ].SetEdge(10, edgeIndex);
							if (y >  0 && x >  0) cells[z, y-1, x-1].SetEdge(11, edgeIndex);
						}
					}
				}
			}
			
			// Pass 2:
			// Cacluate Vertex location for each cell with QEF algorithm
			for (int z=0; z<ChunkVoxels; ++z) {
				for (int y=0; y<ChunkVoxels; ++y) {
					for (int x=0; x<ChunkVoxels; ++x) {
						if (cells[z,y,x].active) {
							CalcVertex(int3(x,y,z), ref cells[z,y,x], node, ChunkVoxels);
						}
					}
				}
			}
			
			float ChunkSize;
			node.coord.ToWorldCube(VoxelSize, ChunkVoxels, out ChunkSize);
			
			float size = ChunkSize / ChunkVoxels;
			float3 origin = ChunkSize * -0.5f;

			// Pass 3:
			// Output the face for each active edge
			for (int i=0; i<edges.Count; ++i) {
				var cell0 = edges[i].GetCellIndex(0);
				var cell1 = edges[i].GetCellIndex(1);
				var cell2 = edges[i].GetCellIndex(2);
				var cell3 = edges[i].GetCellIndex(3);
				
				if (	all(cell0 >= 0 & cell0 < ChunkVoxels) &&
						all(cell1 >= 0 & cell1 < ChunkVoxels) &&
						all(cell2 >= 0 & cell2 < ChunkVoxels) &&
						all(cell3 >= 0 & cell3 < ChunkVoxels)) {
					if (edges[i].flipFace) {
						emitTriangle(cell0, cell1, cell2, size, origin);
						emitTriangle(cell2, cell1, cell3, size, origin);
					} else {
						emitTriangle(cell1, cell0, cell3, size, origin);
						emitTriangle(cell3, cell0, cell2, size, origin);
					}
				}
			}

			if (node.mesh == null) {
				node.mesh = new Mesh();
				node.mesh.name = "TerrainChunk Mesh";
				node.mesh.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32;
				node.go.GetComponent<MeshFilter>().mesh = node.mesh;
			}

			node.mesh.Clear();
			
			node.mesh.SetVertices(vertices);
			node.mesh.SetNormals(normals);
			node.mesh.SetUVs(0, uv);
			node.mesh.SetColors(colors);
			node.mesh.SetTriangles(triangles, 0);

			node.needsRemesh = false;
		}

		void ProcessEdgeCell (int3 index, int axis, TerrainNode node, int ChunkVoxels, float iso) {
			
			var posA = index;
			var posB = index;
			posB[axis] += 1;

			var voxA = node.voxels.native[Voxels._3dToFlatIndex(posA, ChunkVoxels)];
			var voxB = node.voxels.native[Voxels._3dToFlatIndex(posB, ChunkVoxels)];

			bool signA = voxA.distance < iso ^ axis == 1; // flip y faces because unity is left handed y up and i usually think right-handed with z up, somewhere my though process caused the y faces to be flipped
			bool edge = (voxA.distance < iso) != (voxB.distance < iso);

			if (edge) {
				if (all(index == 0) && axis == 0) {
					int a = 5;
				}

				AddEdge(axis, index, signA, voxA, voxB, posA, posB, iso);
			}
		}

		bool GetCellOrNeighbourCell (int3 index, TerrainNode node, Dictionary<int3, TerrainNode> neighbours, int ChunkVoxels, out Cell cell) {
			var neighbOffs = select(-1, 0, index >= 0);
			neighbOffs = select(neighbOffs, +1, index >= ChunkVoxels);
			
			neighbours.TryGetValue(neighbOffs, out TerrainNode neighb);
			
			if (neighb == null || neighb.DCCells == null) {
				if (any(index < 0) || any(index >= ChunkVoxels) || node.DCCells == null) {
					cell = default;
					return false;
				}

				cell = node.DCCells[index.z, index.y, index.x];
				return true;
			}
			
			if (node.coord.lod != neighb.coord.lod) {
				int a = 5;
			}

			int3 nodePos   = node  .coord.ToWorldCubeInt(ChunkVoxels, out int nodeSize);
			int3 neighbPos = neighb.coord.ToWorldCubeInt(ChunkVoxels, out int neighbSize);

			int3 pos = index;
			pos *= nodeSize / ChunkVoxels;
			pos += nodePos - nodeSize/2;
			pos -= neighbPos - neighbSize/2;
			pos /= neighbSize / ChunkVoxels;
			
			cell = neighb.DCCells[pos.z, pos.y, pos.x];

			if (!cell.active) {
				// TODO: rare speacial case
				cell = new Cell();
				//CalcVertex(index, ref cell, node, ChunkVoxels);
				cell.active = true;
				cell.vertex = (float3)pos + 0.5f;
			}
			
			cell.vertex *= neighbSize / ChunkVoxels;
			cell.vertex += neighbPos - neighbSize/2;
			cell.vertex -= nodePos - nodeSize/2;
			cell.vertex /= nodeSize / ChunkVoxels;
			return true;
		}

		public void CalcNodeSeam (TerrainNode node, Dictionary<int3, TerrainNode> neighbours, float VoxelSize, int ChunkVoxels) {

			vertices	= new List<Vector3>();
			normals		= new List<Vector3>();
			uv			= new List<Vector2>();
			colors		= new List<Color>();
			triangles	= new List<int>();
	
			float iso = 0f;
			
			int ArraySize = ChunkVoxels + 1;
			
			cells = new Cell[ChunkVoxels, ChunkVoxels, ChunkVoxels]; // assume zeroed
			edges = new List<Edge>();

			int CV = ChunkVoxels;
			
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
						if ((faceY || faceZ) && x < CV) ProcessEdgeCell(int3(x,y,z), 0, node, ChunkVoxels, iso);
						if ((faceX || faceZ) && y < CV) ProcessEdgeCell(int3(x,y,z), 1, node, ChunkVoxels, iso);
						if ((faceX || faceY) && z < CV) ProcessEdgeCell(int3(x,y,z), 2, node, ChunkVoxels, iso);
					}
				}
			}

			//for (int y=0; y<CV+1; ++y) {
			//	for (int x=0; x<CV+1; ++x) {
			//		if (         x<CV)        ProcessEdgeCell(int3(x,y, 0), 0, node, ChunkVoxels, iso); // x edges on z- plane
			//		if (!n100 && x<CV)        ProcessEdgeCell(int3(x,y,CV), 0, node, ChunkVoxels, iso); // x edges on z+ plane
			//		//if (y<CV)        ProcessEdgeCell(int3(x,y, 0), 1, node, ChunkVoxels, iso); // y edges on z- plane
			//		//if (y<CV)        ProcessEdgeCell(int3(x,y,CV), 1, node, ChunkVoxels, iso); // y edges on z+ plane
			//	}
			//}
			//for (int z=0; z<CV+1; ++z) {
			//	for (int x=0; x<CV+1; ++x) {
			//		if (         x<CV && z>0) ProcessEdgeCell(int3(x, 0,z), 0, node, ChunkVoxels, iso); // x edges on y- plane, x edges on z plane already processed
			//		if (!n100 && x<CV && z>0) ProcessEdgeCell(int3(x,CV,z), 0, node, ChunkVoxels, iso); // x edges on y+ plane, x edges on z plane already processed
			//		//if (z<CV)        ProcessEdgeCell(int3(x, 0,z), 2, node, ChunkVoxels, iso); // z edges on y- plane
			//		//if (z<CV)        ProcessEdgeCell(int3(x,CV,z), 2, node, ChunkVoxels, iso); // z edges on y+ plane
			//	}
			//}
			//for (int z=0; z<CV+1; ++z) {
			//	for (int y=0; y<CV+1; ++y) {
			//		//if (y<CV && z>0) ProcessEdgeCell(int3( 0,y,z), 1, node, ChunkVoxels, iso); // y edges on x- plane, y edges on z plane already processed
			//		//if (y<CV && z>0) ProcessEdgeCell(int3(CV,y,z), 1, node, ChunkVoxels, iso); // y edges on x+ plane, y edges on z plane already processed
			//		//if (z<CV && y>0) ProcessEdgeCell(int3( 0,y,z), 2, node, ChunkVoxels, iso); // z edges on x- plane, z edges on y plane already processed
			//		//if (z<CV && y>0) ProcessEdgeCell(int3(CV,y,z), 2, node, ChunkVoxels, iso); // z edges on x+ plane, z edges on y plane already processed
			//	}
			//}

			float nodeSize;
			var nodePos = node.coord.ToWorldCube(VoxelSize, ChunkVoxels, out nodeSize);
			
			float size = nodeSize / ChunkVoxels;
			float3 origin = nodeSize * -0.5f;
			
			for (int i=0; i<edges.Count; ++i) {
				var cell0Index = edges[i].GetCellIndex(0);
				var cell1Index = edges[i].GetCellIndex(1);
				var cell2Index = edges[i].GetCellIndex(2);
				var cell3Index = edges[i].GetCellIndex(3);

				bool cell0B = GetCellOrNeighbourCell(cell0Index, node, neighbours, ChunkVoxels, out Cell cell0);
				bool cell1B = GetCellOrNeighbourCell(cell1Index, node, neighbours, ChunkVoxels, out Cell cell1);
				bool cell2B = GetCellOrNeighbourCell(cell2Index, node, neighbours, ChunkVoxels, out Cell cell2);
				bool cell3B = GetCellOrNeighbourCell(cell3Index, node, neighbours, ChunkVoxels, out Cell cell3);

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
				node.seamMesh.name = "TerrainChunk Seam Mesh";
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

	}
}
