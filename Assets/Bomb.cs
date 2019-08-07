using UnityEngine;
using Unity.Mathematics;
using static Unity.Mathematics.math;

public class Bomb : MonoBehaviour {
	public float FuseTime = 3f;

	public float Radius = 4f;

	private void Update () {
		FuseTime -= Time.deltaTime;

		if (FuseTime <= 0) {
			VoxelEdit.SubstractSphere(transform.position, Radius);

			Destroy(this.gameObject);
		}
	}
}
