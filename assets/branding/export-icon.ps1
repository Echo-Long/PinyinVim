param([string]$SourcePath = (Join-Path $PSScriptRoot 'pinyinvim-icon.png'))
$ErrorActionPreference = 'Stop'
# Use the Windows .NET Framework drawing implementation from either shell.
if ($PSVersionTable.PSEdition -eq 'Core') {
    $windowsPowerShell = Join-Path $env:WINDIR 'System32/WindowsPowerShell/v1.0/powershell.exe'
    & $windowsPowerShell -NoProfile -ExecutionPolicy Bypass -File $PSCommandPath -SourcePath $SourcePath
    if ($LASTEXITCODE -ne 0) { throw 'Icon export failed in Windows PowerShell.' }
    return
}
Add-Type -AssemblyName System.Drawing

# Only exports sizes/formats; the generated master image is left unchanged.
if (-not ('PinyinVimBranding.IconExport' -as [type])) {
    Add-Type -ReferencedAssemblies System.Drawing -TypeDefinition @'
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;

namespace PinyinVimBranding
{
    public static class IconExport
    {
        static Bitmap Resize(Image source, int size)
        {
            var bitmap = new Bitmap(size, size, PixelFormat.Format32bppArgb);
            using (var graphics = Graphics.FromImage(bitmap))
            using (var attributes = new ImageAttributes())
            {
                graphics.CompositingMode = CompositingMode.SourceCopy;
                graphics.CompositingQuality = CompositingQuality.HighQuality;
                graphics.InterpolationMode = InterpolationMode.HighQualityBicubic;
                graphics.PixelOffsetMode = PixelOffsetMode.HighQuality;
                attributes.SetWrapMode(WrapMode.TileFlipXY);
                graphics.Clear(Color.Transparent);
                graphics.DrawImage(source, new Rectangle(0, 0, size, size),
                    0, 0, source.Width, source.Height, GraphicsUnit.Pixel, attributes);
            }
            return bitmap;
        }

        static byte[] EncodeFrame(Bitmap bitmap, bool png)
        {
            using (var stream = new MemoryStream())
            {
                if (png) bitmap.Save(stream, ImageFormat.Png);
                else
                {
                    int size = bitmap.Width;
                    int maskStride = ((size + 31) / 32) * 4;
                    var writer = new BinaryWriter(stream);
                    writer.Write(40); writer.Write(size); writer.Write(size * 2);
                    writer.Write((ushort)1); writer.Write((ushort)32);
                    writer.Write(0); writer.Write(size * size * 4);
                    writer.Write(0); writer.Write(0); writer.Write(0); writer.Write(0);
                    for (int y = size - 1; y >= 0; y--)
                        for (int x = 0; x < size; x++)
                        {
                            Color pixel = bitmap.GetPixel(x, y);
                            writer.Write(pixel.B); writer.Write(pixel.G);
                            writer.Write(pixel.R); writer.Write(pixel.A);
                        }
                    for (int y = size - 1; y >= 0; y--)
                    {
                        var mask = new byte[maskStride];
                        for (int x = 0; x < size; x++)
                            if (bitmap.GetPixel(x, y).A == 0)
                                mask[x / 8] |= (byte)(0x80 >> (x % 8));
                        writer.Write(mask);
                    }
                    writer.Flush();
                }
                return stream.ToArray();
            }
        }

        public static void Export(string sourcePath, string directory)
        {
            int[] sizes = {16, 20, 24, 32, 40, 48, 64, 128, 256};
            var frames = new List<byte[]>();
            using (var source = Image.FromFile(sourcePath))
            {
                if (source.Width != source.Height) throw new InvalidDataException("Icon must be square.");
                foreach (int size in sizes)
                    using (var bitmap = Resize(source, size))
                    {
                        frames.Add(EncodeFrame(bitmap, size == 256));
                        if (size == 256) bitmap.Save(Path.Combine(directory, "pinyinvim-icon-256.png"), ImageFormat.Png);
                    }
                using (var bitmap = Resize(source, 512))
                    bitmap.Save(Path.Combine(directory, "pinyinvim-icon-512.png"), ImageFormat.Png);
                using (var stream = File.Create(Path.Combine(directory, "pinyinvim.ico")))
                using (var writer = new BinaryWriter(stream))
                {
                    writer.Write((ushort)0); writer.Write((ushort)1); writer.Write((ushort)sizes.Length);
                    int offset = 6 + sizes.Length * 16;
                    for (int i = 0; i < sizes.Length; i++)
                    {
                        byte dimension = sizes[i] == 256 ? (byte)0 : (byte)sizes[i];
                        writer.Write(dimension); writer.Write(dimension);
                        writer.Write((byte)0); writer.Write((byte)0);
                        writer.Write((ushort)1); writer.Write((ushort)32);
                        writer.Write(frames[i].Length); writer.Write(offset);
                        offset += frames[i].Length;
                    }
                    foreach (byte[] frame in frames) writer.Write(frame);
                }
                Preview(source, directory);
            }
        }

        static void Preview(Image source, string directory)
        {
            using (var preview = new Bitmap(1024, 768, PixelFormat.Format32bppArgb))
            using (var graphics = Graphics.FromImage(preview))
            using (var title = new Font("Segoe UI", 28, FontStyle.Bold, GraphicsUnit.Pixel))
            using (var text = new Font("Segoe UI", 18, FontStyle.Regular, GraphicsUnit.Pixel))
            using (var label = new Font("Segoe UI", 14, FontStyle.Regular, GraphicsUnit.Pixel))
            using (var ink = new SolidBrush(Color.FromArgb(37, 57, 78)))
            using (var muted = new SolidBrush(Color.FromArgb(111, 129, 148)))
            using (var dark = new SolidBrush(Color.FromArgb(20, 31, 48)))
            {
                graphics.Clear(Color.FromArgb(239, 244, 249));
                graphics.TextRenderingHint = System.Drawing.Text.TextRenderingHint.AntiAliasGridFit;
                graphics.FillRectangle(Brushes.White, 24, 24, 976, 208);
                using (var hero = Resize(source, 192)) graphics.DrawImageUnscaled(hero, 48, 32);
                graphics.DrawString("PinyinVim", title, ink, 280, 67);
                graphics.DrawString("A floating Vim editor for writing in Chinese.", text, muted, 280, 114);
                graphics.DrawString("Frosted note / V mark / mint cursor", label, muted, 280, 156);
                graphics.FillRectangle(Brushes.White, 24, 256, 976, 216);
                graphics.FillRectangle(dark, 24, 500, 976, 244);
                graphics.DrawString("Light background / actual pixel sizes", label, muted, 48, 273);
                graphics.DrawString("Dark background / actual pixel sizes", label, Brushes.LightSteelBlue, 48, 517);
                int[] sizes = {16, 20, 24, 32, 40, 48, 64, 128};
                for (int row = 0; row < 2; row++)
                    for (int i = 0; i < sizes.Length; i++)
                    {
                        int centerX = 84 + i * 121;
                        int centerY = row == 0 ? 364 : 615;
                        using (var bitmap = Resize(source, sizes[i]))
                            graphics.DrawImageUnscaled(bitmap, centerX - sizes[i] / 2, centerY - sizes[i] / 2);
                        string value = sizes[i] + " px";
                        SizeF extent = graphics.MeasureString(value, label);
                        graphics.DrawString(value, label, row == 0 ? muted : Brushes.LightSteelBlue,
                            centerX - extent.Width / 2, row == 0 ? 442 : 700);
                    }
                preview.Save(Path.Combine(directory, "pinyinvim-icon-preview.png"), ImageFormat.Png);
            }
        }
    }
}
'@
}
[PinyinVimBranding.IconExport]::Export([IO.Path]::GetFullPath($SourcePath), $PSScriptRoot)
Write-Output 'Exported transparent PNGs, 9-size Windows ICO and light/dark preview.'
