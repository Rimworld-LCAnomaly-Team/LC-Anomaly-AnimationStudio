using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using RimWorld;
using UnityEngine;
using Verse;

namespace LCAnimationStudio.Runtime
{
    public sealed class ContactSheetExporter
    {
        private readonly AnimatedGifEncoder gifEncoder = new AnimatedGifEncoder();

        public string Export(Pawn pawn, RuntimePreviewController preview, IEnumerable<int> ticks, string outputPath)
        {
            if (pawn == null || preview?.Animation == null) throw new InvalidOperationException("A preview Pawn and animation are required.");
            int[] frames = ticks.Distinct().OrderBy(tick => tick).ToArray();
            if (frames.Length == 0) throw new InvalidOperationException("At least one contact-sheet tick is required.");
            const int tile = 192;
            int columns = Math.Min(4, frames.Length);
            int rows = (frames.Length + columns - 1) / columns;
            var sheet = new Texture2D(columns * tile, rows * tile, TextureFormat.RGBA32, false);
            Color[] clear = Enumerable.Repeat(new Color(0.08f, 0.08f, 0.08f, 1f), sheet.width * sheet.height).ToArray();
            sheet.SetPixels(clear);
            int originalTick = preview.RequestedTick;
            var temporaryTextures = new List<Texture2D>();
            try
            {
                for (int i = 0; i < frames.Length; i++)
                {
                    preview.SetTick(frames[i]);
                    PortraitsCache.SetDirty(pawn);
                    RenderTexture render = PortraitsCache.Get(pawn, new Vector2(tile, tile), Rot4.South,
                        supersample: false, compensateForUIScale: false, renderHeadgear: true, renderClothes: true);
                    Texture2D texture = render.CreateTexture2D(TextureFormat.RGBA32, false);
                    temporaryTextures.Add(texture);
                    int x = (i % columns) * tile;
                    int y = (rows - 1 - i / columns) * tile;
                    sheet.SetPixels(x, y, tile, tile, texture.GetPixels());
                    Color marker = Color.Lerp(Color.cyan, Color.magenta, frames[i] / (float)Math.Max(1, preview.Animation.durationTicks));
                    for (int px = 0; px < tile; px++)
                        for (int py = 0; py < 4; py++) sheet.SetPixel(x + px, y + py, marker);
                }
                sheet.Apply(false, false);
                string fullPath = Path.GetFullPath(outputPath);
                Directory.CreateDirectory(Path.GetDirectoryName(fullPath));
                File.WriteAllBytes(fullPath, sheet.EncodeToPNG());
                return fullPath;
            }
            finally
            {
                preview.SetTick(originalTick);
                foreach (Texture2D texture in temporaryTextures) UnityEngine.Object.Destroy(texture);
                UnityEngine.Object.Destroy(sheet);
            }
        }

        public string ExportGif(Pawn pawn, RuntimePreviewController preview, IEnumerable<int> ticks, string outputPath)
        {
            if (pawn == null || preview?.Animation == null) throw new InvalidOperationException("A preview Pawn and animation are required.");
            int[] frameTicks = ticks.Distinct().OrderBy(tick => tick).ToArray();
            if (frameTicks.Length == 0) throw new InvalidOperationException("At least one GIF tick is required.");
            const int size = 192;
            int originalTick = preview.RequestedTick;
            var textures = new List<Texture2D>();
            try
            {
                foreach (int tick in frameTicks)
                {
                    preview.SetTick(tick);
                    PortraitsCache.SetDirty(pawn);
                    RenderTexture render = PortraitsCache.Get(pawn, new Vector2(size, size), Rot4.South,
                        supersample: false, compensateForUIScale: false, renderHeadgear: true, renderClothes: true);
                    textures.Add(render.CreateTexture2D(TextureFormat.RGBA32, false));
                }
                string fullPath = Path.GetFullPath(outputPath);
                gifEncoder.Write(fullPath, textures, 8);
                return fullPath;
            }
            finally
            {
                preview.SetTick(originalTick);
                foreach (Texture2D texture in textures) UnityEngine.Object.Destroy(texture);
            }
        }
    }
}
