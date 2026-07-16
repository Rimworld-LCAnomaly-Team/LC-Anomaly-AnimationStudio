using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEngine;

namespace LCAnimationStudio.Runtime
{
    public sealed class AnimatedGifEncoder
    {
        public void Write(string path, IReadOnlyList<Texture2D> frames, int delayCentiseconds = 8)
        {
            if (frames == null || frames.Count == 0) throw new ArgumentException("At least one GIF frame is required.", nameof(frames));
            int width = frames[0].width;
            int height = frames[0].height;
            foreach (Texture2D frame in frames)
                if (frame.width != width || frame.height != height) throw new InvalidDataException("All GIF frames must have identical dimensions.");

            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path)));
            using (var stream = File.Create(path))
            using (var writer = new BinaryWriter(stream, Encoding.ASCII))
            {
                writer.Write(Encoding.ASCII.GetBytes("GIF89a"));
                writer.Write((ushort)width);
                writer.Write((ushort)height);
                writer.Write((byte)0xF7); // global 256-color table
                writer.Write((byte)0);
                writer.Write((byte)0);
                WritePalette(writer);
                WriteLoopExtension(writer);
                foreach (Texture2D frame in frames) WriteFrame(writer, frame, delayCentiseconds);
                writer.Write((byte)0x3B);
            }
        }

        private static void WritePalette(BinaryWriter writer)
        {
            for (int index = 0; index < 256; index++)
            {
                writer.Write((byte)(((index >> 5) & 7) * 255 / 7));
                writer.Write((byte)(((index >> 2) & 7) * 255 / 7));
                writer.Write((byte)((index & 3) * 255 / 3));
            }
        }

        private static void WriteLoopExtension(BinaryWriter writer)
        {
            writer.Write(new byte[] { 0x21, 0xFF, 0x0B });
            writer.Write(Encoding.ASCII.GetBytes("NETSCAPE2.0"));
            writer.Write(new byte[] { 0x03, 0x01, 0x00, 0x00, 0x00 });
        }

        private static void WriteFrame(BinaryWriter writer, Texture2D texture, int delay)
        {
            writer.Write(new byte[] { 0x21, 0xF9, 0x04, 0x00 });
            writer.Write((ushort)Math.Max(1, delay));
            writer.Write((byte)0);
            writer.Write((byte)0);
            writer.Write((byte)0x2C);
            writer.Write((ushort)0);
            writer.Write((ushort)0);
            writer.Write((ushort)texture.width);
            writer.Write((ushort)texture.height);
            writer.Write((byte)0);
            writer.Write((byte)8);
            byte[] compressed = Compress(ToPaletteIndices(texture));
            for (int offset = 0; offset < compressed.Length; offset += 255)
            {
                int count = Math.Min(255, compressed.Length - offset);
                writer.Write((byte)count);
                writer.Write(compressed, offset, count);
            }
            writer.Write((byte)0);
        }

        private static byte[] ToPaletteIndices(Texture2D texture)
        {
            Color32[] pixels = texture.GetPixels32();
            var result = new byte[pixels.Length];
            int target = 0;
            for (int y = texture.height - 1; y >= 0; y--)
            {
                int row = y * texture.width;
                for (int x = 0; x < texture.width; x++)
                {
                    Color32 color = pixels[row + x];
                    result[target++] = (byte)((color.r & 0xE0) | ((color.g & 0xE0) >> 3) | (color.b >> 6));
                }
            }
            return result;
        }

        private static byte[] Compress(byte[] indices)
        {
            const int clearCode = 256;
            const int endCode = 257;
            var output = new MemoryStream();
            int bitBuffer = 0;
            int bitCount = 0;
            int codeSize = 9;
            int nextCode = 258;
            var dictionary = new Dictionary<int, int>(4096);

            Action<int> writeCode = code =>
            {
                bitBuffer |= code << bitCount;
                bitCount += codeSize;
                while (bitCount >= 8)
                {
                    output.WriteByte((byte)(bitBuffer & 0xFF));
                    bitBuffer >>= 8;
                    bitCount -= 8;
                }
            };

            writeCode(clearCode);
            if (indices.Length > 0)
            {
                int prefix = indices[0];
                for (int i = 1; i < indices.Length; i++)
                {
                    int suffix = indices[i];
                    int key = (prefix << 8) | suffix;
                    int found;
                    if (dictionary.TryGetValue(key, out found))
                    {
                        prefix = found;
                        continue;
                    }
                    writeCode(prefix);
                    if (nextCode < 4096)
                    {
                        dictionary.Add(key, nextCode++);
                        if (nextCode == (1 << codeSize) && codeSize < 12) codeSize++;
                    }
                    else
                    {
                        writeCode(clearCode);
                        dictionary.Clear();
                        codeSize = 9;
                        nextCode = 258;
                    }
                    prefix = suffix;
                }
                writeCode(prefix);
            }
            writeCode(endCode);
            if (bitCount > 0) output.WriteByte((byte)(bitBuffer & 0xFF));
            return output.ToArray();
        }
    }
}
