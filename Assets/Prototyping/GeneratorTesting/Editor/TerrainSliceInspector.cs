using UnityEditor;
using UnityEngine;

namespace GeneratorTesting {
	[CustomEditor(typeof(TerrainSlice))]
	public class TerrainSliceInspector : Editor {
	
		private TerrainSlice terrainSlice;

		private void OnEnable () {
			terrainSlice = target as TerrainSlice;
			Undo.undoRedoPerformed += RefreshCreator;
		}

		private void OnDisable () {
			Undo.undoRedoPerformed -= RefreshCreator;
		}

		private void RefreshCreator () {
			if (Application.isPlaying) {
				terrainSlice.FullUpdate();
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