using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using static Unity.Mathematics.math;
using Unity.Mathematics;

namespace Prototyping {
	public class Player : MonoBehaviour {

		public float BaseSpeed = 1f;
		public float FastSpeedMultiplier = 4f;

		public float MouselookSensitiviy = 1f / 100f; // screen radii per mouse input units
		
		public float2 test = float2(0,0);
		public float2 MouselookAng = float2(0,0);

		public float testFallSpeed = -50f;

		Camera cam;
	
		void Awake () {
			cam = GetComponentInChildren<Camera>();
		}

		void Start () {

		}
	
		void Update () {
			//Debug.Log("Mouse: "+ Input.GetAxisRaw("Mouse X") +" "+ Input.GetAxisRaw("Mouse Y"));
			//Debug.Log(Input.GetAxisRaw("Horizontal") +" "+ Input.GetAxisRaw("Forward") +" "+ Input.GetButton("Jump") +" "+ Input.GetButton("Crouch") +" "+ Input.GetButton("Sprint"));

			if (Input.GetKeyDown(KeyCode.Mouse1)) {
				Cursor.visible = false;
				Cursor.lockState = CursorLockMode.Locked;
			}
			if (Input.GetKeyUp(KeyCode.Mouse1)) {
				Cursor.visible = true;
				Cursor.lockState = CursorLockMode.None;
			}
			if (Input.GetKey(KeyCode.Mouse1)) {
				float mouseMult = MouselookSensitiviy * cam.fieldOfView / 2;
				MouselookAng += mouseMult * float2(Input.GetAxisRaw("Mouse X"), -Input.GetAxisRaw("Mouse Y"));
				MouselookAng.x = fmod(MouselookAng.x, 360f);
				MouselookAng.y = clamp(MouselookAng.y, -90, +90);

				transform.eulerAngles = float3(MouselookAng.y, MouselookAng.x, 0);
			}

			float3 moveVec = 0;
			moveVec.x = Input.GetAxis("Horizontal");
			moveVec.z = Input.GetAxis("Forward");
			moveVec.y = Input.GetAxis("Jump") - Input.GetAxis("Crouch");

			moveVec = normalizesafe(moveVec) * BaseSpeed * (Input.GetButton("Sprint") ? FastSpeedMultiplier : 1);

			moveVec = transform.TransformVector(moveVec);

			transform.localPosition += (Vector3)moveVec;

			//transform.localPosition += (Vector3)float3(0, testFallSpeed, 0) * Time.deltaTime;
		}
	}
}
