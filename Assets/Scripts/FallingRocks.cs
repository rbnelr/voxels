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

	public ParticleSystem Particles;
	
	public void AddRock (float3 pos_world) {
		var ep = new ParticleSystem.EmitParams {
			position = pos_world,
			velocity = rand.NextFloat3Direction() * rand.NextFloat(.5f, 3f),
			rotation3D = ((Quaternion)rand.NextQuaternionRotation()).eulerAngles,
			angularVelocity3D = Quaternion.AngleAxis(rand.NextFloat(-.2f, 2f), rand.NextFloat3Direction()).eulerAngles,
		};
		Particles.Emit(ep, 1);
	}
}
