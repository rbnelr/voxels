using UnityEngine;
using Unity.Mathematics;
using static Unity.Mathematics.math;
using static VoxelUtil;

public class DiggingTest : MonoBehaviour {
	
	public Chunks Chunks;
	public float Radius;

	void Update () {
		if (Input.GetMouseButtonDown(0)) {
			Dig(Chunks, transform.position, Radius);
		}
	}

	void OnDrawGizmos () {
		Gizmos.color = Color.red;
		Gizmos.DrawWireSphere(transform.position, Radius);
	}
	
	static bool Intersect (float3 boxCorner, float3 boxSize, float3 sphereCenter, float sphereRadius) {
		float3 pos = sphereCenter - boxCorner;
		float3 nearest = clamp(pos, 0, boxSize);
		float3 toSphere = pos - nearest;
		float dist = length(toSphere);
		return dist <= sphereRadius;
	}

	static void Dig (Chunks Chunks, float3 pos, float radius) {
		foreach (var c in Chunks.chunks.Values) {
			if (Intersect(c.Corner, Chunk.SIZE, pos, radius)) {
				SubstractSphere(c, pos, radius);
			}
		}
	}

	static void SubstractSphere (Chunk c, float3 pos, float radius) {
		int VOXELS = Chunk.VOXELS + 2;

		pos -= c.Corner;
		pos /= Chunk.VOXEL_SIZE;
		radius /= Chunk.VOXEL_SIZE;

		int3 lo = (int3)floor(pos - radius) + 1;
		int3 hi = (int3)ceil (pos + radius) + 1;
		lo = clamp(lo, 0, VOXELS);
		hi = clamp(hi, 0, VOXELS);
		
		for (int z=lo.z; z<hi.z; ++z) { 
			for (int y=lo.y; y<hi.y; ++y) { 
				for (int x=lo.x; x<hi.x; ++x) { 
					int3 index = int3(x,y,z);

					int i = _3dToFlatIndex(index, VOXELS);
					var vox = c.Voxels[i];

					float3 voxPos = (float3)index - 0.5f;
					float3 dirTo = pos - voxPos;

					float diggedDist = (radius - length(dirTo)) * Chunk.VOXEL_SIZE;

					vox.value = max(vox.value, diggedDist);
					vox.gradient = normalize(dirTo);

					c.Voxels[i] = vox;
				}
			}
		}

		c.DeferRemesh = true;
	}
}
