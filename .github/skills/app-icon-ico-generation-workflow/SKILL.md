---
name: app-icon-ico-generation-workflow
description: Use when generating or replacing the SGStudio application .ico file from multi-size PNG assets under LOGO/, especially src/app/res_standard/app.ico. Requires a 9-entry ICO with PNG-compressed 32-bit images at 16, 20, 24, 32, 40, 48, 64, 128, and 256 px; exact-size PNG sources are used when present, and missing sizes are generated from the 128 px source with high-quality transparent RGBA scaling.
---

# App Icon ICO Generation Workflow

## Goal

Generate `src/app/res_standard/app.ico` from `LOGO/` PNG assets while preserving the SGStudio ICO shape:

```text
16, 20, 24, 32, 40, 48, 64, 128, 256
```

Every ICO entry must be:

- PNG-compressed.
- 32-bit RGBA with transparency.
- Stored in the order above.

## Source Selection

Use exact-size PNG assets when they exist:

```text
LOGO/SGStudio_logo_16x16px.png
LOGO/SGStudio_logo_20x20px.png
LOGO/SGStudio_logo_24x24px.png
LOGO/SGStudio_logo_32x32px.png
LOGO/SGStudio_logo_64x64px.png
LOGO/SGStudio_logo_128x128px.png.png
```

Generate missing sizes from the 128 px source:

```text
40, 48, 256
```

Use high-quality scaling, preserve alpha, and keep the output square. Do not regenerate available small sizes from the 128 px source unless the exact-size source is absent or invalid; small icons may contain hand-tuned pixel detail.

## Procedure

1. Inspect the existing `app.ico` before replacing it.
   - Confirm its entry sizes, bit depth, and whether entries are PNG-compressed.
   - Preserve the existing entry set unless the user explicitly requests a different shape.

2. Inspect the source PNGs.
   - Confirm each available source has the expected dimensions.
   - Prefer `Format32bppArgb` / RGBA-compatible sources.

3. Build the ICO as a container.
   - Write an ICO header.
   - Write one directory entry per target size.
   - Embed PNG bytes for each entry.
   - Use `0` for the ICO width/height byte of the 256 px entry.

4. Avoid byte-array expansion bugs.
   - In PowerShell, do not let generated `byte[]` values flow through the pipeline as ordinary function output.
   - Prefer a typed C# helper, a deterministic script, or explicit collection handling that preserves each PNG as one byte array.

5. Replace `src/app/res_standard/app.ico`.
   - Keep `src/app/res_standard/app.qrc` unchanged unless the resource alias or file name changes.
   - Do not build or run by default; this is normally a static asset update.

## Validation

After generation, parse the ICO file and verify:

- Header `reserved = 0`, `type = 1`, `count = 9`.
- Entry dimensions are exactly:

```text
16, 20, 24, 32, 40, 48, 64, 128, 256
```

- Every entry reports `planes = 1` and `bitCount = 32`.
- Every embedded image starts with the PNG signature:

```text
89 50 4E 47 0D 0A 1A 0A
```

- Every embedded PNG IHDR width and height matches its ICO directory entry.
- The final byte range of the last entry ends exactly at the file length.
- A standard icon loader can open the result, for example `System.Drawing.Icon` on Windows.

## Expected Output

- `src/app/res_standard/app.ico` is replaced with the generated multi-size ICO.
- The ICO contains native images for common Windows, Qt, taskbar, title-bar, and high-DPI icon sizes.
- Static verification results are recorded in the task notes or final response.
