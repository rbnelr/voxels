using UnityEngine;
using Unity.Mathematics;
using static Unity.Mathematics.math;

public class PlayerDigging : MonoBehaviour {
	
	public float Reach = 4f;
	public float Radius = 1f;

	public float HitForce = 1;

	float3 Position;
	bool CanDig;

	Camera cam;
	void Start () {
		cam = GetComponentInChildren<Camera>();
	}
	void Update () {
		var ray = cam.ViewportPointToRay(float3(.5f, .5f, 0f));

		CanDig = Physics.Raycast(ray, out RaycastHit hit, Reach);
		if (Input.GetMouseButtonDown(0) && CanDig) {
			var chunk = hit.transform.GetComponent<Chunk>();
			if (chunk != null) {
				VoxelEdit.SubstractSphere(hit.point, Radius);
			}
			
			var rigidbody = hit.transform.GetComponent<Rigidbody>();
			if (rigidbody != null) {
				rigidbody.AddForceAtPosition(ray.direction * HitForce, hit.point);
			}
		}

		Position = hit.point;
	}

	void OnDrawGizmos () {
		if (CanDig) {
			Gizmos.color = Color.blue;
			Gizmos.DrawWireSphere(Position, Radius);
		}
	}
}
