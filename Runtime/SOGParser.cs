// SOGParser.cs
// Parses a .sog ZIP archive into SOGRawData.
//
// WebP loading strategy:
//   Unity's ImageConversion.LoadImage does NOT support lossless WebP (VP8L).
//   The editor path (SOGImporter) provides a custom webpLoader that extracts
//   files to disk and uses Unity's texture import pipeline instead.

using System;
using System.IO;
using System.IO.Compression;
using System.Text;
using Newtonsoft.Json;
using UnityEngine;

namespace GaussianSplatting.SOG
{
    public static class SOGParser
    {
        static readonly float kSqrt2Over2 = Mathf.Sqrt(2f) / 2f;

        // -------------------------------------------------------------------------
        // Public API
        // webpLoader signature: (filename, bytes) => Color32[] pixels
        // Pass null to use Unity's built-in LoadImage (works for PNG/JPG, not VP8L WebP).
        // -------------------------------------------------------------------------

        public static SOGRawData ParseFromFile(string filePath,
            Func<string, byte[], Color32[]> webpLoader = null)
        {
            return ParseFromBytes(File.ReadAllBytes(filePath), webpLoader);
        }

        public static SOGRawData ParseFromBytes(byte[] sogBytes,
            Func<string, byte[], Color32[]> webpLoader = null)
        {
            var loader = webpLoader ?? LoadImageFallback;
            using var zipStream = new MemoryStream(sogBytes);
            using var archive = new ZipArchive(zipStream, ZipArchiveMode.Read);
            return ParseArchive(archive, loader);
        }

        // -------------------------------------------------------------------------
        // Archive parsing
        // -------------------------------------------------------------------------

        static SOGRawData ParseArchive(ZipArchive archive,
            Func<string, byte[], Color32[]> webpLoader)
        {
            SOGMeta meta = ReadMeta(archive);
            int n = meta.count;
            if (n <= 0)
                throw new InvalidDataException($"SOG: count is {n}.");

            // Pre-cache all needed WebP byte arrays from ZIP
            Color32[] LoadImg(string filename) =>
                webpLoader(filename, ReadEntryBytes(archive, filename));

            var result = new SOGRawData { count = n };
            result.positions    = DecodePositions(meta, n, LoadImg);
            result.rotations    = DecodeRotations(meta, n, LoadImg);
            result.logScales    = DecodeScales(meta, n, LoadImg);
            DecodeSH0(meta, n, LoadImg, out result.sh0Colors, out result.opacityLogits);

            if (meta.shN?.files?.Length >= 2)
                result.shHigher = DecodeShN(meta, n, LoadImg);

            ComputeBounds(result);
            return result;
        }

        // -------------------------------------------------------------------------
        // meta.json
        // -------------------------------------------------------------------------

        static SOGMeta ReadMeta(ZipArchive archive)
        {
            var entry = FindEntry(archive, "meta.json")
                ?? throw new InvalidDataException("SOG: meta.json not found in archive.");
            string json;
            using (var reader = new StreamReader(entry.Open(), Encoding.UTF8))
                json = reader.ReadToEnd();
            return JsonConvert.DeserializeObject<SOGMeta>(json)
                ?? throw new InvalidDataException("SOG: Failed to deserialize meta.json.");
        }

        // -------------------------------------------------------------------------
        // Positions — 16-bit quantized, lower/upper byte split across two images
        // -------------------------------------------------------------------------

        // SOG stores positions with a log transform applied before quantization.
        // mins/maxs in meta.json are in log-space: logVal = sign(x)*log(|x|+1)
        // Inverse: x = sign(logVal) * (exp(|logVal|) - 1)
        static float InvLogTransform(float v) =>
            Mathf.Sign(v) * (Mathf.Exp(Mathf.Abs(v)) - 1f);

        static Vector3[] DecodePositions(SOGMeta meta, int n,
            Func<string, Color32[]> load)
        {
            if (meta.means == null) throw new InvalidDataException("SOG: 'means' missing.");
            Color32[] pixL = load(meta.means.files[0]); // means_l
            Color32[] pixU = load(meta.means.files[1]); // means_u

            float minX = meta.means.mins[0], minY = meta.means.mins[1], minZ = meta.means.mins[2];
            float rangeX = meta.means.maxs[0] - minX;
            float rangeY = meta.means.maxs[1] - minY;
            float rangeZ = meta.means.maxs[2] - minZ;

            var positions = new Vector3[n];
            for (int i = 0; i < n; i++)
            {
                ushort vx = (ushort)((pixU[i].r << 8) | pixL[i].r);
                ushort vy = (ushort)((pixU[i].g << 8) | pixL[i].g);
                ushort vz = (ushort)((pixU[i].b << 8) | pixL[i].b);
                // Lerp in log-space, then invert the log transform to get actual positions
                float lx = minX + (vx / 65535f) * rangeX;
                float ly = minY + (vy / 65535f) * rangeY;
                float lz = minZ + (vz / 65535f) * rangeZ;
                positions[i] = new Vector3(
                    InvLogTransform(lx),
                    -InvLogTransform(ly),  // negate Y: SOG Y-down → Unity Y-up
                    InvLogTransform(lz));
            }
            return positions;
        }

        // -------------------------------------------------------------------------
        // Rotations — smallest-three quaternion encoding
        //   R,G,B = 3 smallest components mapped [0,255] → [-√2/2, +√2/2]
        //   A (252-255): which component is largest/omitted (mode = A-252)
        //     0 → x omitted, RGB=(y,z,w)
        //     1 → y omitted, RGB=(x,z,w)
        //     2 → z omitted, RGB=(x,y,w)
        //     3 → w omitted, RGB=(x,y,z)
        // -------------------------------------------------------------------------

        // SOG quats.webp: smallest-three quaternion encoding.
        // PlayCanvas internal quaternion order: [qw, qx, qy, qz] (index 0 = qw).
        // Alpha = 252 + dropped_index (0=qw,1=qx,2=qy,3=qz).
        // RGB stores remaining 3 components in original index order, mapped [-√2/2,+√2/2]→[0,255].
        // Source: github.com/playcanvas/splat-transform write-sog.ts / gsplat-sog-data.js
        static Quaternion[] DecodeRotations(SOGMeta meta, int n,
            Func<string, Color32[]> load)
        {
            if (meta.quats == null) throw new InvalidDataException("SOG: 'quats' missing.");
            Color32[] pix = load(meta.quats.files[0]);
            var rots = new Quaternion[n];
            for (int i = 0; i < n; i++)
            {
                // Decode three stored components: (byte/255 - 0.5) * sqrt(2) → [-√2/2, +√2/2]
                float a = (pix[i].r / 255f) * (2f * kSqrt2Over2) - kSqrt2Over2; // R
                float b = (pix[i].g / 255f) * (2f * kSqrt2Over2) - kSqrt2Over2; // G
                float c = (pix[i].b / 255f) * (2f * kSqrt2Over2) - kSqrt2Over2; // B
                int mode = pix[i].a - 252; // 0=qw dropped, 1=qx dropped, 2=qy dropped, 3=qz dropped
                float d = Mathf.Sqrt(Mathf.Max(0f, 1f - a*a - b*b - c*c)); // reconstructed

                // Reconstruct (qx, qy, qz, qw) from PlayCanvas [qw,qx,qy,qz] component order.
                // mode 0 (qw largest, dropped): a=qx, b=qy, c=qz, d=qw (reconstructed)
                // mode 1 (qx largest, dropped): a=qw, b=qy, c=qz, d=qx (reconstructed)
                // mode 2 (qy largest, dropped): a=qw, b=qx, c=qz, d=qy (reconstructed)
                // mode 3 (qz largest, dropped): a=qw, b=qx, c=qy, d=qz (reconstructed)
                float x, y, z, w;
                switch (mode)
                {
                    case 0: x=a;  y=b;  z=c;  w=d;  break; // qw dropped: a=qx,b=qy,c=qz,d=qw
                    case 1: x=d;  y=b;  z=c;  w=a;  break; // qx dropped: a=qw,b=qy,c=qz,d=qx
                    case 2: x=b;  y=d;  z=c;  w=a;  break; // qy dropped: a=qw,b=qx,c=qz,d=qy
                    default:x=b;  y=c;  z=d;  w=a;  break; // qz dropped: a=qw,b=qx,c=qy,d=qz
                }
                rots[i] = new Quaternion(-x, y, -z, w); // negate qx,qz for Y-down → Y-up: R_Unity = M_Y*R_SOG*M_Y
            }
            return rots;
        }

        // -------------------------------------------------------------------------
        // Scales — codebook palette (R=x_idx, G=y_idx, B=z_idx)
        // -------------------------------------------------------------------------

        static Vector3[] DecodeScales(SOGMeta meta, int n,
            Func<string, Color32[]> load)
        {
            if (meta.scales == null) throw new InvalidDataException("SOG: 'scales' missing.");
            Color32[] pix = load(meta.scales.files[0]);
            float[] cb = meta.scales.codebook;
            var scales = new Vector3[n];
            for (int i = 0; i < n; i++)
                scales[i] = new Vector3(cb[pix[i].r], cb[pix[i].g], cb[pix[i].b]);
            return scales;
        }

        // -------------------------------------------------------------------------
        // SH0 — codebook palette, RGBA channels index same codebook
        // -------------------------------------------------------------------------

        // SOG sh0.webp: RGB channels index the SH DC codebook; Alpha is direct linear opacity [0,1].
        // Source: github.com/playcanvas/splat-transform — alpha = byte/255, NOT a codebook index.
        static void DecodeSH0(SOGMeta meta, int n, Func<string, Color32[]> load,
            out Vector3[] sh0Colors, out float[] opacityLinear)
        {
            if (meta.sh0 == null) throw new InvalidDataException("SOG: 'sh0' missing.");
            Color32[] pix = load(meta.sh0.files[0]);
            float[] cb = meta.sh0.codebook;
            sh0Colors     = new Vector3[n];
            opacityLinear = new float[n];
            for (int i = 0; i < n; i++)
            {
                sh0Colors[i]     = new Vector3(cb[pix[i].r], cb[pix[i].g], cb[pix[i].b]);
                opacityLinear[i] = pix[i].a / 255.0f; // linear [0,1], NOT a codebook lookup
            }
        }

        // -------------------------------------------------------------------------
        // ShN — two-level palette: labels → centroid index → quantized SH via codebook
        // -------------------------------------------------------------------------

        static float[][] DecodeShN(SOGMeta meta, int n, Func<string, Color32[]> load)
        {
            if (meta.shN?.codebook == null) return null;
            int bands = meta.shN.bands;
            int numClusters = meta.shN.count;
            float[] cb = meta.shN.codebook;

            int totalCoeffs = 0;
            for (int d = 1; d <= bands; d++)
                totalCoeffs += (2 * d + 1) * 3;

            Color32[] labelPix    = load(meta.shN.files[1]); // shN_labels.webp
            Color32[] centroidPix = load(meta.shN.files[0]); // shN_centroids.webp
            int pixelsPerCentroid = (totalCoeffs + 2) / 3;

            var shHigher = new float[n][];
            for (int i = 0; i < n; i++)
            {
                shHigher[i] = new float[totalCoeffs];
                int clusterIdx = Math.Min(
                    labelPix[i].r | (labelPix[i].g << 8), numClusters - 1);
                int centBase = clusterIdx * pixelsPerCentroid;
                int ci = 0;
                for (int p = 0; p < pixelsPerCentroid && ci < totalCoeffs; p++)
                {
                    int px = centBase + p;
                    if (px >= centroidPix.Length) break;
                    Color32 c = centroidPix[px];
                    if (ci < totalCoeffs) shHigher[i][ci++] = cb[c.r];
                    if (ci < totalCoeffs) shHigher[i][ci++] = cb[c.g];
                    if (ci < totalCoeffs) shHigher[i][ci++] = cb[c.b];
                }
            }
            return shHigher;
        }

        // -------------------------------------------------------------------------
        // Bounding box
        // -------------------------------------------------------------------------

        static void ComputeBounds(SOGRawData data)
        {
            if (data.positions == null || data.positions.Length == 0)
            { data.boundsMin = Vector3.zero; data.boundsMax = Vector3.one; return; }
            Vector3 mn = data.positions[0], mx = data.positions[0];
            for (int i = 1; i < data.positions.Length; i++)
            { mn = Vector3.Min(mn, data.positions[i]); mx = Vector3.Max(mx, data.positions[i]); }
            data.boundsMin = mn;
            data.boundsMax = mx;
        }

        // -------------------------------------------------------------------------
        // Helpers
        // -------------------------------------------------------------------------

        /// <summary>Fallback: uses Unity's built-in LoadImage (PNG/JPG only, NOT VP8L WebP).</summary>
        static Color32[] LoadImageFallback(string filename, byte[] imageBytes)
        {
            var tex = new Texture2D(2, 2, TextureFormat.RGBA32, false);
            if (!ImageConversion.LoadImage(tex, imageBytes))
                throw new InvalidDataException(
                    $"SOG: LoadImage failed for '{filename}'. " +
                    "Unity does not support lossless WebP (VP8L) via LoadImage. " +
                    "Use the editor importer path which uses AssetDatabase instead.");
            Color32[] pixels = tex.GetPixels32();
            UnityEngine.Object.DestroyImmediate(tex);
            return pixels;
        }

        static byte[] ReadEntryBytes(ZipArchive archive, string filename)
        {
            var entry = FindEntry(archive, filename)
                ?? throw new InvalidDataException($"SOG: '{filename}' not found in archive.");
            using var ms = new MemoryStream();
            entry.Open().CopyTo(ms);
            return ms.ToArray();
        }

        static ZipArchiveEntry FindEntry(ZipArchive archive, string name)
        {
            foreach (var e in archive.Entries)
                if (string.Equals(e.Name, name, StringComparison.OrdinalIgnoreCase))
                    return e;
            return null;
        }
    }
}
