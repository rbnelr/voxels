using UnityEngine;
using Unity.Mathematics;
using static Unity.Mathematics.math;

public class Bomb : MonoBehaviour {
	public float FuseTime = 3f;

	public float Radius = 4f;
	public float ExplosionForce = 200f * 1000;

	public GameObject Explosion;

	private void Update () {
		FuseTime -= Time.deltaTime;

		if (FuseTime <= 0) {
			if (Chunks.Instance != null)
				VoxelEdit.SubstractSphere(transform.position, Radius);

			{
				var objs = Physics.OverlapSphere(transform.position, Radius*2);
				for (int i=0; i<objs.Length; ++i) {
					var rb = objs[i].GetComponent<Rigidbody>();
					if (rb != null)
						rb.AddExplosionForce(ExplosionForce, transform.position, Radius*2);
				}
			}

			Instantiate(Explosion, transform.position, Quaternion.identity);

			Destroy(this.gameObject);
		}
	}
}
