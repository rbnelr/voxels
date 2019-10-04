using UnityEngine;
using Unity.Mathematics;
using static Unity.Mathematics.math;
using System.Linq;

public class TerrainAtlasGenerator : MonoBehaviour {
	
	[System.Serializable]
	public class TerrainMaterial {
		public Texture2D	Texture;
		public Color		Tint;
		public float		Scale;
	}

	public TerrainMaterial[] Materials;
	
	public Material Mat;

	public Texture2D Atlas;
	public int AtlasSize = 1024;

	Rect[] rects;

	private void Start () {
		Texture2D[] texs = Materials.Select(x => x.Texture).ToArray();

		Atlas = new Texture2D(AtlasSize, AtlasSize);
		Atlas.wrapMode = TextureWrapMode.Clamp;
		//Atlas.filterMode = FilterMode.Point;

		// --TODO: Remove potential duplicates? Since we might use the same texture with different tint or scale as a different material
		// --Or does PackTextures do that already (just give us the same rects for identlical textures?)
		// -> PackTextures DOES remove duplicates, but this is not documented
		rects = Atlas.PackTextures(texs, 8, AtlasSize, true);
		
	}
	private void Update () {
		Mat.SetTexture("_Atlas", Atlas);
		Mat.SetVectorArray("_AtlasUVRects", rects.Select(x => new Vector4(x.width, x.height, x.x, x.y)).ToArray());
		Mat.SetVectorArray("_MaterialScales", Materials.Select(x =>
				new Vector4(x.Scale, x.Scale * (float)x.Texture.height / (float)x.Texture.width, 0, 0)
			).ToArray());
		Mat.SetColorArray("_MaterialTints", Materials.Select(x => x.Tint).ToArray());
	}
}
