using UnityEngine;

public class TerrainOctreeTest : MonoBehaviour {

	public GameObject player;
	Vector3 playerPos { get { return player.transform.position; } }

	[Range(0, 15)]
	public int MaxLod = 12;
	public int LeafSize = 1;
	
	class Node {
		public Vector3 pos;
		public float size;

		public Node[] children = new Node[8];

		public static Vector3Int[] childrenPos = new Vector3Int[] {
			new Vector3Int(-1,-1,-1),
			new Vector3Int( 1,-1,-1),
			new Vector3Int(-1, 1,-1),
			new Vector3Int( 1, 1,-1),
			new Vector3Int(-1,-1, 1),
			new Vector3Int( 1,-1, 1),
			new Vector3Int(-1, 1, 1),
			new Vector3Int( 1, 1, 1),
		};
	}

	Node root;

	int countNodes;

	Color[] drawColors = new Color[] {
		Color.blue,
		Color.cyan,
		Color.green,
		Color.red,
		Color.yellow,
		Color.magenta,
		Color.gray,
		Color.white,
		Color.black,
		Color.blue,
		Color.cyan,
		Color.green,
		Color.red,
		Color.yellow,
		Color.magenta,
		Color.gray,
		Color.white,
		Color.black
	};
	void drawNode (Node n, int depth=0) {
		float size = n.size;
		Gizmos.color = drawColors[MaxLod - depth + 1];
		Gizmos.DrawWireCube(n.pos, new Vector3(size, size, size));
		
		countNodes++;

		for (int i=0; i<8; ++i) {
			if (n.children[i] != null) {
				drawNode(n.children[i], depth+1);
			}
		}
	}
	void OnDrawGizmos () {
		countNodes = 0;
		if (root != null)
			drawNode(root);

		Debug.Log("Total Nodes: "+ countNodes);
	}
	
	int calcLod (float dist) {
		float m = 0.5f;
		float n = 16f;
		float l = 6f;

		float a = -l / ( Mathf.Log(m) - Mathf.Log(n) );
		float b = (l * Mathf.Log(m)) / ( Mathf.Log(m) - Mathf.Log(n) );
		
		var lod = Mathf.FloorToInt( a * Mathf.Log(dist / LeafSize) + b );
		return lod;
	}

	private void updateNode (Node n, int depth=0) {
		if (depth > MaxLod) return;

		for (int i=0; i<8; ++i) {
			var size = n.size / 2;
			var pos = n.pos + (Vector3)Node.childrenPos[i] * size / 2;
			
			var closest = pos + VectorExt.Clamp(playerPos - pos, new Vector3(-size/2,-size/2,-size/2), new Vector3(size/2,size/2,size/2));
			var dist = (playerPos - closest).magnitude;

			var lod = Mathf.FloorToInt(calcLod(dist));
			var desiredLod = lod;
			bool needChild = desiredLod < (MaxLod - depth);

			if (needChild) {
				if (n.children[i] == null) {
					n.children[i] = new Node();
				}
				n.children[i].pos = pos;
				n.children[i].size = size;
				updateNode(n.children[i], depth+1);
			} else {
				n.children[i] = null;
			}
		}
	}

	void Update () {
		if (root == null) {
			root = new Node();
		}

		int rootSize = LeafSize << MaxLod;
		root.size = (float)rootSize;

		float rootHalfSize = root.size / 2;

		var pos = VectorExt.Round(playerPos * (1f / rootHalfSize)) * rootHalfSize;
		root.pos = pos;

		updateNode(root);
	}
}
