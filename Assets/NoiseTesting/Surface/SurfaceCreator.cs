using System.Collections;
using System.Collections.Generic;
using UnityEngine;

[RequireComponent(typeof(MeshFilter), typeof(MeshRenderer))]
public class SurfaceCreator : MonoBehaviour {

	[Range(1, 200)]
	public int resolution = 10;
	int currentResolution;
	
	[Range(0f, 1f)]
	public float strength = 1f;
	public bool coloringForStrength;

	public Vector3 offset;
	public Vector3 rotation;

	public float frequency = 1f;
	public bool damping;
	
	[Range(1, 8)]
	public int octaves = 1;

	[Range(1f, 4f)]
	public float lacunarity = 2f;

	[Range(0f, 1f)]
	public float persistence = 0.5f;

	[Range(1,3)]
	public int dimensions = 3;
	
	public NoiseMethodType type = NoiseMethodType.Perlin;
	
	public Gradient coloring;

	Mesh mesh;
	
	void OnEnable () {
		if (mesh == null) {
			mesh = new Mesh();
			mesh.name = "Surface Mesh";
			GetComponent<MeshFilter>().mesh = mesh;
		}
		Refresh();
	}

	Vector3[] vertices;
	Vector3[] normals;
	Color[] colors;
	
	public bool showNormals;

	public bool analyticalDerivatives;
	
	void OnDrawGizmosSelected () {
		if (showNormals && vertices != null && normals != null) {
			float scale = 1f / resolution;
			Gizmos.color = Color.yellow;
			for (int v=0; v<vertices.Length; v++) {
				var pos = transform.TransformPoint(vertices[v]);
				var dir = transform.TransformVector(normals[v] * scale);
				Gizmos.DrawRay(pos, dir);
			}
		}
	}

	public void Refresh () {
		if (resolution != currentResolution) {
			CreateGrid();
		}

		Quaternion q = Quaternion.Euler(rotation);
		Quaternion qInv = Quaternion.Inverse(q);

		Vector3 point00 = q * new Vector3(-0.5f,-0.5f) + offset;
		Vector3 point10 = q * new Vector3( 0.5f,-0.5f) + offset;
		Vector3 point01 = q * new Vector3(-0.5f, 0.5f) + offset;
		Vector3 point11 = q * new Vector3( 0.5f, 0.5f) + offset;

		NoiseMethod method = Noise.methods[(int)type][dimensions -1];

		float stepSize = 1f / resolution;
		
		float amplitude = damping ? strength / frequency : strength;
		
		int v = 0;
		for (int y=0; y<(resolution +1); y++) {
			Vector3 point0 = Vector3.Lerp(point00, point01, y * stepSize);
			Vector3 point1 = Vector3.Lerp(point10, point11, y * stepSize);
			for (int x=0; x<(resolution +1); x++) {
				Vector3 point = Vector3.Lerp(point0, point1, x * stepSize);
				
				NoiseSample sample = Noise.Sum(method, point, frequency, octaves, lacunarity, persistence);
				sample = sample * 0.5f;
				sample.derivative = qInv * sample.derivative;
				
				vertices[v].y = sample.value * amplitude;
				if (coloringForStrength)
					sample *= amplitude;
				colors[v] = coloring.Evaluate(sample.value + 0.5f);
				if (analyticalDerivatives) {
					normals[v] = new Vector3(-sample.derivative.x, 1f, -sample.derivative.y).normalized;
				}
				v++;
			}
		}
		mesh.vertices = vertices;
		mesh.colors = colors;
		if (!analyticalDerivatives) {
			CalculateNormals();
		}
		mesh.normals = normals;
	}

	void CalculateNormals () {
		int v = 0;
		for (int z=0; z<(resolution +1); z++) {
			for (int x=0; x<(resolution +1); x++) {
				normals[v++] = new Vector3(-GetXDerivative(x, z), 1f, -GetZDerivative(x, z)).normalized;
			}
		}
	}

	float GetXDerivative (int x, int z) {
		int xl = Mathf.Clamp(x -1, 0, resolution);
		int xr = Mathf.Clamp(x +1, 0, resolution);
		float l	= vertices[z * (resolution+1) + xl].y;
		float r	= vertices[z * (resolution+1) + xr].y;
		return (r - l) * resolution / (xr - xl);
	}
	float GetZDerivative (int x, int z) {
		int zb = Mathf.Clamp(z -1, 0, resolution);
		int zf = Mathf.Clamp(z +1, 0, resolution);
		float b	= vertices[zb * (resolution+1) + x].y;
		float f	= vertices[zf * (resolution+1) + x].y;
		return (f - b) * resolution / (zf - zb);
	}

	void CreateGrid () {
		currentResolution = resolution;
		mesh.Clear();

		vertices = new Vector3[(resolution + 1) * (resolution + 1)];
		normals = new Vector3[vertices.Length];
		colors = new Color[vertices.Length];
		Vector2[] uv = new Vector2[vertices.Length];
		float stepSize = 1f / resolution;

		int v = 0;
		for (int z=0; z<(resolution +1); ++z) {
			for (int x=0; x<(resolution +1); ++x) {
				vertices[v] = new Vector3(x * stepSize - 0.5f, 0f, z * stepSize - 0.5f);
				normals[v] = new Vector3(0,1,0);
				uv[v] = new Vector2(x * stepSize, z * stepSize);
				v++;
			}
		}
		mesh.vertices = vertices;
		mesh.normals = normals;
		mesh.uv = uv;
	
		int[] triangles = new int[resolution * resolution * 6];
		int t = 0;
		for (int y=0; y<resolution; ++y) {
			for (int x=0; x<resolution; ++x) {
				triangles[t++] = (y+0) * (resolution+1) + (x+0);
				triangles[t++] = (y+1) * (resolution+1) + (x+0);
				triangles[t++] = (y+0) * (resolution+1) + (x+1);
				triangles[t++] = (y+0) * (resolution+1) + (x+1);
				triangles[t++] = (y+1) * (resolution+1) + (x+0);
				triangles[t++] = (y+1) * (resolution+1) + (x+1);
			}
		}
		mesh.triangles = triangles;
	}
}
