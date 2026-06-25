# Unity SOG Plugin

A Unity UPM package that adds [SOG (Spatially Ordered Gaussians)](https://developer.playcanvas.com/user-manual/gaussian-splatting/formats/sog/) format support to 3D Gaussian Splatting plugins for Unity.

SOG is a compressed 3D Gaussian Splatting format by PlayCanvas that achieves ~15–20× smaller file sizes compared to PLY, using WebP images and vector quantization codebooks inside a ZIP archive.

![Unity SOG import result](https://img.shields.io/badge/Unity-2022.3%2B-blue) ![Status](https://img.shields.io/badge/status-working-brightgreen)

---

## Features

- **Editor import** — drag a `.sog` file into the Unity Project window; it auto-imports as a `GaussianSplatAsset`
- **Full quality** — uses Float32 buffers (no lossy re-compression)
- **Higher-order SH** — supports spherical harmonics up to band 3 (15 coefficients)
- **Correct coordinate conversion** — handles SOG's Y-down → Unity Y-up transform for positions, rotations, and SH coefficients
- **Runtime loader component** — `SOGRuntimeLoader` MonoBehaviour for assigning pre-imported assets at runtime

---

## Two install options

This plugin works with two different base Gaussian Splatting plugins. Choose based on your use case:

| | Standard | VR |
|---|---|---|
| **Base plugin** | [Aras Pranckevičius's UnityGaussianSplatting](https://github.com/aras-p/UnityGaussianSplatting) | [ninjamode's Unity-VR-Gaussian-Splatting](https://github.com/ninjamode/Unity-VR-Gaussian-Splatting) |
| **SOG plugin version** | **v0.1.0** | **v0.1.1** |
| **VR / stereo rendering** | No | Yes (stereo eye handling) |
| **Quest support** | No | Yes (Quest 2 / 3 / Pro optimised) |
| **Multi-layer splats** | No | Yes |
| **GPU sorting** | Standard | DeviceRadixSort + AMD FidelityFX FFX |
| **Base plugin version** | 1.1.1 (latest Aras) | 0.9.0 (NinjaMode fork) |

> **Note:** The two versions use different C# APIs. v0.1.0 compiles only against Aras's plugin; v0.1.1 compiles only against NinjaMode's VR fork. The SOG file format, binary buffers, and rendering quality are identical.

---

## Requirements

| Dependency | Version |
|---|---|
| Unity | 2022.3 or newer |
| Base Gaussian Splatting plugin | see table above |
| [libwebp](https://chromium.googlesource.com/webm/libwebp) native library | 1.4.0 (bundled) |

> **Note:** Unity's built-in `ImageConversion.LoadImage` does **not** support VP8L lossless WebP.  
> The editor importer requires the native `libwebp.dll` (bundled inside the package — no separate download needed).

---

## Installation — Standard (desktop, v0.1.0)

Download both `.tgz` files from the [v0.1.0 release](https://github.com/ollihuttunen/Unity-SOG_plugin/releases/tag/v0.1.0):

| File | Description |
|---|---|
| `org.nesnausk.gaussian-splatting-1.1.1.tgz` | Aras Pranckevičius's UnityGaussianSplatting (MIT) |
| `com.ollihuttunen.sog-gaussian-splatting-0.1.0.tgz` | This plugin |

### Step 1 — Install UnityGaussianSplatting

In Unity Package Manager → **`+`** → **Add package from tarball** → select `org.nesnausk.gaussian-splatting-1.1.1.tgz`.

### Step 2 — Install this plugin

In Unity Package Manager → **`+`** → **Add package from tarball** → select `com.ollihuttunen.sog-gaussian-splatting-0.1.0.tgz`.

---

## Installation — VR / Quest (v0.1.1)

Download both `.tgz` files from the [latest release](https://github.com/ollihuttunen/Unity-SOG_plugin/releases/latest):

| File | Description |
|---|---|
| `org.nesnausk.gaussian-splatting-0.9.0.tgz` | ninjamode's Unity VR Gaussian Splatting fork (MIT) |
| `com.ollihuttunen.sog-gaussian-splatting-0.1.1.tgz` | This plugin (VR-compatible build) |

### Step 1 — Install NinjaMode's VR fork

In Unity Package Manager → **`+`** → **Add package from tarball** → select `org.nesnausk.gaussian-splatting-0.9.0.tgz`.

### Step 2 — Install this plugin

In Unity Package Manager → **`+`** → **Add package from tarball** → select `com.ollihuttunen.sog-gaussian-splatting-0.1.1.tgz`.

### Step 3 — Install XR packages (VR only)

For actual VR rendering, also install:
- **XR Plugin Management** (Unity Registry)
- **OpenXR Plugin** or **Oculus XR Plugin** (depending on your headset)

> **Note:** SOG import and desktop preview work without XR packages. You only need them for actual VR headset output.

---

## Common setup (both versions)

> **Unity will show an "unsigned package" warning for tarball installs — this is expected. Click Install to proceed.**

### Enable Gaussian Splat rendering in URP

> **This is required.** Without it splats are invisible even after a successful import.

1. In the **Project** window, open your URP Renderer asset — typically at `Settings/PC_Renderer` or `Assets/Settings/URP-Balanced-Renderer`
2. In the **Inspector**, scroll to the bottom and click **Add Renderer Feature**
3. Select **Gaussian Splat Renderer Feature**

This is a one-time setup per project.

---

## Usage

### Step 1 — Import a .sog file

1. Drag a `.sog` file into your Unity **Project** window
2. Unity imports it automatically and creates several files next to it:
   - `YourFile.asset` — the `GaussianSplatAsset`
   - `YourFile_pos.bytes`, `YourFile_oth.bytes`, `YourFile_col.bytes`, `YourFile_shs.bytes` — binary data buffers

### Step 2 — Add to scene

1. Create an empty **GameObject** in the scene
2. Add a **GaussianSplatRenderer** component to it
3. Drag `YourFile.asset` into the renderer's **Asset** field

The splat should now be visible in the Scene and Game views.

### Runtime (pre-imported assets)

Add `SOGRuntimeLoader` to the same GameObject as `GaussianSplatRenderer`:

```
GameObject
  ├── GaussianSplatRenderer
  └── SOGRuntimeLoader
        ├── Load Source: Direct Reference
        └── Asset Reference: [drag your .asset here]
```

Or load from a `Resources/` folder:

```
GameObject
  ├── GaussianSplatRenderer
  └── SOGRuntimeLoader
        ├── Load Source: Resources
        └── Resources Path: "Splats/MyScene"
```

---

## SOG Format Overview

SOG files are ZIP archives containing:

| File | Description |
|---|---|
| `meta.json` | Splat count, codebooks, file references |
| `means_l.webp` + `means_u.webp` | 16-bit quantized positions (lower + upper bytes) |
| `quats.webp` | Rotations (smallest-three quaternion encoding) |
| `scales.webp` | Log-scale values via 256-entry codebook |
| `sh0.webp` | DC color coefficients + opacity |
| `shN_centroids.webp` + `shN_labels.webp` | Higher-order SH via two-level palette |

### Key technical details

**Positions use a log transform:**  
`meta.json` mins/maxs are in log-space: `logVal = sign(x)*log(|x|+1)`.  
Inverse: `x = sign(logVal) * (exp(|logVal|) - 1)`.

**Quaternion component order:** `[qw, qx, qy, qz]` (PlayCanvas internal convention).  
Alpha channel in `quats.webp`: `252 + dropped_component_index`.

**Opacity:** `sh0.webp` alpha channel is direct linear `[0,1]` = `byte/255`. Not a logit, not a codebook lookup.

**Y-axis coordinate conversion** (SOG Y-down → Unity Y-up):
- Position: `(x, -y, z)`
- Quaternion: `(-qx, qy, -qz, qw)` — negate qx and qz (not qy!)
- SH coefficients: negate bands with odd Y parity (sh1, sh4, sh5, sh9, sh10, sh11)

---

## Project Structure

```
Unity-SOG_plugin/
├── package.json
├── Runtime/
│   ├── SOGData.cs              # Data structures (SOGMeta, SOGRawData, etc.)
│   ├── SOGParser.cs            # ZIP reading, WebP decoding, per-splat data extraction
│   ├── SOGConverter.cs         # SOGRawData → GaussianSplatAsset binary buffers
│   └── SOGRuntimeLoader.cs     # MonoBehaviour for runtime asset assignment
├── Editor/
│   ├── SOGImporter.cs          # ScriptedImporter (.sog → binary buffers)
│   ├── SOGImporterEditor.cs    # Custom Inspector for importer
│   ├── SOGPostprocessor.cs     # Creates GaussianSplatAsset after buffers import
│   └── LibWebPDecoder.cs       # Native libwebp P/Invoke wrapper
└── Plugins/
    └── x86_64/
        └── libwebp.dll             # bundled in release package
```

---

## Known Limitations

- **Windows x64 only (primary target)** — the libwebp native plugin is configured for Windows x64. The C# code itself is fully cross-platform; only the native library needs to be swapped for other platforms.
- **Editor import only** — true runtime `.sog` loading (without prior editor import) is not yet supported. `GaussianSplatAsset` requires `TextAsset` sub-assets that can only be created in the editor.
- **Unity 2022.3+ only** — uses `ScriptedImporter` and URP Renderer Feature APIs.
- **VR version based on NinjaMode fork v0.9.0** — the VR fork is not synced with the latest Aras upstream (v1.1.1). Future upstream improvements may not be reflected.

### Unity 2022.3 notes

**DirectX 12 required.** The Gaussian Splat compute shaders use Wave Intrinsics (`wavebasic`, `waveballot`) which require DX12. Unity 2022.3 defaults to DX11, so splats will not render without this change:

1. **Edit → Project Settings → Player → Other Settings → Rendering**
2. Uncheck **Auto Graphics API**
3. Click **+** → add **Direct3D12** and move it to the top of the list
4. Restart Unity when prompted

**Version Control package errors.** Unity 2022.3 projects that include the built-in **Version Control** (Plastic SCM) package may show `CS0006` metadata errors that prevent all scripts from compiling. If you see these errors:

- Package Manager → find **Version Control** → **Remove**
- Then delete the `Library` folder and reopen the project to force a clean reimport

### macOS note

The plugin has not been tested on macOS but the C# code should work as-is. If you want to try it, you need `libwebp.dylib` instead of `libwebp.dll`. The easiest way to get it is via Homebrew:

```bash
brew install webp
# library location: /opt/homebrew/lib/libwebp.dylib  (Apple Silicon)
#                   /usr/local/lib/libwebp.dylib      (Intel Mac)
```

Place the `.dylib` in `Plugins/macOS/` inside the package folder and configure its platform in the Unity Inspector. Contributions adding official macOS support are welcome.

---

## How It Works

The import pipeline:

```
.sog file
  └─ SOGImporter (ScriptedImporter)
       └─ SOGParser → SOGRawData
            └─ SOGConverter → binary buffers (.bytes files)
                 └─ SOGPostprocessor → GaussianSplatAsset (.asset)
                      └─ GaussianSplatRenderer (base plugin renders it)
```

1. `SOGParser` opens the ZIP, reads `meta.json`, decodes each WebP image using the native libwebp library, and produces `SOGRawData` with per-splat positions, rotations, scales, colors, and SH coefficients.
2. `SOGConverter` transforms the raw data into the binary buffer format expected by `GaussianSplatAsset` (Float32 quality, Morton-tiled color texture, 10.10.10.2 quaternion encoding).
3. `SOGPostprocessor` detects the generated `.bytes` files and assembles the final `GaussianSplatAsset` with all `TextAsset` references.

---

## License

MIT License — see [LICENSE](LICENSE) file.

---

## Acknowledgements

- [Aras Pranckevičius](https://github.com/aras-p) for the [UnityGaussianSplatting](https://github.com/aras-p/UnityGaussianSplatting) plugin — the rendering core and binary format that this plugin targets
- [ninjamode](https://github.com/ninjamode) for the [Unity-VR-Gaussian-Splatting](https://github.com/ninjamode/Unity-VR-Gaussian-Splatting) fork — adds stereo VR rendering, Quest optimisations, multi-layer splat support, and improved GPU sorting (DeviceRadixSort / AMD FidelityFX FFX); published in [IEEE TVCG 2025](https://doi.org/10.1109/TVCG.2025.3549110)
- [PlayCanvas](https://playcanvas.com) for the [SOG format specification](https://developer.playcanvas.com/user-manual/gaussian-splatting/formats/sog/) and [splat-transform](https://github.com/playcanvas/splat-transform) reference implementation
- [Google WebM Project](https://www.webmproject.org) for libwebp
