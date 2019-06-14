using System.Collections;
using System.Collections.Generic;
using Unity.Collections;
using Unity.Jobs;
using UnityEngine;
using Unity.Mathematics;
using static Unity.Mathematics.math;

namespace OctreeGeneration {
	public class TerrainChunk {
		public readonly static int3[] childrenPos = new int3[] {
			new int3(-1,-1,-1),
			new int3( 1,-1,-1),
			new int3(-1, 1,-1),
			new int3( 1, 1,-1),
			new int3(-1,-1, 1),
			new int3( 1,-1, 1),
			new int3(-1, 1, 1),
			new int3( 1, 1, 1),
		};
		public TerrainChunk getChild (int x, int y, int z) { // x,y,z: [0,1]
			return children[z*4 + y*2 + x];
		}

		public TerrainChunk[] children;
		public TerrainChunk parent;
		public int depth;

		public float3 pos; // center of cube, must always be the center of the 8 sub cubes of the parent Chunk
		public float size; // must always be 1/2 of the size of the parent cube

		public GameObject go;
		public Mesh mesh;

		public NativeArray<Voxel>? voxels = null;
		
		public JobHandle? voxelizeJob;
		
		public bool needsRemesh = false;

		public Voxel GetVoxel (int3 pos, int ChunkVoxels) {
			int ArraySize = ChunkVoxels + 1;
			return voxels.Value[pos.z * ArraySize * ArraySize + pos.y * ArraySize + pos.x];
		}

		public void Dispose () {
			voxelizeJob?.Complete();
			voxels?.Dispose();
		}

		public void StartVoxelizeJob (int ChunkVoxels, TerrainGenerator gen) {
			int ArraySize = ChunkVoxels + 1;
			int voxelsLength = ArraySize * ArraySize * ArraySize;

			voxels?.Dispose();
			voxels = new NativeArray<Voxel>(voxelsLength, Allocator.Persistent);

			var job = new TerrainVoxelizeJob {
				ChunkPos = pos,
				ChunkSize = size,
				ChunkVoxels = ChunkVoxels,
				Gen = gen,
				Voxels = voxels.Value
			};
			voxelizeJob = job.Schedule(voxelsLength, ChunkVoxels);
		}
		public void finishVoxelizeJob () {
			voxelizeJob.Value.Complete();
			voxelizeJob = null;

			needsRemesh = true;
			
			if (parent != null)
				parent.needsRemesh = true;
		}
	}
	
	public class TerrainController : MonoBehaviour {
		
		public GameObject TerrainChunkPrefab;
		
		public TerrainGenerator generator;
		public GameObject player;
		float3 playerPos { get { return player.transform.position; } }

		public GameObject test;
		float3 testPos { get { return test.transform.position; } }
		public int3 testDir;

		public float VoxelSize = 1f;
		public int ChunkVoxels = 32;
		
		[Range(0, 15)]
		public int MaxLod = 7;

		TerrainChunk root;
		
		TerrainChunk _lookupOctree (TerrainChunk c, float3 worldPos, int depth, int maxDepth) {
			if (depth > maxDepth)
				return null;
			
			var hs = c.size/2;
			var pos = worldPos;
			pos -= c.pos - hs;
			pos /= hs;
			var posChild = VectorExt.FloorToInt(pos);
			if (	posChild.x >= 0 && posChild.x <= 1 &&
					posChild.y >= 0 && posChild.y <= 1 &&
					posChild.z >= 0 && posChild.z <= 1) {
				
				var child = c.getChild(posChild.x, posChild.y, posChild.z);
				if (child != null) {
					var childLookup = _lookupOctree(child, worldPos, depth+1, maxDepth);
					if (childLookup != null)	
						return childLookup; // in child octant
				}
				return c; // in octant that does not have a child
			} else {
				return null; // not in this Chunks octants
			}
		}
		public TerrainChunk LookupOctree (float3 worldPos, int maxDepth=int.MaxValue) { // Octree lookup which smallest Chunk contains the world space postion
			if (root == null) return null;
			return _lookupOctree(root, worldPos, 0, maxDepth);
		}
		public TerrainChunk GetNeighbourTree (TerrainChunk c, int x, int y, int z) {
			var posInNeighbour = c.pos + new float3(x,y,z) * (c.size + VoxelSize)/2;
			return LookupOctree(posInNeighbour, c.depth);
		}

		TerrainChunk newChunk (float size, float3 pos, int depth, TerrainChunk parent=null) {
			var c = new TerrainChunk {
				children = new TerrainChunk[8],
				parent = parent,
				depth = depth,

				pos = pos,
				size = size,
				
				go = Instantiate(TerrainChunkPrefab, pos, Quaternion.identity, this.gameObject.transform),
				mesh = new Mesh(),
			};

			c.mesh.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32;
			c.go.GetComponent<MeshFilter>().mesh = c.mesh;

			c.StartVoxelizeJob(ChunkVoxels, generator);

			return c;
		}
		void deleteChunk (TerrainChunk c) { // deletes all children recursivly
			Destroy(c.go);
			Destroy(c.mesh); // is this needed?
			for (int i=0; i<8; ++i)
				if (c.children[i] != null)
					deleteChunk(c.children[i]);

			if (c.parent != null)
				c.parent.needsRemesh = true;

			c.Dispose();
		}
		void updateChunkLod (TerrainChunk chunk, int depth=0) { // Creates and Deletes Chunks when needed according to Lod function
			if (chunk.voxelizeJob.HasValue && chunk.voxelizeJob.Value.IsCompleted) {
				chunk.finishVoxelizeJob();
			}

			if (depth == MaxLod) return;

			for (int i=0; i<8; ++i) {
				var child = chunk.children[i];

				var size = chunk.size / 2;
				var pos = chunk.pos + (float3)TerrainChunk.childrenPos[i] * size / 2;
				
				if (child != null) {
					Debug.Assert(all(child.pos == pos));
					Debug.Assert(child.size == size);
				}

				var closest = pos + clamp(playerPos - pos, -size/2, size/2);
				var dist = length(playerPos - closest);
				
				int ChunkLod = MaxLod - depth;
				var desiredLod = Mathf.FloorToInt(calcLod(dist));

				bool needChild = desiredLod < ChunkLod;

				if (needChild && child == null) {
					child = newChunk(size, pos, depth+1, chunk);
				} else if (!needChild && child != null) {
					deleteChunk(child);
					child = null;
				}

				if (child != null) {
					updateChunkLod(child, depth +1);
				}
				
				chunk.children[i] = child;
			}
		}
		
		int calcLod (float dist) {
			float m = 0.5f;
			float n = 16f;
			float l = 6f;

			float a = -l / ( Mathf.Log(m) - Mathf.Log(n) );
			float b = (l * Mathf.Log(m)) / ( Mathf.Log(m) - Mathf.Log(n) );
		
			float Lod0ChunkSize = VoxelSize * ChunkVoxels;
			var lod = Mathf.FloorToInt( a * Mathf.Log(dist / Lod0ChunkSize) + b );
			return lod;
		}

		int remeshLimiter = 0;

		void updateChunkMesh (TerrainChunk chunk) {
			for (int i=0; i<8; ++i) {
				if (chunk.children[i] != null)
					updateChunkMesh(chunk.children[i]);
			}

			if (chunk.needsRemesh && remeshLimiter == 0) {
				TerrainMesher.Meshize(chunk, ChunkVoxels, this);
				chunk.needsRemesh = false;
				remeshLimiter++;
			}
		}

		void disposeChunk (TerrainChunk chunk) {
			for (int i=0; i<8; ++i) {
				if (chunk.children[i] != null)
					disposeChunk(chunk.children[i]);
			}

			chunk.Dispose();
		}

		float _prevVoxelSize;
		int _prevChunkVoxels;
		int _prevMaxLod;

		void OnDestroy () {
			if (root != null)
				disposeChunk(root);
		}
		void Update () {
			if (	VoxelSize != _prevVoxelSize || 
					ChunkVoxels != _prevChunkVoxels ||
					MaxLod != _prevMaxLod ) {

				if (root != null)
					deleteChunk(root);
				root = null;
				_prevVoxelSize = VoxelSize;
				_prevChunkVoxels = ChunkVoxels;
				_prevMaxLod = MaxLod;
			}

			float Lod0ChunkSize = VoxelSize * ChunkVoxels;
			float RootChunkSize = Lod0ChunkSize * Mathf.Pow(2f, MaxLod);

			if (root == null) {
				root = newChunk(RootChunkSize, new float3(0,0,0), 0);
			}

			updateChunkLod(root);

			remeshLimiter = 0;
			updateChunkMesh(root);
		}

		static readonly Color[] drawColors = new Color[] {
			Color.blue,
			Color.cyan,
			Color.green,
			Color.red,
			Color.yellow,
			Color.magenta,
			Color.gray,
			Color.white,
			Color.black,
			Color.blue,
			Color.cyan,
			Color.green,
			Color.red,
			Color.yellow,
			Color.magenta,
			Color.gray,
			Color.white,
			Color.black
		};
		int _countChunks = 0;
		void drawChunk (TerrainChunk n, int depth=0) {
			Gizmos.color = drawColors[Mathf.Clamp(MaxLod - depth + 1, 0, drawColors.Length-1)];
			Gizmos.DrawWireCube(n.pos, new float3(n.size, n.size, n.size));
			_countChunks++;

			for (int i=0; i<8; ++i) {
				if (n.children[i] != null) {
					drawChunk(n.children[i], depth+1);
				}
			}
		}
		void OnDrawGizmosSelected () {
			_countChunks = 0;
			if (root != null)
				drawChunk(root);
		}

		void OnDrawGizmos () {
			//{ // debug test octree lookup
			//	var n = LookupOctree(testPos);
			//	if (n != null) {
			//		Gizmos.color = Color.red;
			//		Gizmos.DrawWireCube(n.pos, new float3(n.size*0.9f, n.size*0.9f, n.size*0.9f));
			//
			//		n = GetNeighbourTree(n, testDir.x, testDir.y, testDir.z);
			//		if (n != null) {
			//			Gizmos.color = Color.blue;
			//			Gizmos.DrawWireCube(n.pos, new float3(n.size*0.9f, n.size*0.9f, n.size*0.9f));
			//		}
			//	}
			//}
			
			//_countChunks = 0;
			//if (root != null)
			//	drawChunk(root);
		}

		void OnGUI () {
			float Lod0ChunkSize = VoxelSize * ChunkVoxels;
			float RootChunkSize = Lod0ChunkSize * Mathf.Pow(2f, MaxLod);
			
			GUI.Label(new Rect(0,  0, 500,30), "Terrain Chunks: "+ _countChunks);
			GUI.Label(new Rect(0, 30, 500,30), "Root Chunk Size: "+ RootChunkSize);
		}
	}
}
