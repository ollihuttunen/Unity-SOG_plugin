# Key Discoveries — SOG to Unity Conversion

A record of the five decisive technical findings that made this project work,
and which sources provided the critical information.

---

## The Five Decisive Discoveries

### 1. Position Log-Transform — The Biggest Single Breakthrough

**Discovery:** The `mins` and `maxs` values in `meta.json` are **not in world space —
they are in log-space**. PlayCanvas applies the transform `sign(x)*log(|x|+1)` to
all position values before quantisation.

The inverse transform required during decoding:
```csharp
float InvLogTransform(float v) =>
    Mathf.Sign(v) * (Mathf.Exp(Mathf.Abs(v)) - 1f);
```

Applied during position decoding (after lerp between min/max):
```csharp
float lx = minX + (vx / 65535f) * rangeX; // lerp in log-space
positions[i] = new Vector3(
    InvLogTransform(lx),
    -InvLogTransform(ly),   // also negate Y for coordinate flip
    InvLogTransform(lz));
```

**Why decisive:** Without this, the entire 100-metre scene compressed into a ~5-unit
blob. This was the root cause of the "squishing toward edges" artifact. Adding this
single function transformed the rendering visually overnight.

**Source:** PlayCanvas `splat-transform` source code on GitHub — specifically the
section that packs positions into WebP images. The official documentation describes
the format structure but does not explicitly mention the log-transform. The source
code revealed it.

---

### 2. Correct Quaternion Y-Flip Formula — The Spike Remover

**Discovery:** Flipping the Y-axis in a coordinate transform requires
`R_Unity = M_Y × R_SOG × M_Y` where `M_Y = diag(1,−1,1)`. Solving the
resulting rotation matrix equations component by component gives:

```csharp
// WRONG (intuitively tempting but mathematically incorrect):
new Quaternion(qx, -qy, qz, qw)

// CORRECT:
new Quaternion(-qx, qy, -qz, qw)  // negate qx and qz — NOT qy
```

**Mathematical derivation summary:**

For M_Y = diag(1,−1,1), element (i,j) of M_Y·R·M_Y =
`M_Y[i,i] * R[i,j] * M_Y[j,j]`. Comparing these elements to the standard
quaternion rotation matrix formula yields the system:

| Condition | Result |
|---|---|
| `sign_w * sign_y = +1` | qy unchanged |
| `sign_w * sign_x = -1` | qx negated |
| `sign_x * sign_z = +1` | qz negated (same sign as qx) |

Setting `sign_w = +1` (canonical form): **qw unchanged, qx negated, qy unchanged, qz negated.**

**Why decisive:** "Negate qy when flipping Y" is the intuitive guess and is wrong.
The correct solution requires negating qx and qz. This was the root cause of
"spiky Gaussians pointing straight up" on the ground plane — Gaussians with
non-trivial qx or qz components were rotated incorrectly. Fixing this eliminated
the spikes completely.

**Source:** No external reference — derived from **pure linear algebra**. The
rotation matrix component formulas (quaternion → matrix), matrix product M_Y·R·M_Y,
and solving the resulting system of sign equations.

---

### 3. Aras's HLSL Shader as the Format Specification

**Discovery:** `GaussianSplatting.hlsl` — Aras's rendering shader — was the only
complete specification of the binary buffer format. It revealed:

- **`DecodeRotation`**: 10.10.10.2 "smallest-three" quaternion packing format and
  the three permutation cases (`q.wxyz`, `q.xwyz`, `q.xywz`)
- **Morton tiling**: colorData is stored in a 2048-wide virtual texture using
  Z-order (Morton) 16×16 tiles, not linear layout
- **SH memory layout**: sh1.rgb, sh2.rgb, ..., sh15.rgb sequentially (interleaved
  RGB per coefficient, not channel-separated)
- **Color encoding**: `col.rgb = sh0_coefficient * SH_C0 + 0.5` where `SH_C0 = 0.2820948`
- **Opacity**: stored directly as float `[0,1]` in colorData alpha — no sigmoid in shader

```hlsl
// Aras's DecodeRotation (GaussianSplatting.hlsl)
float4 DecodeRotation(float4 pq) {
    uint idx = (uint)round(pq.w * 3.0);
    float4 q;
    q.xyz = pq.xyz * sqrt(2.0) - (1.0 / sqrt(2.0));
    q.w = sqrt(1.0 - saturate(dot(q.xyz, q.xyz)));
    if (idx == 0) q = q.wxyz;
    if (idx == 1) q = q.xwyz;
    if (idx == 2) q = q.xywz;
    return q;
}
```

**Why decisive:** Aras's binary format has no separate documentation. The shader
**was** the documentation. Every incorrect implementation produced a directly
visible visual error — the shader revealed exactly what the data meant.

**Source:** `GaussianSplatting.hlsl` directly from the
[UnityGaussianSplatting repository](https://github.com/aras-p/UnityGaussianSplatting).

---

### 4. SOG vs SOGS — A Critical Name Distinction

**Discovery:** SOG (PlayCanvas **Spatially** Ordered Gaussians) and SOGS
(Fraunhofer **Self-Organizing** Gaussians) are completely different formats from
different organisations, despite their similar names.

| Property | SOG (PlayCanvas) | SOGS (Fraunhofer) |
|---|---|---|
| Container | ZIP archive | Custom binary |
| Image encoding | Lossless WebP (VP8L) | PNG |
| Opacity | Direct linear `byte/255` | Logit / sigmoid |
| Quaternion order | `[qw, qx, qy, qz]` | Different |
| Primary use | Web / game engine delivery | Research benchmark |

**Why decisive:** Early in the project, SOGS documentation was fetched when SOG
documentation was needed. Using the wrong format spec would have produced a
non-functional decoder. Catching this early prevented significant wasted effort.

**Source:** User correction during development — recognised the name discrepancy
and redirected to the correct PlayCanvas sources.

---

### 5. Opacity: Direct Value, Not a Logit

**Discovery:** The alpha channel of `sh0.webp` stores a **direct linear opacity**
`[0,1]` as `byte / 255`. It is not a logit value, and no sigmoid transform is needed.

```csharp
// WRONG (PLY convention — logit stored, sigmoid needed):
opacityLinear[i] = Sigmoid(codebook[pix[i].a]);

// CORRECT (SOG convention — linear value, already converted by encoder):
opacityLinear[i] = pix[i].a / 255.0f;
```

**Why decisive:** In the standard 3DGS PLY format, opacity is stored as a logit
and requires `sigmoid()` to convert to linear. The natural assumption was the same
for SOG. The PlayCanvas source code showed that `sigmoid(opacity)` is applied
*during encoding*, so the stored value is already linear.

**Source:** PlayCanvas `splat-transform` source code — the write path for
`sh0.webp` shows the sigmoid is applied before storage.

---

## Source Hierarchy

```
Most authoritative (ground truth)
    │
    ├── 1. PlayCanvas splat-transform source code
    │        github.com/playcanvas/splat-transform
    │        → log-transform, quaternion component order, opacity encoding
    │
    ├── 2. Aras's GaussianSplatting.hlsl
    │        github.com/aras-p/UnityGaussianSplatting
    │        → binary format, DecodeRotation, Morton tiling, SH layout
    │
    ├── 3. Mathematics (rotation matrices, SH basis functions)
    │        → quaternion Y-flip derivation, SH sign table for Y parity
    │
    └── 4. PlayCanvas SOG documentation
             developer.playcanvas.com
             → format overview (insufficient alone, missing critical details)
```

## Core Lesson

> **Official documentation tells you *what* a format does.
> Source code tells you *exactly how* it does it.**

In this project, source code was not optional — documentation alone would not have
been sufficient to build a correct decoder. The log-transform, the opacity encoding,
and the quaternion component ordering were only unambiguous in the source code.

The SH Y-flip and quaternion Y-flip formulas had no source at all — they required
deriving from first principles. No article or repository contained the answer;
the answer came from solving the linear algebra.

---

*This document is part of the
[Unity-SOG_plugin](https://github.com/ollihuttunen/Unity-SOG_plugin) project.*
