using System.Collections;
using System.Collections.Generic;
using Unity.Mathematics;
using static Unity.Mathematics.math;
using UnityEngine;

namespace OctreeGeneration {
	public class TerrainNodeDebug : MonoBehaviour {
		public TerrainOctree octree;
		public TerrainNode node;

		void drawGradientArrow (float3 pos, float3 norm) {
			Gizmos.DrawRay(pos + (float3)transform.position, norm * (node.coord.lod + 1) * 5);
		}

		void OnDrawGizmosSelected () {
			if (node != null && octree != null && octree.DrawNormals && node.mesh != null) {
				var vert = node.mesh.vertices;
				var norm = node.mesh.normals;
				for (int i=0; i<vert.Length; ++i)
					drawGradientArrow(vert[i], norm[i]);
			}
		}
	}
}
