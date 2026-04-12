// FreeImageWebP.cs
// Decodes WebP images (including lossless VP8L) via FreeImage.dll,
// which Unity ships in its Editor directory.
//
// FreeImage pixel layout (32-bit): BGRA byte order, rows bottom-to-top.
// We swap B↔R to match Unity's Color32 (RGBA) convention.

using System;
using System.Runtime.InteropServices;
using UnityEngine;

namespace GaussianSplatting.SOG.Editor
{
    internal static class FreeImageWebP
    {
        // FreeImage simple API — these are the only functions we need
        [DllImport("FreeImage", CallingConvention = CallingConvention.Cdecl)]
        static extern IntPtr FreeImage_OpenMemory(byte[] data, uint size_in_bytes);

        [DllImport("FreeImage", CallingConvention = CallingConvention.Cdecl)]
        static extern void FreeImage_CloseMemory(IntPtr stream);

        [DllImport("FreeImage", CallingConvention = CallingConvention.Cdecl)]
        static extern int FreeImage_GetFileTypeFromMemory(IntPtr stream, int size);

        [DllImport("FreeImage", CallingConvention = CallingConvention.Cdecl)]
        static extern IntPtr FreeImage_LoadFromMemory(int fif, IntPtr stream, int flags);

        [DllImport("FreeImage", CallingConvention = CallingConvention.Cdecl)]
        static extern IntPtr FreeImage_ConvertTo32Bits(IntPtr dib);

        [DllImport("FreeImage", CallingConvention = CallingConvention.Cdecl)]
        static extern void FreeImage_Unload(IntPtr dib);

        [DllImport("FreeImage", CallingConvention = CallingConvention.Cdecl)]
        static extern uint FreeImage_GetWidth(IntPtr dib);

        [DllImport("FreeImage", CallingConvention = CallingConvention.Cdecl)]
        static extern uint FreeImage_GetHeight(IntPtr dib);

        [DllImport("FreeImage", CallingConvention = CallingConvention.Cdecl)]
        static extern IntPtr FreeImage_GetScanLine(IntPtr dib, int scanline);

        /// <summary>
        /// Decode a WebP (or any FreeImage-supported format) byte array to Color32 pixels.
        /// Pixels are returned in bottom-to-top, left-to-right order (matching Unity's GetPixels32).
        /// </summary>
        public static Color32[] Decode(string filename, byte[] imageData)
        {
            IntPtr stream = FreeImage_OpenMemory(imageData, (uint)imageData.Length);
            if (stream == IntPtr.Zero)
                throw new InvalidOperationException(
                    $"FreeImage_OpenMemory failed for '{filename}'.");

            try
            {
                int fif = FreeImage_GetFileTypeFromMemory(stream, 0);
                if (fif < 0)
                    throw new InvalidOperationException(
                        $"FreeImage: Unknown format for '{filename}'. fif={fif}");

                IntPtr dib = FreeImage_LoadFromMemory(fif, stream, 0);
                if (dib == IntPtr.Zero)
                    throw new InvalidOperationException(
                        $"FreeImage: Load failed for '{filename}' (fif={fif}).");

                try
                {
                    // Ensure 32-bit BGRA
                    IntPtr dib32 = FreeImage_ConvertTo32Bits(dib);
                    if (dib32 != dib)
                    {
                        FreeImage_Unload(dib);
                        dib = dib32;
                    }

                    int w = (int)FreeImage_GetWidth(dib);
                    int h = (int)FreeImage_GetHeight(dib);
                    var pixels = new Color32[w * h];

                    // FreeImage scanline 0 = bottom row → matches Unity's GetPixels32 order
                    // FreeImage 32-bit = BGRA in memory → swap B and R to get RGBA
                    for (int y = 0; y < h; y++)
                    {
                        IntPtr row = FreeImage_GetScanLine(dib, y);
                        int baseIdx = y * w;
                        for (int x = 0; x < w; x++)
                        {
                            int off = x * 4;
                            byte b = Marshal.ReadByte(row, off);
                            byte g = Marshal.ReadByte(row, off + 1);
                            byte r = Marshal.ReadByte(row, off + 2);
                            byte a = Marshal.ReadByte(row, off + 3);
                            pixels[baseIdx + x] = new Color32(r, g, b, a);
                        }
                    }
                    return pixels;
                }
                finally
                {
                    FreeImage_Unload(dib);
                }
            }
            finally
            {
                FreeImage_CloseMemory(stream);
            }
        }
    }
}
