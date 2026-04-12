// SOGData.cs
// Data structures for the SOG (Self-Organizing Gaussians) format v2.
// Format reference: https://developer.playcanvas.com/user-manual/gaussian-splatting/formats/sog/
//
// Actual meta.json structure (discovered from real .sog files):
// {
//   "version": 2,
//   "asset": { "generator": "splat-transform v0.x.x" },
//   "count": 1199709,
//   "means":  { "mins": [...], "maxs": [...], "files": ["means_l.webp","means_u.webp"] },
//   "quats":  { "files": ["quats.webp"] },
//   "scales": { "codebook": [...256 floats...], "files": ["scales.webp"] },
//   "sh0":    { "codebook": [...256 floats...], "files": ["sh0.webp"] },
//   "shN":    { "count": 65536, "bands": 3, "codebook": [...256 floats...],
//               "files": ["shN_centroids.webp", "shN_labels.webp"] }
// }

using System;
using UnityEngine;

namespace GaussianSplatting.SOG
{
    // -------------------------------------------------------------------------
    // meta.json model
    // -------------------------------------------------------------------------

    [Serializable]
    public class SOGMeta
    {
        public int version;
        public int count;           // total number of Gaussian splats
        public SOGMetaAsset asset;  // generator info (optional)
        public SOGMeans means;
        public SOGQuats quats;
        public SOGScales scales;
        public SOGSh0 sh0;
        public SOGShN shN;          // optional higher-order SH
    }

    [Serializable]
    public class SOGMetaAsset
    {
        public string generator;
    }

    [Serializable]
    public class SOGMeans
    {
        public float[] mins;        // [x_min, y_min, z_min]
        public float[] maxs;        // [x_max, y_max, z_max]
        public string[] files;      // ["means_l.webp", "means_u.webp"]
    }

    [Serializable]
    public class SOGQuats
    {
        public string[] files;      // ["quats.webp"]
    }

    [Serializable]
    public class SOGScales
    {
        /// <summary>
        /// Palette of 256 log-scale values (same convention as PLY format). WebP pixels store indices (R=x_idx, G=y_idx, B=z_idx).
        /// </summary>
        public float[] codebook;
        public string[] files;      // ["scales.webp"]
    }

    [Serializable]
    public class SOGSh0
    {
        /// <summary>
        /// Palette of 256 float values shared across all 4 channels (R=dc0_r, G=dc0_g, B=dc0_b, A=opacity).
        /// </summary>
        public float[] codebook;
        public string[] files;      // ["sh0.webp"]
    }

    [Serializable]
    public class SOGShN
    {
        public int count;           // number of SH clusters (e.g. 65536)
        public int bands;           // SH degree (1–3)
        /// <summary>256-entry codebook used to dequantize centroid image pixels.</summary>
        public float[] codebook;
        public string[] files;      // ["shN_centroids.webp", "shN_labels.webp"]
    }

    // -------------------------------------------------------------------------
    // Decoded per-splat data
    // -------------------------------------------------------------------------

    /// <summary>
    /// Decoded Gaussian splat data, ready for conversion to GaussianSplatAsset.
    /// </summary>
    public class SOGRawData
    {
        public int count;
        public Vector3[] positions;
        public Quaternion[] rotations;
        /// <summary>Log-scale per splat (exp() applied in SOGConverter).</summary>
        public Vector3[] logScales;
        /// <summary>SH0/DC color coefficient per splat.</summary>
        public Vector3[] sh0Colors;
        /// <summary>Linear opacity per splat [0,1] (from alpha/255, NOT a logit).</summary>
        public float[] opacityLogits;
        /// <summary>Higher-order SH per splat (null if shN is absent).</summary>
        public float[][] shHigher;
        public Vector3 boundsMin;
        public Vector3 boundsMax;
    }
}
