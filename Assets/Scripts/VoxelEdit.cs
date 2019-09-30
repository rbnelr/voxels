using UnityEngine;
using Unity.Mathematics;
using static Unity.Mathematics.math;
using static VoxelUtil;

class VoxelEdit {
	
	static bool Intersect (float3 boxCorner, float3 boxSize, float3 sphereCenter, float sphereRadius) {
		float3 pos = sphereCenter - boxCorner;
		float3 nearest = clamp(pos, 0, boxSize);
		float3 toSphere = pos - nearest;
		float dist = length(toSphere);
		return dist <= sphereRadius;
	}

	public static void SubstractSphere (float3 pos, float radius) {
		foreach (var c in Chunks.Instance.chunks.Values) {
			if (Intersect(c.Corner, Chunk.SIZE, pos, radius) && c.Voxels.IsCreated) {
				SubstractSphere(c, pos, radius);
			}
		}
	}

	public static void SubstractSphere (Chunk c, float3 pos, float radius) {
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
					float3 normal = normalize(dirTo);

					float t = saturate((radius - length(dirTo)) / radius);

					diggedDist = lerp(vox.value, diggedDist, t);
					normal = lerp(vox.gradient, normal, t);

					bool voxel_was_removed = vox.value < 0 && diggedDist >= 0;

					vox.gradient = normal;
					vox.value = max(vox.value, diggedDist);
					
					c.Voxels[i] = vox;
					
					if (voxel_was_removed) {
						float3 pos_world = (float3)index * Chunk.VOXEL_SIZE + c.Corner;

						FallingRocks.Instance.AddRock(pos_world);
					}
				}
			}
		}

		c.DeferRemesh = true;
	}
}
