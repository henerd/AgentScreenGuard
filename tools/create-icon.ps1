$ErrorActionPreference = 'Stop'
if ($PSEdition -eq 'Core') {
    & (Join-Path $env:WINDIR 'System32\WindowsPowerShell\v1.0\powershell.exe') -NoProfile -File $PSCommandPath
    if ($LASTEXITCODE -ne 0) { throw '아이콘 생성 실패' }
    return
}
Add-Type -AssemblyName System.Drawing
Add-Type -ReferencedAssemblies System.Drawing -TypeDefinition @'
using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;

public static class ScreenGuardIcon
{
    static GraphicsPath Rounded(float x, float y, float width, float height, float radius)
    {
        var path = new GraphicsPath();
        float d = radius * 2;
        path.AddArc(x, y, d, d, 180, 90);
        path.AddArc(x + width - d, y, d, d, 270, 90);
        path.AddArc(x + width - d, y + height - d, d, d, 0, 90);
        path.AddArc(x, y + height - d, d, d, 90, 90);
        path.CloseFigure();
        return path;
    }

    static Bitmap Render(int size)
    {
        var bitmap = new Bitmap(size, size, PixelFormat.Format32bppArgb);
        using (var g = Graphics.FromImage(bitmap))
        {
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.ScaleTransform(size / 256f, size / 256f);
            using (var outline = Rounded(9, 9, 238, 238, 53))
            using (var fill = new LinearGradientBrush(new Rectangle(9, 9, 238, 238), Color.FromArgb(21, 44, 79), Color.FromArgb(33, 115, 184), 55f))
            using (var edge = new Pen(Color.FromArgb(125, 154, 225, 255), 2))
            {
                g.FillPath(fill, outline);
                g.DrawPath(edge, outline);
            }
            using (var pen = new Pen(Color.FromArgb(238, 250, 255), 16) { StartCap = LineCap.Round, EndCap = LineCap.Round })
            {
                g.DrawArc(pen, 88, 55, 80, 87, 180, 180);
                g.DrawLine(pen, 88, 98, 88, 128);
                g.DrawLine(pen, 168, 98, 168, 128);
            }
            using (var shadow = Rounded(64, 124, 128, 96, 22))
            using (var fill = new SolidBrush(Color.FromArgb(55, 0, 0, 0))) g.FillPath(fill, shadow);
            using (var body = Rounded(64, 116, 128, 96, 22))
            using (var fill = new LinearGradientBrush(new Rectangle(64, 116, 128, 96), Color.FromArgb(247, 253, 255), Color.FromArgb(193, 229, 255), 90f)) g.FillPath(fill, body);
            using (var fill = new SolidBrush(Color.FromArgb(29, 78, 122)))
            {
                g.FillEllipse(fill, 118, 144, 20, 20);
                using (var stem = Rounded(123, 158, 10, 26, 4)) g.FillPath(fill, stem);
            }
        }
        return bitmap;
    }

    public static void Save(string folder)
    {
        Directory.CreateDirectory(folder);
        int[] sizes = { 16, 20, 24, 32, 48, 64, 128, 256 };
        var frames = new byte[sizes.Length][];
        for (int i = 0; i < sizes.Length; i++)
            using (var bitmap = Render(sizes[i]))
            using (var stream = new MemoryStream())
            {
                bitmap.Save(stream, ImageFormat.Png);
                frames[i] = stream.ToArray();
                if (sizes[i] == 256) bitmap.Save(Path.Combine(folder, "icon.png"), ImageFormat.Png);
            }
        using (var writer = new BinaryWriter(File.Create(Path.Combine(folder, "app.ico"))))
        {
            writer.Write((ushort)0); writer.Write((ushort)1); writer.Write((ushort)sizes.Length);
            int offset = 6 + 16 * sizes.Length;
            for (int i = 0; i < sizes.Length; i++)
            {
                writer.Write((byte)(sizes[i] == 256 ? 0 : sizes[i]));
                writer.Write((byte)(sizes[i] == 256 ? 0 : sizes[i]));
                writer.Write((byte)0); writer.Write((byte)0); writer.Write((ushort)1); writer.Write((ushort)32);
                writer.Write(frames[i].Length); writer.Write(offset); offset += frames[i].Length;
            }
            foreach (var frame in frames) writer.Write(frame);
        }
    }
}
'@
[ScreenGuardIcon]::Save((Join-Path $PSScriptRoot '..\assets'))
