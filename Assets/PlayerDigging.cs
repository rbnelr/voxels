using UnityEngine;
using Unity.Mathematics;
using static Unity.Mathematics.math;

public class PlayerDigging : MonoBehaviour {
	
	public GameObject Pickaxe;
	float3 pickPos;
	quaternion pickOri;
	
	public float Reach = 4f;
	public float Radius = 1f;

	public float HitForce = 1;

	public float AnimLength = 1f;
	float anim_t = 0;
	bool digging = false;
	
	public float AxeRot = 40f;
	public float AxeDepth = 0.3f;

	public AnimationCurve AnimationCurve;
	
	Camera cam;
	void Start () {
		cam = GetComponentInChildren<Camera>();

		pickPos = Pickaxe.transform.localPosition;
		pickOri = Pickaxe.transform.localRotation;
	}
	void Update () {
		if (!digging && anim_t < 0.1f && Input.GetMouseButton(0))
			digging = true;
		
		bool digging_hit = false;
		{
			anim_t += Time.deltaTime / AnimLength * (digging ? +1f : -1f);
			anim_t = saturate(anim_t);

			float t = AnimationCurve.Evaluate(anim_t);

			float rot = lerp(0, AxeRot, t);
			float dist = lerp(0, Reach - AxeDepth, t);

			float3 pos = pickPos + float3(0,0, dist);
			quaternion ori = Quaternion.AngleAxis(rot, float3(1,0,0)) * pickOri;

			Pickaxe.transform.localPosition = pos;
			Pickaxe.transform.localRotation = ori;
		
			if (digging && anim_t >= 1f) {
				digging = false;
				digging_hit = true;
			}
		}

		//digging_hit = true;
		if (digging_hit) {
			var ray = cam.ViewportPointToRay(float3(.5f, .5f, 0f));
		
			if (Physics.Raycast(ray, out RaycastHit hit, Reach)) {
				var chunk = hit.transform.GetComponent<Chunk>();
				if (chunk != null) {
					VoxelEdit.SubstractSphere(hit.point, Radius);
				}
			
				var rigidbody = hit.transform.GetComponent<Rigidbody>();
				if (rigidbody != null) {
					rigidbody.AddForceAtPosition(ray.direction * HitForce, hit.point);
				}
			}
		}
	}
}
