using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace OctreeGeneration {
	public class TerrainVoxelizer {
		public static void Voxelize (TerrainNode node, int ChunkVoxels, TerrainGenerator gen) {
			var sw = new System.Diagnostics.Stopwatch();
			sw.Start();
				
			node.voxels = new Voxel[ChunkVoxels+1,ChunkVoxels+1,ChunkVoxels+1];
			
			for (int z=0; z<ChunkVoxels+1; ++z) {
				for (int y=0; y<ChunkVoxels+1; ++y) {
					for (int x=0; x<ChunkVoxels+1; ++x) {
						var pos_world = new Vector3(x,y,z);
						pos_world *= (node.size / ChunkVoxels);
						pos_world += -new Vector3(node.size,node.size,node.size) * 0.5f + node.pos;
						
						if (pos_world.x == -304 && pos_world.y >= 16 && pos_world.y <= 48 && pos_world.z == -80) {
							int a = 5;
						}

						node.voxels[z,y,x] = gen.Generate(pos_world);
					}
				}
			}
			
			sw.Start();

			Debug.Log("TerrainVoxelizer.Voxelize() took "+ sw.Elapsed.TotalMilliseconds +" ms");
		}
	}
}
