using UnityEngine;

public static class VectorExt {
	public static Vector3 Round (Vector3 v) {
		return new Vector3( Mathf.Round(v.x), Mathf.Round(v.y), Mathf.Round(v.z) );
	}
	public static Vector3 Floor (Vector3 v) {
		return new Vector3( Mathf.Floor(v.x), Mathf.Floor(v.y), Mathf.Floor(v.z) );
	}
	public static Vector3 Ceil (Vector3 v) {
		return new Vector3( Mathf.Ceil(v.x), Mathf.Ceil(v.y), Mathf.Ceil(v.z) );
	}

	public static Vector3Int FloorToInt (Vector3 v) {
		return new Vector3Int( Mathf.FloorToInt(v.x), Mathf.FloorToInt(v.y), Mathf.FloorToInt(v.z) );
	}
	public static Vector3Int CeilToInt (Vector3 v) {
		return new Vector3Int( Mathf.CeilToInt(v.x), Mathf.CeilToInt(v.y), Mathf.CeilToInt(v.z) );
	}
	
	public static Vector3 Min (Vector3 a, Vector3 b) {
		return new Vector3( Mathf.Min(a.x,b.x), Mathf.Min(a.y,b.y), Mathf.Min(a.z,b.z) );
	}
	public static Vector3 Max (Vector3 a, Vector3 b) {
		return new Vector3( Mathf.Max(a.x,b.x), Mathf.Max(a.y,b.y), Mathf.Max(a.z,b.z));
	}
	public static Vector3Int Min (Vector3Int a, Vector3Int b) {
		return new Vector3Int( Mathf.Min(a.x,b.x), Mathf.Min(a.y,b.y), Mathf.Min(a.z,b.z) );
	}
	public static Vector3Int Max (Vector3Int a, Vector3Int b) {
		return new Vector3Int( Mathf.Max(a.x,b.x), Mathf.Max(a.y,b.y), Mathf.Max(a.z,b.z));
	}

	public static Vector3 Clamp (Vector3 v, Vector3 min, Vector3 max) {
		return new Vector3( Mathf.Clamp(v.x, min.x, max.x), Mathf.Clamp(v.y, min.y, max.y), Mathf.Clamp(v.z, min.z, max.z) );
	}
	public static Vector3 Clamp01 (Vector3 v) {
		return new Vector3( Mathf.Clamp01(v.x), Mathf.Clamp01(v.y), Mathf.Clamp01(v.z) );
	}
	public static Vector3Int Clamp (Vector3Int v, Vector3Int min, Vector3Int max) {
		return new Vector3Int( Mathf.Clamp(v.x, min.x, max.x), Mathf.Clamp(v.y, min.y, max.y), Mathf.Clamp(v.z, min.z, max.z) );
	}
}

public static class MyMath {
	public static float map (float x, float in_a, float in_b) {
		return (x - in_a) / (in_b - in_a);
	}
	public static float map (float x, float in_a, float in_b, float out_a, float out_b) {
		return (x - in_a) / (in_b - in_a) * (out_b - out_a) + out_a;
	}
	public static float mapClamp (float x, float in_a, float in_b) {
		return Mathf.Clamp01((x - in_a) / (in_b - in_a));
	}
	public static float mapClamp (float x, float in_a, float in_b, float out_a, float out_b) {
		return Mathf.Clamp01((x - in_a) / (in_b - in_a)) * (out_b - out_a) + out_a;
	}
}
