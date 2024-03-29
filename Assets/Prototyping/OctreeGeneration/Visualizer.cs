﻿using UnityEngine;

namespace OctreeGeneration {
	public enum VisualizeType {
		Density,
		Color,
	}
	
	[RequireComponent(typeof(MeshFilter), typeof(MeshRenderer))]
	public class Visualizer : MonoBehaviour {
		
		[Range(2, 512)]
		public int resolution = 256;
		
		[MinMaxRange(-20,+20)]
		public MinMaxRange coloringRange;
		public Gradient coloring;
		
		public VisualizeType type = VisualizeType.Density;
		
		public TerrainGeneratorStruct generator;

		Texture2D texture;
		
		void OnEnable () {
			if (texture == null) {
				texture = new Texture2D(resolution, resolution, TextureFormat.RGB24, true);
				texture.name = "Visualizer Texture";
				texture.wrapMode = TextureWrapMode.Clamp;
				texture.filterMode = FilterMode.Trilinear;
				texture.anisoLevel = 16;
				GetComponent<MeshRenderer>().material.mainTexture = texture;
			}
			FillTexture();
		}
		
		private void Update () {
			if (transform.hasChanged) {
				transform.hasChanged = false;
				FillTexture();
			}
		}

		public void FillTexture () {
			if (texture.width != resolution) {
				texture.Resize(resolution, resolution);
			}

			Vector3 point00 = transform.TransformPoint(new Vector3(-0.5f, -0.5f));
			Vector3 point10 = transform.TransformPoint(new Vector3( 0.5f, -0.5f));
			Vector3 point01 = transform.TransformPoint(new Vector3(-0.5f,  0.5f));
			Vector3 point11 = transform.TransformPoint(new Vector3( 0.5f,  0.5f));

			float stepSize = 1f / resolution;
		
			for (int y = 0; y < resolution; y++) {
				Vector3 point0 = Vector3.Lerp(point00, point01, (y + 0.5f) * stepSize);
				Vector3 point1 = Vector3.Lerp(point10, point11, (y + 0.5f) * stepSize);
				for (int x = 0; x < resolution; x++) {
					Vector3 point = Vector3.Lerp(point0, point1, (x + 0.5f) * stepSize);
					
					var sample = generator.Generate(point);
					Color col = Color.magenta;
					switch (type) {
						case VisualizeType.Density:
							col = coloring.Evaluate(MyMath.mapClamp(sample.distance, coloringRange.rangeStart, coloringRange.rangeEnd));
							break;
						case VisualizeType.Color:
							col = Color.white;
							break;
					}

					texture.SetPixel(x, y, col);
				}
			}
			texture.Apply();
		}
	}
}
