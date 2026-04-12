// SOGConverter.cs
// Converts SOGRawData (decoded Gaussian splat data) into the binary buffer format
// expected by GaussianSplatAsset (Aras Pranckevičius' UnityGaussianSplatting plugin).
//
// Binary layout (using VeryHigh / Float32 quality — no lossy compression):
//
//   posData    per splat: 3×float32 = 12 bytes  (x, y, z)
//   otherData  per splat: 4 bytes (rotation packed as 10.10.10.2 uint) + 12 bytes (3×float32 scale) = 16 bytes
//   colorData  per splat: 4×float32 = 16 bytes  (dc0.r, dc0.g, dc0.b, opacity_logit)
//   shData     per splat: 16×float3 = 192 bytes (sh1-shF + padding; dc0 is in colorData)
//   chunkData  not written (pass null) for Float32 quality

using System;
using System.IO;
using GaussianSplatting.Runtime;
using UnityEngine;

namespace GaussianSplatting.SOG
{
    /// <summary>
    /// Converts SOGRawData into a populated GaussianSplatAsset.
    /// </summary>
    public static class SOGConverter
    {
        // -------------------------------------------------------------------------
        // Public entry point
        // -------------------------------------------------------------------------

        /// <summary>
        /// Build binary buffers from raw SOG data and write them to the given paths.
        /// Returns a partially-configured GaussianSplatAsset (caller must call SetAssetFiles).
        /// This method is editor-safe (no AssetDatabase calls here).
        /// </summary>
        /// <param name="rawData">Decoded Gaussian splat data from SOGParser.</param>
        /// <param name="pathPos">Output path for position buffer (.bytes).</param>
        /// <param name="pathOther">Output path for rotation+scale buffer (.bytes).</param>
        /// <param name="pathColor">Output path for color+opacity buffer (.bytes).</param>
        /// <param name="pathSH">Output path for higher-order SH buffer (.bytes).</param>
        public static GaussianSplatAsset CreateAsset(
            SOGRawData rawData,
            string pathPos,
            string pathOther,
            string pathColor,
            string pathSH)
        {
            int n = rawData.count;

            // Write binary buffers
            File.WriteAllBytes(pathPos,   BuildPosData(rawData));
            File.WriteAllBytes(pathOther, BuildOtherData(rawData));
            File.WriteAllBytes(pathColor, BuildColorData(rawData));
            File.WriteAllBytes(pathSH,    BuildSHData(rawData));

            // Create and initialize asset
            var asset = ScriptableObject.CreateInstance<GaussianSplatAsset>();
            asset.Initialize(
                n,
                GaussianSplatAsset.VectorFormat.Float32,   // posFormat
                GaussianSplatAsset.VectorFormat.Float32,   // scaleFormat
                GaussianSplatAsset.ColorFormat.Float32x4,  // colorFormat
                GaussianSplatAsset.SHFormat.Float32,       // shFormat
                rawData.boundsMin,
                rawData.boundsMax,
                null                    // cameras — not stored in SOG format
            );

            return asset;
        }

        // -------------------------------------------------------------------------
        // Texture padding — Aras packs color data into a 2048-wide texture, height
        // rounded up to a multiple of 16 (swizzle tile size).  Only colorData
        // must be exactly texWidth*texHeight elements; pos/other/sh use splatCount.
        // -------------------------------------------------------------------------

        static int CalcColorTexelCount(int splatCount)
        {
            const int kTextureWidth = 2048;
            int height = Math.Max(1, (splatCount + kTextureWidth - 1) / kTextureWidth);
            height = (height + 15) / 16 * 16;
            return kTextureWidth * height;
        }

        // -------------------------------------------------------------------------
        // posData: 3×float32 per splat
        // -------------------------------------------------------------------------

        static byte[] BuildPosData(SOGRawData data)
        {
            int n = data.count;
            byte[] buf = new byte[n * 12]; // 3 × float32 = 12 bytes
            int off = 0;
            for (int i = 0; i < n; i++)
            {
                WriteFloat(buf, off,      data.positions[i].x); off += 4;
                WriteFloat(buf, off,      data.positions[i].y); off += 4;
                WriteFloat(buf, off,      data.positions[i].z); off += 4;
            }
            return buf;
        }

        // -------------------------------------------------------------------------
        // otherData: 4 bytes (packed rotation) + 12 bytes (scale) = 16 bytes per splat
        // -------------------------------------------------------------------------

        static byte[] BuildOtherData(SOGRawData data)
        {
            int n = data.count;
            byte[] buf = new byte[n * 16];
            int off = 0;
            for (int i = 0; i < n; i++)
            {
                uint packedRot = EncodeQuatSmallest3(data.rotations[i]);
                WriteUInt(buf, off, packedRot); off += 4;

                // Scale: codebook stores log-scale values (same as PLY format); apply exp + abs like Aras
                float sx = Mathf.Abs(Mathf.Exp(data.logScales[i].x));
                float sy = Mathf.Abs(Mathf.Exp(data.logScales[i].y));
                float sz = Mathf.Abs(Mathf.Exp(data.logScales[i].z));
                WriteFloat(buf, off, sx); off += 4;
                WriteFloat(buf, off, sy); off += 4;
                WriteFloat(buf, off, sz); off += 4;
            }
            return buf;
        }

        // -------------------------------------------------------------------------
        // colorData: 4×float32 per splat, stored in Morton-tiled texture layout
        //
        // The shader reads color via SplatIndexToPixelIndex() which uses 16×16
        // Morton tiles (Z-order).  The buffer must therefore be laid out so that
        // splat i's color lives at the offset corresponding to that Morton pixel.
        //
        // Value format (from GaussianSplatting.hlsl comment, line ~146):
        //   col.rgb = sh0 * SH_C0 + 0.5   (pre-computed, NOT raw SH coefficient)
        //   col.a   = sigmoid(opacity_logit)  (actual [0,1] opacity)
        // -------------------------------------------------------------------------

        const float kSH_C0 = 0.2820948f; // 1 / (2 * sqrt(pi))
        const int   kTexWidth = 2048;

        static byte[] BuildColorData(SOGRawData data)
        {
            int n      = data.count;
            int padded = CalcColorTexelCount(n); // texWidth * texHeight
            byte[] buf = new byte[padded * 16];  // 4 × float32 = 16 bytes

            for (int i = 0; i < n; i++)
            {
                // Map splat index → Morton-tiled texture pixel
                SplatIndexToPixelIndex(i, out int texX, out int texY);
                int off = (texY * kTexWidth + texX) * 16;

                WriteFloat(buf, off,      data.sh0Colors[i].x * kSH_C0 + 0.5f); off += 4;
                WriteFloat(buf, off,      data.sh0Colors[i].y * kSH_C0 + 0.5f); off += 4;
                WriteFloat(buf, off,      data.sh0Colors[i].z * kSH_C0 + 0.5f); off += 4;
                WriteFloat(buf, off,      data.opacityLogits[i]); // already linear [0,1] from alpha/255
            }
            return buf;
        }

        // C# port of HLSL SplatIndexToPixelIndex + DecodeMorton2D_16x16
        static void SplatIndexToPixelIndex(int idx, out int texX, out int texY)
        {
            int t = idx;
            // DecodeMorton2D_16x16: map lower 8 bits → packed (nibble_x | nibble_y<<8)
            t = (t & 0xFF) | ((t & 0xFE) << 7);
            t &= 0x5555;
            t = (t ^ (t >> 1)) & 0x3333;
            t = (t ^ (t >> 2)) & 0x0f0f;
            int mx = t & 0xF;
            int my = (t >> 8) & 0xF;

            int tileWidth = kTexWidth / 16;  // 128
            int tileIdx   = idx >> 8;        // 256 splats per 16×16 tile
            texX = (tileIdx % tileWidth) * 16 + mx;
            texY = (tileIdx / tileWidth) * 16 + my;
        }

        // -------------------------------------------------------------------------
        // shData: SHTableItemFloat32 per splat = 16×float3 = 192 bytes
        //   sh1..shF = higher-order SH coefficients (3 floats each)
        //   shPadding = 3 floats for 16-byte alignment
        //
        // Y-flip sign: when we negate Y (SOG Y-down → Unity Y-up), SH basis
        // functions with odd Y parity must be negated to keep shading correct.
        // Affected (0-based coeff index): 0,3,4,8,9,10
        // -------------------------------------------------------------------------

        // -1 where basis function is odd in Y (negate for Y-axis flip), else +1
        static readonly float[] kSHYFlipSign =
        {
            -1f,  // coeff 0 = sh1  (Y_{1,-1} ~ y)
             1f,  // coeff 1 = sh2  (Y_{1,0}  ~ z)
             1f,  // coeff 2 = sh3  (Y_{1,+1} ~ x)
            -1f,  // coeff 3 = sh4  (Y_{2,-2} ~ xy)
            -1f,  // coeff 4 = sh5  (Y_{2,-1} ~ yz)
             1f,  // coeff 5 = sh6  (Y_{2,0})
             1f,  // coeff 6 = sh7  (Y_{2,+1} ~ xz)
             1f,  // coeff 7 = sh8  (Y_{2,+2})
            -1f,  // coeff 8 = sh9  (Y_{3,-3} ~ y(3x²-y²))
            -1f,  // coeff 9 = sh10 (Y_{3,-2} ~ xyz)
            -1f,  // coeff10 = sh11 (Y_{3,-1} ~ y(4z²-x²-y²))
             1f,  // coeff11 = sh12 (Y_{3,0})
             1f,  // coeff12 = sh13 (Y_{3,+1})
             1f,  // coeff13 = sh14 (Y_{3,+2})
             1f,  // coeff14 = sh15 (Y_{3,+3})
        };

        static byte[] BuildSHData(SOGRawData data)
        {
            int n = data.count;
            const int bytesPerSplat = 192; // 16 × float3 = 16 × 12 = 192
            byte[] buf = new byte[n * bytesPerSplat];
            int off = 0;

            for (int i = 0; i < n; i++)
            {
                // 15 SH coefficient vectors (sh1..sh15), each is float3 (rgb)
                // shHigher[i] stores them as R0G0B0, R1G1B1, ... (interleaved RGB per coefficient)
                for (int coeff = 0; coeff < 15; coeff++)
                {
                    float r = 0f, g = 0f, b = 0f;
                    if (data.shHigher != null && data.shHigher[i] != null)
                    {
                        int baseIdx = coeff * 3;
                        if (baseIdx + 2 < data.shHigher[i].Length)
                        {
                            r = data.shHigher[i][baseIdx];
                            g = data.shHigher[i][baseIdx + 1];
                            b = data.shHigher[i][baseIdx + 2];
                        }
                    }
                    float sign = kSHYFlipSign[coeff];
                    WriteFloat(buf, off, r * sign); off += 4;
                    WriteFloat(buf, off, g * sign); off += 4;
                    WriteFloat(buf, off, b * sign); off += 4;
                }
                // padding (float3)
                WriteFloat(buf, off, 0f); off += 4;
                WriteFloat(buf, off, 0f); off += 4;
                WriteFloat(buf, off, 0f); off += 4;
            }
            return buf;
        }

        // -------------------------------------------------------------------------
        // Quaternion encoding — Aras's 10.10.10.2 "smallest-three" format
        //
        // HLSL decode (GaussianSplatting.hlsl):
        //   pq.xyz = (bits[0:9]/1023, bits[10:19]/1023, bits[20:29]/1023) in [0,1]
        //   q.xyz  = pq.xyz * sqrt(2) - 1/sqrt(2)   → maps [0,1] → [-0.707, +0.707]
        //   q.w    = sqrt(1 - dot(q.xyz, q.xyz))      → reconstructed largest component
        //   idx    = round(pq.w * 3)                  → which component was omitted
        //   permute q.xyzw according to idx
        //
        // So the encoder must:
        //   1. Find the largest-magnitude component → idx (0=x,1=y,2=z,3=w)
        //   2. Negate all 4 if the largest is negative (canonical form)
        //   3. Store the 3 remaining ("small") components, mapped [−0.707,+0.707]→[0,1]
        //   4. Store idx in the top 2 bits via (idx/3)*3.5
        // -------------------------------------------------------------------------

        static uint EncodeQuatSmallest3(Quaternion q)
        {
            float qx = q.x, qy = q.y, qz = q.z, qw = q.w;

            float absX = Mathf.Abs(qx), absY = Mathf.Abs(qy);
            float absZ = Mathf.Abs(qz), absW = Mathf.Abs(qw);

            int idx;
            float c0, c1, c2, largest;

            // idx = which component has the largest absolute value (it gets dropped)
            // Component order stored in pq.xyz for each idx (matches HLSL permutation):
            //   idx=0 (x dropped) → store y, z, w
            //   idx=1 (y dropped) → store x, z, w
            //   idx=2 (z dropped) → store x, y, w
            //   idx=3 (w dropped) → store x, y, z
            if (absX >= absY && absX >= absZ && absX >= absW)
            { idx = 0; largest = qx; c0 = qy; c1 = qz; c2 = qw; }
            else if (absY >= absX && absY >= absZ && absY >= absW)
            { idx = 1; largest = qy; c0 = qx; c1 = qz; c2 = qw; }
            else if (absZ >= absX && absZ >= absY && absZ >= absW)
            { idx = 2; largest = qz; c0 = qx; c1 = qy; c2 = qw; }
            else
            { idx = 3; largest = qw; c0 = qx; c1 = qy; c2 = qz; }

            // Canonical form: largest component must be positive
            if (largest < 0f) { c0 = -c0; c1 = -c1; c2 = -c2; }

            // Map [-1/√2, +1/√2] → [0, 1]:  v = c / √2 + 0.5
            const float rcpSqrt2 = 0.70710678118f;
            float v0 = c0 * rcpSqrt2 + 0.5f;
            float v1 = c1 * rcpSqrt2 + 0.5f;
            float v2 = c2 * rcpSqrt2 + 0.5f;

            return ((uint)(v0 * 1023.5f) & 0x3FF)
                 | (((uint)(v1 * 1023.5f) & 0x3FF) << 10)
                 | (((uint)(v2 * 1023.5f) & 0x3FF) << 20)
                 | (((uint)(idx / 3.0f * 3.5f)) << 30);
        }

        // -------------------------------------------------------------------------
        // Low-level binary write helpers (little-endian)
        // -------------------------------------------------------------------------

        static void WriteFloat(byte[] buf, int offset, float value)
        {
            byte[] bytes = BitConverter.GetBytes(value);
            if (!BitConverter.IsLittleEndian) Array.Reverse(bytes);
            Buffer.BlockCopy(bytes, 0, buf, offset, 4);
        }

        static void WriteUInt(byte[] buf, int offset, uint value)
        {
            buf[offset]     = (byte)(value & 0xFF);
            buf[offset + 1] = (byte)((value >> 8)  & 0xFF);
            buf[offset + 2] = (byte)((value >> 16) & 0xFF);
            buf[offset + 3] = (byte)((value >> 24) & 0xFF);
        }
    }
}
