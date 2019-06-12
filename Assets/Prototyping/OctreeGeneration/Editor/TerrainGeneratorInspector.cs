using UnityEditor;
using UnityEngine;

namespace OctreeGeneration {
	[CustomEditor(typeof(TerrainGenerator))]
	public class TerrainGeneratorInspector : Editor {
	
		private TerrainGenerator terrainGenerator;

		private void OnEnable () {
			terrainGenerator = target as TerrainGenerator;
			Undo.undoRedoPerformed += RefreshCreator;
		}

		private void OnDisable () {
			Undo.undoRedoPerformed -= RefreshCreator;
		}

		private void RefreshCreator () {
			if (Application.isPlaying) {
				terrainGenerator.UpdateNoise();
			}
		}

		public override void OnInspectorGUI () {
			EditorGUI.BeginChangeCheck();
			DrawDefaultInspector();
			if (EditorGUI.EndChangeCheck()) {
				RefreshCreator();
			}
		}
	}
}