using UnityEngine;

namespace GeneratorTesting {
	public struct Voxel {
		public float density;
		public Color color;
	}
	
	public class TerrainGenerator : MonoBehaviour {
		
		public Vector3 offset;
		public Vector3 rotation;
		
		public float abyssSize = 1;
		[Range(-1,+1)]
		public float abyssBias = -0.6f;
		public Vector3 abyssOffset;
		public float abyssTurbZFreq;
		public float abyssTurbZScale;

		public float caveSize = 1;
		[Range(-1,+1)]
		public float caveBias = -0.6f;

		public Gradient coloring;
		
		public bool hasChanged = false;
		void LateUpdate () {
			hasChanged = false;
		}

		void OnEnable () {
			UpdateNoise();
		}

		public void UpdateNoise () {
			hasChanged = true;
		}
		
		public Voxel Generate (Vector3 pos_world) {
			//if (caveDensity == null)
			//	UpdateNoise();

			var pos2d = new Vector3(pos_world.x, pos_world.z, 0f);
			
			pos_world = Quaternion.Euler(rotation) * pos_world + offset;

			var pos = pos_world;
			
			var abyssPos = (pos2d / abyssSize) + abyssOffset;
			abyssPos.x += abyssTurbZScale * Noise.Perlin1D(new Vector3(pos_world.y, 0,0), abyssTurbZFreq).value;
			abyssPos.y += abyssTurbZScale * Noise.Perlin1D(new Vector3(1234f + pos_world.y, 0,0), abyssTurbZFreq).value;
			var cave = Noise.SimplexValue2D(abyssPos, 1f);

			cave += abyssBias;

			var dir = Vector3.Dot(cave.derivative, Vector3.up);
			return new Voxel {
				density = -cave.value, 
				color = coloring.Evaluate(dir * 0.5f + 0.5f)
			};
		}
	}
}
