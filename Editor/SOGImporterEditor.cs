// SOGImporterEditor.cs
// Custom Inspector for SOGImporter — shown when a .sog file is selected in the Project window.

using UnityEditor;
using UnityEditor.AssetImporters;
using UnityEngine;

namespace GaussianSplatting.SOG.Editor
{
    [CustomEditor(typeof(SOGImporter))]
    public class SOGImporterEditor : ScriptedImporterEditor
    {
        public override void OnInspectorGUI()
        {
            var importer = (SOGImporter)target;

            EditorGUILayout.LabelField("SOG Gaussian Splatting Importer", EditorStyles.boldLabel);
            EditorGUILayout.Space(4);

            // Info box
            EditorGUILayout.HelpBox(
                "Drag a .sog file into the Project window to import it as a GaussianSplatAsset.\n\n" +
                "After import:\n" +
                "  1. Add a GaussianSplatRenderer component to a GameObject.\n" +
                "  2. Assign this asset to the 'Asset' field.\n" +
                "  3. Make sure your scene uses the Universal Render Pipeline (URP).",
                MessageType.Info);

            EditorGUILayout.Space(4);
            ApplyRevertGUI();
        }
    }
}
