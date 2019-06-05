using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public static class VectorExt {
	public static Vector3Int FloorToInt (Vector3 v) {
		return new Vector3Int( Mathf.FloorToInt(v.x), Mathf.FloorToInt(v.y), Mathf.FloorToInt(v.z) );
	}
	public static Vector3Int CeilToInt (Vector3 v) {
		return new Vector3Int( Mathf.CeilToInt(v.x), Mathf.CeilToInt(v.y), Mathf.CeilToInt(v.z) );
	}
	public static Vector3 Clamp (Vector3 v, Vector3 min, Vector3 max) {
		return new Vector3( Mathf.Clamp(v.x, min.x, max.x), Mathf.Clamp(v.y, min.y, max.y), Mathf.Clamp(v.z, min.z, max.z) );
	}
	public static Vector3 Clamp01 (Vector3 v) {
		return new Vector3( Mathf.Clamp01(v.x), Mathf.Clamp01(v.y), Mathf.Clamp01(v.z) );
	}
}
