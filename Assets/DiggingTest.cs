using UnityEngine;
using Unity.Mathematics;
using static Unity.Mathematics.math;
using static VoxelUtil;

public class DiggingTest : MonoBehaviour {
	
	public Chunks Chunks;
	public float Radius;

	void Update () {
		if (Input.GetMouseButtonDown(0)) {
			VoxelEdit.SubstractSphere(Chunks, transform.position, Radius);
		}
	}

	void OnDrawGizmos () {
		Gizmos.color = Color.red;
		Gizmos.DrawWireSphere(transform.position, Radius);
	}
}
