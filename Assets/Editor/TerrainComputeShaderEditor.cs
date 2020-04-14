using UnityEngine;
using UnityEditor;

namespace knockback
{
    [CustomEditor(typeof(TerrainComputeShader))]
    public class TerrainComputeShaderEditor : Editor
    {
        public override void OnInspectorGUI()
        {
            DrawDefaultInspector();

            TerrainComputeShader mm = (TerrainComputeShader)target;
            if (GUILayout.Button("Apply"))
            {
                mm.Apply();
            }
            if (GUILayout.Button("Restart Rain"))
            {
                mm.RainOnce();
            }
            if (GUILayout.Button("Rain"))
            {
                mm.RainOnce();
            }
        }
    }
}
