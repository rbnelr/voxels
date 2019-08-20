using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class DeleteAfterTime : MonoBehaviour {
	public float LifetimeRemain = 5f;

	private void Update () {
		LifetimeRemain -= Time.deltaTime;

		if (LifetimeRemain <= 0)
			Destroy(this.gameObject);
	}
}
