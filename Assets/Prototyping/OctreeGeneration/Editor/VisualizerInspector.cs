using UnityEditor;
using UnityEngine;

namespace OctreeGeneration {
	[CustomEditor(typeof(Visualizer))]
	public class VisualizerInspector : Editor {
	
		private Visualizer visualizer;

		private void OnEnable () {
			visualizer = target as Visualizer;
			Undo.undoRedoPerformed += RefreshCreator;
		}

		private void OnDisable () {
			Undo.undoRedoPerformed -= RefreshCreator;
		}

		private void RefreshCreator () {
			if (Application.isPlaying) {
				visualizer.FillTexture();
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