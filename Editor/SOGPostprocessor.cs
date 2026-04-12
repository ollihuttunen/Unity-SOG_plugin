// SOGPostprocessor.cs
// Listens for newly-imported _pos.bytes files that belong to a .sog asset.
// When all four companion .bytes files are imported, creates a standalone
// GaussianSplatAsset (.asset) next to the .sog — the same way Aras's own
// GaussianSplatAssetCreator produces assets.  Users assign this .asset to
// GaussianSplatRenderer, not the .sog file itself.

using System;
using System.IO;
using GaussianSplatting.Runtime;
using GaussianSplatting.SOG;
using UnityEditor;
using UnityEngine;

namespace GaussianSplatting.SOG.Editor
{
    public class SOGPostprocessor : AssetPostprocessor
    {
        static void OnPostprocessAllAssets(
            string[] importedAssets,
            string[] deletedAssets,
            string[] movedAssets,
            string[] movedFromAssetPaths)
        {
            foreach (string path in importedAssets)
            {
                // Only react to _pos.bytes — one trigger per set of four buffers
                if (!path.EndsWith("_pos.bytes", StringComparison.OrdinalIgnoreCase))
                    continue;

                string basePath  = path.Substring(0, path.Length - "_pos.bytes".Length);
                string sogPath   = basePath + ".sog";
                string assetPath = basePath + ".asset";

                if (!File.Exists(sogPath))
                    continue;

                string pathOther = basePath + "_oth.bytes";
                string pathColor = basePath + "_col.bytes";
                string pathSH    = basePath + "_shs.bytes";

                if (!File.Exists(pathOther) || !File.Exists(pathColor) || !File.Exists(pathSH))
                    continue;

                // Load the four TextAssets (they were just imported)
                var taPos   = AssetDatabase.LoadAssetAtPath<TextAsset>(path);
                var taOther = AssetDatabase.LoadAssetAtPath<TextAsset>(pathOther);
                var taColor = AssetDatabase.LoadAssetAtPath<TextAsset>(pathColor);
                var taSH    = AssetDatabase.LoadAssetAtPath<TextAsset>(pathSH);

                if (taPos == null || taOther == null || taColor == null || taSH == null)
                {
                    Debug.LogWarning(
                        $"[SOGPostprocessor] Could not load TextAssets for '{basePath}'. " +
                        "Try reimporting the .sog file.");
                    continue;
                }

                // Re-parse the .sog to obtain bounds and splat count.
                // The WebP images are already in the OS file cache so this is fast.
                SOGRawData rawData;
                try
                {
                    rawData = SOGParser.ParseFromFile(sogPath, LibWebPDecoder.Decode);
                }
                catch (Exception ex)
                {
                    Debug.LogError(
                        $"[SOGPostprocessor] Re-parse failed for '{sogPath}': {ex.Message}");
                    continue;
                }

                // Create a fully-wired GaussianSplatAsset as a standalone .asset file.
                // This is the same approach Aras's GaussianSplatAssetCreator uses.
                var asset = ScriptableObject.CreateInstance<GaussianSplatAsset>();
                asset.Initialize(
                    rawData.count,
                    GaussianSplatAsset.VectorFormat.Float32,
                    GaussianSplatAsset.VectorFormat.Float32,
                    GaussianSplatAsset.ColorFormat.Float32x4,
                    GaussianSplatAsset.SHFormat.Float32,
                    rawData.boundsMin, rawData.boundsMax, null);
                asset.SetAssetFiles(null, taPos, taOther, taColor, taSH);
                asset.name = Path.GetFileNameWithoutExtension(sogPath);

                AssetDatabase.CreateAsset(asset, assetPath);
                AssetDatabase.SaveAssets();

                Debug.Log(
                    $"[SOGPostprocessor] Created '{assetPath}' ({rawData.count:N0} splats). " +
                    "Assign this .asset to GaussianSplatRenderer.");
            }
        }
    }
}
