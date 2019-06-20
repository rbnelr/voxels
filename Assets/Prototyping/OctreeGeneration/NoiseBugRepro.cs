using UnityEngine;
using Unity.Mathematics;
using static Unity.Mathematics.math;

public class NoiseBugRepro : MonoBehaviour
{
    // Start is called before the first frame update
    void Start()
    {
		string output = "";
        for (int i=0; i<20; ++i) {
			float x = lerp(1.8f, 2.2f, i / 20f);
			float3 pos = float3(x, 2f, 2f);

			float val = noise.snoise(pos / 20f);

			output += string.Format("noise.snoise({0:F2}, {1:F2}, {2:F2}) -> {3:F6}\n", pos.x, pos.y, pos.z, val);
		}
		
		Debug.Log(output);
    }

    // Update is called once per frame
    void Update()
    {
        
    }
}
