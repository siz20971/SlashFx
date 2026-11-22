using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;
using Ympp.SlashEffectStudio.Editor.Rendering;

namespace Ympp.SlashEffectStudio.Editor.Baking
{
    public sealed class SpriteSheetBakeResult
    {
        public string textureAssetPath;
        public Texture2D texture;
        public Sprite[] sprites;
        public int effectiveFrameCount;
        public int columns;
        public int rows;
        public int cellWidth;
        public int cellHeight;
        public int sheetWidth;
        public int sheetHeight;
        public float fps;
        public float durationSeconds;
        public Vector2 normalizedPivot; // Y-up [0..1]
        public bool isUniformGrid;
        public bool gridUvExact;        // True if ParticleSystem Grid mode maps cleanly without margin offset
    }

    /// <summary>
    /// Bakes a SlashEffectAsset into a Unity SpriteSheet PNG, applies optional empty-frame trimming,
    /// ParticleSystem-safe POT scaling, 1px Alpha Bleed dilation, and automatic Multiple Sprite slicing.
    /// </summary>
    public static class SpriteSheetBaker
    {
        private struct CropBox
        {
            public int x;
            public int y;
            public int w;
            public int h;
            public bool empty;
        }

        public static SpriteSheetBakeResult Bake(SlashEffectAsset asset, bool reuseLinkedTexturePath = true)
        {
            if (asset == null) throw new ArgumentNullException(nameof(asset));
            asset.EnsureValid();

            var settings = asset.bakeSettings;
            string safeName = SanitizeFileName(string.IsNullOrEmpty(asset.effectName) ? asset.name : asset.effectName);

            // Resolve output path (preserve existing linked GUID on Hot-Rebake!)
            string outputPath = null;
            if (reuseLinkedTexturePath && !string.IsNullOrEmpty(settings.lastBakedTextureGuid))
            {
                string existing = AssetDatabase.GUIDToAssetPath(settings.lastBakedTextureGuid);
                if (!string.IsNullOrEmpty(existing) && existing.EndsWith(".png", StringComparison.OrdinalIgnoreCase))
                {
                    outputPath = existing;
                }
            }

            if (string.IsNullOrEmpty(outputPath))
            {
                string folder = string.IsNullOrWhiteSpace(settings.outputFolder)
                    ? "Assets/SlashEffects/Baked"
                    : settings.outputFolder.Replace('\\', '/').TrimEnd('/');
                EnsureAssetFolderExists(folder);
                outputPath = $"{folder}/{safeName}_Sheet.png";
            }
            else
            {
                EnsureAssetFolderExists(Path.GetDirectoryName(outputPath)?.Replace('\\', '/'));
            }

            int totalFrames = Mathf.Clamp(asset.playback.frameCount, 1, 128);
            var renderedFrames = new List<Texture2D>(totalFrames);
            var boxes = new List<CropBox>(totalFrames);

            using (var renderer = new SlashGpuRenderer())
            {
                for (int i = 0; i < totalFrames; i++)
                {
                    var frameTex = renderer.RenderFrameToTexture2D(asset, i, settings.alphaOutputMode);
                    renderedFrames.Add(frameTex);
                    boxes.Add(FindAlphaBoundingBox(frameTex, settings.alphaOutputMode));
                }
            }

            try
            {
                // Optional skip of completely transparent leading/trailing frames
                int startIdx = 0;
                int endIdx = totalFrames - 1;
                if (settings.skipEmptyBoundaryFrames)
                {
                    while (startIdx < endIdx && boxes[startIdx].empty) startIdx++;
                    while (endIdx > startIdx && boxes[endIdx].empty) endIdx--;
                }

                var activeFrames = new List<Texture2D>();
                var activeBoxes = new List<CropBox>();
                for (int i = startIdx; i <= endIdx; i++)
                {
                    activeFrames.Add(renderedFrames[i]);
                    activeBoxes.Add(boxes[i]);
                }

                int frameCount = activeFrames.Count;
                int fw = asset.canvasWidth;
                int fh = asset.canvasHeight;
                int cols = Mathf.Clamp(settings.columns, 1, frameCount);
                int rows = Mathf.CeilToInt(frameCount / (float)cols);
                int padding = Mathf.Clamp(settings.padding, 0, 32);
                int maxSize = Mathf.Clamp(settings.maxSize, 256, 8192);

                bool isUniform = settings.packMode == SheetPackMode.UniformGrid;
                // In ParticleSystem Grid mode with ScaleCellsToPot, enforce 0 padding or scale uniformly so Grid UV is 100% exact
                if (isUniform && settings.particleSheetMode == ParticleSheetMode.Grid && settings.potMode == SheetPotMode.ScaleCellsToPot)
                {
                    padding = 0;
                }

                int rawSheetW;
                int rawSheetH;
                int cellW = fw;
                int cellH = fh;

                var frameRectsTopDown = new RectInt[frameCount];
                var cropUsed = new CropBox[frameCount];

                if (isUniform)
                {
                    for (int i = 0; i < frameCount; i++)
                    {
                        cropUsed[i] = new CropBox { x = 0, y = 0, w = fw, h = fh, empty = activeBoxes[i].empty };
                    }

                    rawSheetW = cols * cellW + (cols - 1) * padding;
                    rawSheetH = rows * cellH + (rows - 1) * padding;

                    for (int i = 0; i < frameCount; i++)
                    {
                        int col = i % cols;
                        int row = i / cols;
                        int x = col * (cellW + padding);
                        int yTop = row * (cellH + padding);
                        frameRectsTopDown[i] = new RectInt(x, yTop, cellW, cellH);
                    }
                }
                else
                {
                    // TightTrim row packing
                    int cursorY = 0;
                    int maxRowW = 1;
                    for (int r = 0; r < rows; r++)
                    {
                        int rowStart = r * cols;
                        int rowEnd = Mathf.Min(frameCount, rowStart + cols);
                        int cursorX = 0;
                        int rowH = 1;
                        for (int i = rowStart; i < rowEnd; i++)
                        {
                            var b = activeBoxes[i];
                            cropUsed[i] = b;
                            rowH = Mathf.Max(rowH, b.h);
                            frameRectsTopDown[i] = new RectInt(cursorX, cursorY, b.w, b.h);
                            cursorX += b.w + (i < rowEnd - 1 ? padding : 0);
                        }
                        maxRowW = Mathf.Max(maxRowW, cursorX);
                        cursorY += rowH + (r < rows - 1 ? padding : 0);
                    }
                    rawSheetW = Mathf.Max(1, maxRowW);
                    rawSheetH = Mathf.Max(1, cursorY);
                    cellW = 0;
                    cellH = 0;
                    for (int i = 0; i < frameCount; i++)
                    {
                        cellW = Mathf.Max(cellW, cropUsed[i].w);
                        cellH = Mathf.Max(cellH, cropUsed[i].h);
                    }
                }

                // Determine final sheet dimensions & scaling
                int finalSheetW = rawSheetW;
                int finalSheetH = rawSheetH;
                float scaleX = 1f;
                float scaleY = 1f;

                if (rawSheetW > maxSize || rawSheetH > maxSize)
                {
                    float uniformDown = Mathf.Min(maxSize / (float)rawSheetW, maxSize / (float)rawSheetH);
                    scaleX = uniformDown;
                    scaleY = uniformDown;
                    finalSheetW = Mathf.Max(1, Mathf.FloorToInt(rawSheetW * uniformDown));
                    finalSheetH = Mathf.Max(1, Mathf.FloorToInt(rawSheetH * uniformDown));
                }

                bool gridUvExact = isUniform && padding == 0;

                if (settings.potMode == SheetPotMode.ScaleCellsToPot && isUniform)
                {
                    // Scale cells uniformly to fit exact POT texture dimensions so Grid UV slicing has zero offset!
                    int potW = Mathf.Min(Mathf.NextPowerOfTwo(Mathf.Max(64, finalSheetW)), Mathf.ClosestPowerOfTwo(maxSize));
                    int potH = Mathf.Min(Mathf.NextPowerOfTwo(Mathf.Max(64, finalSheetH)), Mathf.ClosestPowerOfTwo(maxSize));
                    if (potW > maxSize) potW = FloorPowerOfTwo(maxSize);
                    if (potH > maxSize) potH = FloorPowerOfTwo(maxSize);

                    scaleX = potW / (float)rawSheetW;
                    scaleY = potH / (float)rawSheetH;
                    finalSheetW = potW;
                    finalSheetH = potH;
                    gridUvExact = padding == 0;
                }
                else if (settings.potMode == SheetPotMode.PadCanvasToPot)
                {
                    int potW = Mathf.NextPowerOfTwo(Mathf.Max(64, finalSheetW));
                    int potH = Mathf.NextPowerOfTwo(Mathf.Max(64, finalSheetH));
                    if (potW > maxSize || potH > maxSize)
                    {
                        int limit = FloorPowerOfTwo(maxSize);
                        float down = Mathf.Min(limit / (float)rawSheetW, limit / (float)rawSheetH);
                        scaleX = down;
                        scaleY = down;
                        potW = Mathf.NextPowerOfTwo(Mathf.Max(64, Mathf.FloorToInt(rawSheetW * down)));
                        potH = Mathf.NextPowerOfTwo(Mathf.Max(64, Mathf.FloorToInt(rawSheetH * down)));
                    }
                    finalSheetW = potW;
                    finalSheetH = potH;
                    if (finalSheetW != Mathf.RoundToInt(rawSheetW * scaleX) || finalSheetH != Mathf.RoundToInt(rawSheetH * scaleY))
                    {
                        gridUvExact = false;
                    }
                }

                // Compose raw sheet Texture2D (Y-up!)
                Color32 bgClear = settings.alphaOutputMode == AlphaOutputMode.AdditiveBlackBg
                    ? new Color32(0, 0, 0, 255)
                    : new Color32(0, 0, 0, 0);

                var rawSheet = new Texture2D(rawSheetW, rawSheetH, TextureFormat.RGBA32, false, linear: false);
                var rawPixels = new Color32[rawSheetW * rawSheetH];
                Array.Fill(rawPixels, bgClear);
                rawSheet.SetPixels32(rawPixels);

                for (int i = 0; i < frameCount; i++)
                {
                    var box = cropUsed[i];
                    if (box.empty) continue;

                    var dstTop = frameRectsTopDown[i];
                    // Convert top-down row placement to Unity Texture2D bottom-left Y-up placement
                    // Row 0 sits at the TOP of the texture (Unity ParticleSystem Grid reads Left->Right, Top->Bottom!)
                    int dstYBottom = rawSheetH - dstTop.y - dstTop.height;
                    var srcPixels = activeFrames[i].GetPixels(box.x, box.y, box.w, box.h);
                    rawSheet.SetPixels(dstTop.x, dstYBottom, box.w, box.h, srcPixels);
                }
                rawSheet.Apply(false, false);

                // Resample/Pad to final sheet size using GPU Blit if needed
                Texture2D finalSheet = rawSheet;
                if (finalSheetW != rawSheetW || finalSheetH != rawSheetH)
                {
                    int contentW = Mathf.Clamp(Mathf.RoundToInt(rawSheetW * scaleX), 1, finalSheetW);
                    int contentH = Mathf.Clamp(Mathf.RoundToInt(rawSheetH * scaleY), 1, finalSheetH);
                    finalSheet = ResampleOrPadSheet(
                        rawSheet,
                        finalSheetW,
                        finalSheetH,
                        contentW,
                        contentH,
                        asset.appearance.enablePixelArt,
                        bgClear);
                    UnityEngine.Object.DestroyImmediate(rawSheet);
                }

                // Optional 1px Alpha Bleed Dilation to eliminate dark bilinear borders
                if (settings.alphaBleedDilation && settings.alphaOutputMode == AlphaOutputMode.StandardAlpha)
                {
                    ApplyAlphaBleed(finalSheet);
                }

                // Write PNG to disk
                byte[] pngBytes = finalSheet.EncodeToPNG();
                UnityEngine.Object.DestroyImmediate(finalSheet);

                string fullDiskPath = Path.GetFullPath(outputPath);
                Directory.CreateDirectory(Path.GetDirectoryName(fullDiskPath)!);
                File.WriteAllBytes(fullDiskPath, pngBytes);

                // Build Unity Multiple Sprite slices (Row 0 at top -> Unity Y-up rect)
                var slices = new List<BakedSpriteSlice>(frameCount);
                Vector2 pivotPx = new Vector2(fw * asset.pivot.x, fh * asset.pivot.y);

                for (int i = 0; i < frameCount; i++)
                {
                    var dstTop = frameRectsTopDown[i];
                    var box = cropUsed[i];

                    float rx = Mathf.Clamp(Mathf.Round(dstTop.x * scaleX), 0f, finalSheetW - 1f);
                    float rw = Mathf.Clamp(Mathf.Round(dstTop.width * scaleX), 1f, finalSheetW - rx);
                    float rh = Mathf.Clamp(Mathf.Round(dstTop.height * scaleY), 1f, finalSheetH);
                    // Row 0 is anchored at the TOP of the final sheet!
                    float ry = Mathf.Clamp(Mathf.Round(finalSheetH - (dstTop.y * scaleY) - rh), 0f, finalSheetH - rh);

                    Vector2 framePivot = new Vector2(
                        (pivotPx.x - box.x) / Mathf.Max(1f, box.w),
                        (pivotPx.y - box.y) / Mathf.Max(1f, box.h)
                    );

                    slices.Add(new BakedSpriteSlice
                    {
                        name = $"{safeName}_{i:000}",
                        rect = new Rect(rx, ry, rw, rh),
                        pivot = framePivot
                    });
                }

                Sprite[] sprites = TextureImporterUtility.ConfigureAndSliceSpriteSheet(
                    outputPath,
                    slices,
                    settings,
                    asset.appearance.enablePixelArt);

                var texAsset = AssetDatabase.LoadAssetAtPath<Texture2D>(outputPath);
                settings.lastBakedTextureGuid = AssetDatabase.AssetPathToGUID(outputPath);
                EditorUtility.SetDirty(asset);
                AssetDatabase.SaveAssets();

                int finalCellW = Mathf.Max(1, Mathf.RoundToInt(cellW * scaleX));
                int finalCellH = Mathf.Max(1, Mathf.RoundToInt(cellH * scaleY));

                return new SpriteSheetBakeResult
                {
                    textureAssetPath = outputPath,
                    texture = texAsset,
                    sprites = sprites,
                    effectiveFrameCount = frameCount,
                    columns = cols,
                    rows = rows,
                    cellWidth = finalCellW,
                    cellHeight = finalCellH,
                    sheetWidth = finalSheetW,
                    sheetHeight = finalSheetH,
                    fps = asset.playback.fps,
                    durationSeconds = frameCount / Mathf.Max(1f, asset.playback.fps),
                    normalizedPivot = asset.pivot,
                    isUniformGrid = isUniform,
                    gridUvExact = gridUvExact
                };
            }
            finally
            {
                foreach (var tex in renderedFrames)
                {
                    if (tex != null) UnityEngine.Object.DestroyImmediate(tex);
                }
            }
        }

        private static CropBox FindAlphaBoundingBox(Texture2D tex, AlphaOutputMode mode)
        {
            int w = tex.width;
            int h = tex.height;
            var pixels = tex.GetPixels32();
            bool additiveBlack = mode == AlphaOutputMode.AdditiveBlackBg;

            int minX = w, minY = h, maxX = -1, maxY = -1;
            for (int y = 0; y < h; y++)
            {
                int row = y * w;
                for (int x = 0; x < w; x++)
                {
                    var p = pixels[row + x];
                    bool visible = additiveBlack ? (p.r > 2 || p.g > 2 || p.b > 2) : (p.a > 1);
                    if (visible)
                    {
                        if (x < minX) minX = x;
                        if (x > maxX) maxX = x;
                        if (y < minY) minY = y;
                        if (y > maxY) maxY = y;
                    }
                }
            }

            if (maxX < minX || maxY < minY)
            {
                return new CropBox { x = 0, y = 0, w = 1, h = 1, empty = true };
            }

            return new CropBox
            {
                x = minX,
                y = minY,
                w = maxX - minX + 1,
                h = maxY - minY + 1,
                empty = false
            };
        }

        private static Texture2D ResampleOrPadSheet(
            Texture2D src,
            int finalW,
            int finalH,
            int contentW,
            int contentH,
            bool pointFilter,
            Color32 bgClear)
        {
            contentW = Mathf.Clamp(contentW, 1, finalW);
            contentH = Mathf.Clamp(contentH, 1, finalH);

            src.filterMode = pointFilter ? FilterMode.Point : FilterMode.Bilinear;
            src.wrapMode = TextureWrapMode.Clamp;

            var rtDesc = new RenderTextureDescriptor(contentW, contentH, RenderTextureFormat.ARGB32, 0)
            {
                sRGB = true,
                useMipMap = false
            };
            var rt = RenderTexture.GetTemporary(rtDesc);
            Graphics.Blit(src, rt);

            var prev = RenderTexture.active;
            RenderTexture.active = rt;
            var scaledContent = new Texture2D(contentW, contentH, TextureFormat.RGBA32, false, linear: false);
            scaledContent.ReadPixels(new Rect(0, 0, contentW, contentH), 0, 0);
            scaledContent.Apply(false, false);
            RenderTexture.active = prev;
            RenderTexture.ReleaseTemporary(rt);

            if (finalW == contentW && finalH == contentH)
            {
                return scaledContent;
            }

            // Place scaled content anchored at the TOP-LEFT of the padded canvas (Y-up: y = finalH - contentH)
            var padded = new Texture2D(finalW, finalH, TextureFormat.RGBA32, false, linear: false);
            var clearArr = new Color32[finalW * finalH];
            Array.Fill(clearArr, bgClear);
            padded.SetPixels32(clearArr);
            padded.SetPixels(0, finalH - contentH, contentW, contentH, scaledContent.GetPixels());
            padded.Apply(false, false);

            UnityEngine.Object.DestroyImmediate(scaledContent);
            return padded;
        }

        private static void ApplyAlphaBleed(Texture2D tex)
        {
            int w = tex.width;
            int h = tex.height;
            var src = tex.GetPixels32();
            var dst = new Color32[src.Length];
            Array.Copy(src, dst, src.Length);

            for (int y = 0; y < h; y++)
            {
                int row = y * w;
                for (int x = 0; x < w; x++)
                {
                    int idx = row + x;
                    if (src[idx].a > 0) continue;

                    int rSum = 0, gSum = 0, bSum = 0, count = 0;
                    for (int dy = -1; dy <= 1; dy++)
                    {
                        int ny = y + dy;
                        if (ny < 0 || ny >= h) continue;
                        int nRow = ny * w;
                        for (int dx = -1; dx <= 1; dx++)
                        {
                            int nx = x + dx;
                            if (nx < 0 || nx >= w) continue;
                            var np = src[nRow + nx];
                            if (np.a > 4)
                            {
                                rSum += np.r;
                                gSum += np.g;
                                bSum += np.b;
                                count++;
                            }
                        }
                    }

                    if (count > 0)
                    {
                        dst[idx] = new Color32(
                            (byte)(rSum / count),
                            (byte)(gSum / count),
                            (byte)(bSum / count),
                            0);
                    }
                }
            }

            tex.SetPixels32(dst);
            tex.Apply(false, false);
        }

        private static int FloorPowerOfTwo(int n)
        {
            int p = 64;
            while (p * 2 <= n) p *= 2;
            return p;
        }

        public static string SanitizeFileName(string raw)
        {
            if (string.IsNullOrWhiteSpace(raw)) return "SlashEffect";
            foreach (char c in Path.GetInvalidFileNameChars())
            {
                raw = raw.Replace(c, '_');
            }
            return raw.Trim();
        }

        public static void EnsureAssetFolderExists(string assetFolderPath)
        {
            if (string.IsNullOrEmpty(assetFolderPath)) return;
            assetFolderPath = assetFolderPath.Replace('\\', '/').TrimEnd('/');
            if (AssetDatabase.IsValidFolder(assetFolderPath)) return;

            var parts = assetFolderPath.Split('/');
            string current = parts[0];
            for (int i = 1; i < parts.Length; i++)
            {
                string next = $"{current}/{parts[i]}";
                if (!AssetDatabase.IsValidFolder(next))
                {
                    AssetDatabase.CreateFolder(current, parts[i]);
                }
                current = next;
            }
        }
    }
}
