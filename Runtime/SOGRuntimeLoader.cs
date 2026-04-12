// SOGRuntimeLoader.cs
// MonoBehaviour that loads a pre-imported GaussianSplatAsset from Resources
// or assigns a .sog file at runtime (editor-mode warm path only).
//
// RECOMMENDED WORKFLOW:
//   1. Import .sog files in the Unity Editor via drag-and-drop (SOGImporter handles this).
//   2. Add the resulting GaussianSplatAsset to a GameObject's GaussianSplatRenderer.asset field.
//   3. For dynamic/runtime scenarios, use this component to swap assets at runtime.
//
// NOTE ON RUNTIME .SOG LOADING:
//   GaussianSplatRenderer expects a pre-built GaussianSplatAsset with TextAsset sub-assets.
//   True runtime loading of .sog files (without prior editor import) requires creating those
//   TextAsset references in memory, which Unity restricts. Full runtime loading support
//   will be added in a future version. For now, use pre-imported assets.

using System;
using System.Collections;
using GaussianSplatting.Runtime;
using UnityEngine;
using UnityEngine.Networking;

namespace GaussianSplatting.SOG
{
    /// <summary>
    /// Assigns a <see cref="GaussianSplatAsset"/> to the <see cref="GaussianSplatRenderer"/>
    /// on the same GameObject. Supports Resources paths and optional late-binding.
    /// </summary>
    [RequireComponent(typeof(GaussianSplatRenderer))]
    [AddComponentMenu("Gaussian Splatting/SOG Runtime Loader")]
    public class SOGRuntimeLoader : MonoBehaviour
    {
        public enum LoadSource
        {
            /// <summary>Load a GaussianSplatAsset from a Resources folder by path.</summary>
            Resources,

            /// <summary>Assign a GaussianSplatAsset reference directly in the Inspector.</summary>
            DirectReference,
        }

        [Tooltip("How to obtain the GaussianSplatAsset at runtime.")]
        public LoadSource loadSource = LoadSource.DirectReference;

        [Tooltip("(DirectReference) The pre-imported GaussianSplatAsset to use.")]
        public GaussianSplatAsset assetReference;

        [Tooltip("(Resources) Path inside a Resources/ folder, e.g. 'Splats/MyScene' (no extension).")]
        public string resourcesPath;

        [Tooltip("If true, loads and assigns the asset when the component awakens. Otherwise call LoadAsset() manually.")]
        public bool loadOnAwake = true;

        GaussianSplatRenderer m_Renderer;

        void Awake()
        {
            m_Renderer = GetComponent<GaussianSplatRenderer>();
            if (loadOnAwake)
                LoadAsset();
        }

        /// <summary>Load and assign the configured GaussianSplatAsset.</summary>
        public void LoadAsset()
        {
            switch (loadSource)
            {
                case LoadSource.DirectReference:
                    Assign(assetReference);
                    break;

                case LoadSource.Resources:
                    if (string.IsNullOrEmpty(resourcesPath))
                    {
                        Debug.LogError("[SOGRuntimeLoader] Resources path is empty.", this);
                        return;
                    }
                    var loaded = Resources.Load<GaussianSplatAsset>(resourcesPath);
                    if (loaded == null)
                    {
                        Debug.LogError($"[SOGRuntimeLoader] Could not load GaussianSplatAsset at Resources path '{resourcesPath}'.", this);
                        return;
                    }
                    Assign(loaded);
                    break;
            }
        }

        void Assign(GaussianSplatAsset asset)
        {
            if (asset == null)
            {
                Debug.LogWarning("[SOGRuntimeLoader] Asset is null — nothing assigned to renderer.", this);
                return;
            }
            m_Renderer.m_Asset = asset;
            Debug.Log($"[SOGRuntimeLoader] Assigned '{asset.name}' ({asset.splatCount:N0} splats) to {gameObject.name}.", this);
        }
    }
}
