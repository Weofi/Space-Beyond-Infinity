using UnityEditor;
using UnityEngine;
using Voidward.Planet;

namespace Voidward.EditorTools
{
    [CustomEditor(typeof(PlanetPreview))]
    public class PlanetPreviewEditor : Editor
    {
        public override void OnInspectorGUI()
        {
            DrawDefaultInspector();
            var p = (PlanetPreview)target;
            EditorGUILayout.Space();
            if (!Application.isPlaying)
            {
                EditorGUILayout.HelpBox("Enter Play mode to generate and inspect planets.", MessageType.Info);
                return;
            }
            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button("Regenerate")) p.Regenerate();
                if (GUILayout.Button("Random Seed")) p.RandomSeed();
                if (GUILayout.Button("Next Preset")) p.SelectPreset(p.PresetIndex + 1);
            }
            if (p.Current != null && GUILayout.Button("Select Preset Asset")) Selection.activeObject = p.Current;
        }
    }
}
