using UnityEngine;
using Unity.Mathematics;
using static Unity.Mathematics.math;
using System.Collections.Generic;
using Unity.Collections;

public class FallingRocks : MonoBehaviour {
	
	public static FallingRocks Instance;
	private void Start () {
		Instance = this;
	}

	List<GameObject> Rocks = new List<GameObject>();

	Unity.Mathematics.Random rand = new Unity.Mathematics.Random(1);

	public GameObject RockPrefab;
	
	public void AddRock (float3 pos_world) {
		var go = Instantiate(RockPrefab);
		go.transform.position = pos_world;
		go.GetComponent<Rigidbody>().velocity = rand.NextFloat3Direction() * rand.NextFloat(.5f, 3);
		go.transform.rotation = rand.NextQuaternionRotation();

		Rocks.Add(go);
	}
}
