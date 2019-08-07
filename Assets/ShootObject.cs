using UnityEngine;
using Unity.Mathematics;
using static Unity.Mathematics.math;

public class ShootObject : MonoBehaviour {
	
	public GameObject PrefabToSpawn;
	public bool repeat = false;
	public float rpm = 10;
	public float velocity = 30;

	public KeyCode key = KeyCode.B;

	float shotTimer = 0;

	Camera cam;
	private void Start () {
		cam = GetComponentInChildren<Camera>();
	}

	private void Update () {
		if (Input.GetKeyDown(key)) {
			Shoot();

			shotTimer = 0;
		} else if (repeat && Input.GetKey(key)) {
			shotTimer -= Time.deltaTime;

			if (shotTimer <= 0) {
				Shoot();

				shotTimer += 1f / rpm;
			}
		}
	}

	void Shoot () {
		var go = Instantiate(PrefabToSpawn, cam.transform.TransformPoint(0, 0, 2), cam.transform.rotation, null);

		float3 dir = cam.transform.TransformDirection(0, 0, 1);

		go.GetComponent<Rigidbody>().velocity = dir * velocity;
	}
}
