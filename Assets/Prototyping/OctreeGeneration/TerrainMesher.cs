using Unity.Jobs;
using Unity.Mathematics;
using static Unity.Mathematics.math;
using Unity.Collections;
using UnityEngine;
using Unity.Burst;
using System.Collections.Generic;
using UnityEngine.Profiling;

namespace OctreeGeneration {
	
	[RequireComponent(typeof(TerrainOctree), typeof(TerrainVoxelizer))]
	public class TerrainMesher : MonoBehaviour {
		
		public void ManualUpdateStartJobs (List<TerrainNode> sortedNodes, TerrainOctree octree) {
			
			var dc = new DualContouring();

			Profiler.BeginSample("StartJob loop");
			for (int i=0; i<sortedNodes.Count; ++i) {
				var node = sortedNodes[i];
				if (	node.needsRemesh && // remesh was flagged
						node.voxels != null // we have voxels yet (if these voxels are up to date or if there if already a voxelize job is handled by the octree)
						) {
					dc.CalcNode(node, octree.VoxelSize, octree.ChunkVoxels);
				}
			}
			Profiler.EndSample();
		}
		public void ManualUpdateFinishJobs (List<TerrainNode> sortedNodes, TerrainOctree octree) {
			
		}
		
		
	}

	struct DualContouring {
		
		unsafe struct Cell {
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
		}
		struct Edge {
			public int axis;
			public int3 index; // where the edge is in the grid

			public bool flipFace;

			public float3 pos; // position of approximated iso crossing
			public float3 gradient; // gradient at approximated iso crossing

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
			edge.gradient = lerp(voxA.gradientAnalytic, voxB.gradientAnalytic, t);

			int indx = edges.Count;
			edges.Add(edge);
			return indx;
		}

		void CalcVertex (int3 pos, ref Cell cell) {
			cell.vertex = (float3)pos + 0.5f;

			//for (int axis=0; axis<3; ++axis) {
			//	for (int a=0; a<4; ++a) {
			//		for (int b=0; b<4; ++b) {
			//			cell.edges
			//		}
			//	}
			//}
		}
		
		List<Vector3> vertices;
		List<Vector3> normals;
		List<Vector2> uv;
		List<Color> colors;
		List<int> triangles;

		void emitTriangle (int3 indxA, int3 indxB, int3 indxC) {
			var a = cells[indxA.z,indxA.y,indxA.x];
			var b = cells[indxB.z,indxB.y,indxB.x];
			var c = cells[indxC.z,indxC.y,indxC.x];

			vertices.Add(a.vertex);
			vertices.Add(b.vertex);
			vertices.Add(c.vertex);

			var flatNormal = cross(b.vertex - a.vertex, c.vertex - a.vertex);
			
			normals.Add(flatNormal);
			normals.Add(flatNormal);
			normals.Add(flatNormal);

			uv.Add(float2(0.5f));
			uv.Add(float2(0.5f));
			uv.Add(float2(0.5f));

			colors.Add(Color.white);
			colors.Add(Color.white);
			colors.Add(Color.white);

			int indx = triangles.Count;
			triangles.Add(indx++);
			triangles.Add(indx++);
			triangles.Add(indx++);
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
			
			// Three pass algo
			// Single pass might be possible

			// Pass 1: 
			// Check the three edges in the negative directions for each cell (this will process each edge)
			// Cheack each edge for iso level crossings and if any crosses happen, mark the cells it touches as active
			// Aproximate the iso crossing on the active edge by liner mapping iso levels and interpolate the gradient
			// finally output the active edges to a list and save their indices in the active cell
			for (int z=0; z<ChunkVoxels; ++z) {
				for (int y=0; y<ChunkVoxels; ++y) {
					for (int x=0; x<ChunkVoxels; ++x) {
						int3 index = int3(x,y,z);

						var posA = index;
						var posB = index + int3(1,0,0);
						var posC = index + int3(0,1,0);
						var posD = index + int3(0,0,1);

						var voxA = node.voxels.native[Voxels._3dToFlatIndex(posA, ChunkVoxels)];
						var voxB = node.voxels.native[Voxels._3dToFlatIndex(posB, ChunkVoxels)];
						var voxC = node.voxels.native[Voxels._3dToFlatIndex(posC, ChunkVoxels)];
						var voxD = node.voxels.native[Voxels._3dToFlatIndex(posD, ChunkVoxels)];

						bool signA = (voxA.distance < iso);
						bool edgeX = (voxA.distance < iso) != (voxB.distance < iso);
						bool edgeY = (voxA.distance < iso) != (voxC.distance < iso);
						bool edgeZ = (voxA.distance < iso) != (voxD.distance < iso);

						if (edgeX) {
							var edgeIndex = AddEdge(0, index, signA, voxA, voxB, posA, posB, iso);
							                    cells[z  , y  , x].SetEdge( 0, edgeIndex);
							if (         y > 0) cells[z  , y-1, x].SetEdge( 1, edgeIndex);
							if (z > 0)          cells[z-1, y  , x].SetEdge( 2, edgeIndex);
							if (z > 0 && y > 0) cells[z-1, y-1, x].SetEdge( 3, edgeIndex);
						}
						if (edgeY) {
							var edgeIndex = AddEdge(1, index, !signA, voxA, voxC, posA, posC, iso); // !signA to flip y faces because unity is left handed y up and i usually think right-handed with z up, somewhere my though process caused the y faces to be flipped
							                    cells[z  , y, x  ].SetEdge( 4, edgeIndex);
							if (         x > 0) cells[z  , y, x-1].SetEdge( 5, edgeIndex);
							if (z > 0)          cells[z-1, y, x  ].SetEdge( 6, edgeIndex);
							if (z > 0 && x > 0) cells[z-1, y, x-1].SetEdge( 7, edgeIndex);
						}
						if (edgeZ) {
							var edgeIndex = AddEdge(2, index, signA, voxA, voxD, posA, posD, iso);
							                    cells[z, y  , x  ].SetEdge( 8, edgeIndex);
							if (         x > 0) cells[z, y  , x-1].SetEdge( 9, edgeIndex);
							if (y > 0)          cells[z, y-1, x  ].SetEdge(10, edgeIndex);
							if (y > 0 && x > 0) cells[z, y-1, x-1].SetEdge(11, edgeIndex);
						}
					}
				}
			}
			
			for (int z=0; z<ChunkVoxels; ++z) {
				for (int y=0; y<ChunkVoxels; ++y) {
					for (int x=0; x<ChunkVoxels; ++x) {
						if (cells[z,y,x].active) {
							CalcVertex(int3(x,y,z), ref cells[z,y,x]);
						}
					}
				}
			}

			for (int i=0; i<edges.Count; ++i) {
				var cell0 = edges[i].GetCellIndex(0);
				var cell1 = edges[i].GetCellIndex(1);
				var cell2 = edges[i].GetCellIndex(2);
				var cell3 = edges[i].GetCellIndex(3);
				
				if (all(cell0 >= 0) && all(cell1 >= 0) & all(cell2 >= 0) & all(cell3 >= 0)) {
					if (edges[i].flipFace) {
						emitTriangle(cell0, cell1, cell2);
						emitTriangle(cell2, cell1, cell3);
					} else {
						emitTriangle(cell0, cell2, cell1);
						emitTriangle(cell1, cell2, cell3);
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
	}
}
