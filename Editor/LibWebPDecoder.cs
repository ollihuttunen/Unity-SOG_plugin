// LibWebPDecoder.cs
// WebP decoder using Google's libwebp native library via P/Invoke.
//
// SETUP: Place libwebp.dll in:
//   Unity-SOG_plugin/Plugins/x86_64/libwebp.dll
//
// Download (Windows x64):
//   https://storage.googleapis.com/downloads.webmproject.org/releases/webp/libwebp-1.4.0-windows-x64.zip
//   Extract: libwebp-1.4.0-windows-x64\lib\libwebp.dll

using System;
using System.IO;
using System.Runtime.InteropServices;
using UnityEngine;

namespace GaussianSplatting.SOG.Editor
{
    internal static class LibWebPDecoder
    {
        // WebPDecodeRGBA: decode WebP → raw RGBA bytes
        // Returns pointer to heap-allocated RGBA data (width*height*4 bytes), or null on failure.
        [DllImport("libwebp", CallingConvention = CallingConvention.Cdecl)]
        static extern IntPtr WebPDecodeRGBA(
            [In] byte[] data,
            UIntPtr data_size,
            ref int width,
            ref int height);

        // Free memory allocated by WebPDecodeRGBA
        [DllImport("libwebp", CallingConvention = CallingConvention.Cdecl)]
        static extern void WebPFree(IntPtr ptr);

        /// <summary>
        /// Decode WebP bytes to Color32 pixels (RGBA, bottom-to-top to match Unity GetPixels32).
        /// </summary>
        public static Color32[] Decode(string filename, byte[] webpData)
        {
            int width = 0, height = 0;
            IntPtr ptr = WebPDecodeRGBA(webpData, (UIntPtr)webpData.Length, ref width, ref height);

            if (ptr == IntPtr.Zero)
                throw new InvalidOperationException(
                    $"libwebp: WebPDecodeRGBA failed for '{filename}'. " +
                    "Ensure libwebp.dll is placed in Plugins/x86_64/ of the SOG plugin.");

            try
            {
                int count = width * height;
                var pixels = new Color32[count];

                // WebP decodes top-to-bottom (row 0 = top of image).
                // SOG data pixel[i] must correspond to splat[i] in natural image order,
                // so we keep the original top-to-bottom ordering — no flip.
                int total = width * height;
                for (int j = 0; j < total; j++)
                {
                    int off4 = j * 4;
                    pixels[j] = new Color32(
                        Marshal.ReadByte(ptr, off4),
                        Marshal.ReadByte(ptr, off4 + 1),
                        Marshal.ReadByte(ptr, off4 + 2),
                        Marshal.ReadByte(ptr, off4 + 3));
                }
                return pixels;
            }
            finally
            {
                WebPFree(ptr);
            }
        }

        /// <summary>Check whether libwebp.dll can be found and loaded.</summary>
        public static bool IsAvailable()
        {
            try
            {
                int w = 0, h = 0;
                // Minimal call with invalid data — returns null, but if DLL loads, no exception.
                WebPDecodeRGBA(new byte[] { 0 }, (UIntPtr)1, ref w, ref h);
                return true;
            }
            catch (DllNotFoundException)
            {
                return false;
            }
            catch
            {
                return true; // DLL loaded, call just returned null — that's fine
            }
        }
    }
}
