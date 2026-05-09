using UnityEditor;
using UnityEngine;

[CustomEditor(typeof(ASCIIRendererFeature))]
public class ASCIIRendererFeatureEditor : Editor
{
    public override void OnInspectorGUI()
    {
        DrawDefaultInspector();

        GUILayout.Space(10);

        ASCIIRendererFeature feature = (ASCIIRendererFeature)target;

        if (GUILayout.Button("Apply Preset -> Render Feature"))
        {
            feature.ApplyPresetToSettings();
            EditorUtility.SetDirty(feature);
        }

        if (GUILayout.Button("Save Render Feature -> Preset"))
        {
            feature.SaveSettingsToPreset();
            EditorUtility.SetDirty(feature);
        }
    }
}