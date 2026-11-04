using System;
using System.ComponentModel;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Text;
using System.Globalization;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using Microsoft.Win32;

namespace AgentScreenGuard
{
    enum CurtainView { Clock, Pin, Recovery }

    static class Appearance
    {
        internal static readonly string CustomPath = Path.Combine(Path.GetDirectoryName(PinStore.FilePath), "background.img");

        internal static Bitmap LoadBackground(string selected = null, bool useCustom = true)
        {
            string wallpaper = null;
            using (var key = Registry.CurrentUser.OpenSubKey("Control Panel\\Desktop"))
                if (key != null) wallpaper = key.GetValue("WallPaper") as string;
            string[] choices = { selected, useCustom ? CustomPath : null, wallpaper, Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "Web\\Wallpaper\\Windows\\img0.jpg") };
            foreach (string path in choices)
            {
                if (String.IsNullOrEmpty(path) || !File.Exists(path)) continue;
                try { using (var image = Image.FromFile(path)) return new Bitmap(image); }
                catch (ArgumentException) { }
                catch (IOException) { }
                catch (ExternalException) { }
                catch (UnauthorizedAccessException) { }
            }
            return null;
        }

        internal static void SaveBackground(string selected, string destination = null)
        {
            destination = destination ?? CustomPath;
            if (selected == null) { if (File.Exists(destination)) File.Delete(destination); return; }
            Directory.CreateDirectory(Path.GetDirectoryName(destination));
            string temporary = destination + ".tmp";
            File.Copy(selected, temporary, true);
            if (File.Exists(destination)) File.Replace(temporary, destination, null);
            else File.Move(temporary, destination);
        }
    }

    sealed class LockVisual : IDisposable
    {
        readonly Bitmap background, blurred;
        internal LockVisual(Bitmap image)
        {
            background = image;
            if (image != null)
            {
                int width = 64;
                int height = Math.Max(1, (int)(64.0 * image.Height / image.Width));
                blurred = new Bitmap(width, height);
                using (var graphics = Graphics.FromImage(blurred))
                {
                    graphics.InterpolationMode = InterpolationMode.HighQualityBilinear;
                    graphics.DrawImage(image, new Rectangle(0, 0, width, height));
                }
            }
        }

        internal static float Scale(Size size) { return Math.Max(0.1f, Math.Min(size.Width / 1600f, size.Height / 900f)); }
        internal static Rectangle FieldBounds(Size size)
        {
            float scale = Scale(size);
            return Rectangle.Round(new RectangleF(size.Width / 2f - 180 * scale, size.Height / 2f + 8 * scale, 360 * scale, 46 * scale));
        }
        internal static Rectangle RecoveryBounds(Size size)
        {
            var field = FieldBounds(size);
            float scale = Scale(size);
            return Rectangle.Round(new RectangleF(field.X, field.Bottom + 12 * scale, field.Width, 36 * scale));
        }

        internal void Draw(Graphics graphics, Size size, CurtainView view, string dots, string status)
        {
            var state = graphics.Save();
            graphics.SmoothingMode = SmoothingMode.AntiAlias;
            graphics.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;
            graphics.InterpolationMode = InterpolationMode.HighQualityBicubic;
            var full = new Rectangle(Point.Empty, size);
            if (background != null)
            {
                var image = view == CurtainView.Clock ? background : blurred;
                float cover = Math.Max(size.Width / (float)image.Width, size.Height / (float)image.Height);
                graphics.DrawImage(image, new RectangleF((size.Width - image.Width * cover) / 2, (size.Height - image.Height * cover) / 2, image.Width * cover, image.Height * cover));
            }
            else
            {
                using (var gradient = new LinearGradientBrush(full, Color.FromArgb(34, 92, 171), Color.FromArgb(8, 18, 46), 40f)) graphics.FillRectangle(gradient, full);
            }
            using (var shade = new SolidBrush(Color.FromArgb(view == CurtainView.Clock ? 22 : 100, Color.Black))) graphics.FillRectangle(shade, full);
            using (var gradient = new LinearGradientBrush(full, Color.Transparent, Color.FromArgb(100, Color.Black), 90f)) graphics.FillRectangle(gradient, full);
            float scale = Scale(size);
            graphics.ScaleTransform(scale, scale);
            float width = size.Width / scale, height = size.Height / scale;
            if (view == CurtainView.Clock)
            {
                Text(graphics, DateTime.Now.ToString("H:mm"), "Segoe UI Light", 110, new RectangleF(0, height * .12f, width, 142), Color.White);
                Text(graphics, DateTime.Now.ToString("M월 d일 dddd", CultureInfo.GetCultureInfo("ko-KR")), "맑은 고딕", 24, new RectangleF(0, height * .12f + 138, width, 46), Color.White);
                Text(graphics, "화면을 클릭하거나 아무 키나 누르세요", "맑은 고딕", 15, new RectangleF(0, height - 108, width, 32), Color.FromArgb(235, Color.White));
            }
            else
            {
                float cx = width / 2, cy = height / 2 - 145;
                using (var fill = new SolidBrush(Color.FromArgb(46, Color.White))) graphics.FillEllipse(fill, cx - 56, cy - 56, 112, 112);
                using (var fill = new SolidBrush(Color.FromArgb(235, Color.White)))
                {
                    graphics.FillEllipse(fill, cx - 17, cy - 31, 34, 34);
                    using (var path = new GraphicsPath())
                    {
                        path.AddArc(cx - 35, cy + 4, 70, 63, 180, 180);
                        path.CloseFigure();
                        graphics.FillPath(fill, path);
                    }
                }
                Text(graphics, Environment.UserName, "맑은 고딕", 30, new RectangleF(0, height / 2 - 70, width, 48), Color.White);
                var field = new RectangleF(cx - 180, height / 2 + 8, 360, 46);
                using (var fill = new SolidBrush(Color.FromArgb(245, 247, 248))) graphics.FillRectangle(fill, field);
                using (var line = new Pen(Color.FromArgb(180, Color.White))) graphics.DrawRectangle(line, field.X, field.Y, field.Width, field.Height);
                using (var line = new Pen(Color.FromArgb(90, 183, 255), 2)) graphics.DrawLine(line, field.X, field.Bottom, field.Right, field.Bottom);
                string placeholder = view == CurtainView.Recovery ? "복구 코드" : "PIN";
                Text(graphics, dots.Length == 0 ? placeholder : dots, "Segoe UI", dots.Length == 0 ? 18 : 15, new RectangleF(field.X + 15, field.Y, field.Width - 65, field.Height), Color.FromArgb(70, 70, 70), false);
                Text(graphics, "→", "Segoe UI", 27, new RectangleF(field.Right - 45, field.Y - 2, 45, field.Height), Color.FromArgb(65, 65, 65));
                Text(graphics, view == CurtainView.Recovery ? "PIN으로 돌아가기" : "PIN을 잊으셨나요?", "맑은 고딕", 15, new RectangleF(field.X, field.Bottom + 12, field.Width, 36), Color.White);
                Text(graphics, status, "맑은 고딕", 14, new RectangleF(cx - 400, field.Bottom + 62, 800, 60), Color.FromArgb(245, Color.White));
            }
            graphics.Restore(state);
        }

        static void Text(Graphics graphics, string text, string family, float size, RectangleF bounds, Color color, bool center = true)
        {
            using (var font = new Font(family, size, FontStyle.Regular, GraphicsUnit.Pixel))
            using (var brush = new SolidBrush(color))
            using (var format = new StringFormat { Alignment = center ? StringAlignment.Center : StringAlignment.Near, LineAlignment = StringAlignment.Center })
                graphics.DrawString(text, font, brush, bounds, format);
        }

        public void Dispose()
        {
            if (background != null) background.Dispose();
            if (blurred != null) blurred.Dispose();
        }
    }

    sealed class Curtain : Form
    {
        internal string Status = "", Dots = "", Minute = "";
        internal CurtainView View;
        internal readonly bool ShowsInterface;
        readonly LockVisual visual;
        internal Rectangle RecoveryLinkBounds { get { return ShowsInterface ? LockVisual.RecoveryBounds(ClientSize) : Rectangle.Empty; } }
        internal Rectangle SubmitBounds
        {
            get { if (!ShowsInterface) return Rectangle.Empty; var field = LockVisual.FieldBounds(ClientSize); int width = (int)(45 * LockVisual.Scale(ClientSize)); return new Rectangle(field.Right - width, field.Y, width, field.Height); }
        }

        internal Curtain(Rectangle bounds, bool showsInterface)
        {
            Text = "화면 잠금";
            FormBorderStyle = FormBorderStyle.None;
            ShowInTaskbar = false;
            StartPosition = FormStartPosition.Manual;
            Bounds = bounds;
            BackColor = Color.Black;
            TopMost = true;
            DoubleBuffered = true;
            ShowsInterface = showsInterface;
            if (showsInterface) visual = new LockVisual(Appearance.LoadBackground());
        }

        protected override bool ShowWithoutActivation { get { return true; } }
        protected override CreateParams CreateParams
        {
            get { var value = base.CreateParams; value.ExStyle |= 0x00080000 | 0x00000020 | 0x08000000 | 0x00000080; return value; }
        }
        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            if (!Native.SetLayeredWindowAttributes(Handle, 0, 255, 2) || !Native.SetWindowDisplayAffinity(Handle, 0x11))
                throw new Win32Exception(Marshal.GetLastWin32Error(), "화면 캡처 설정을 적용하지 못했습니다.");
        }
        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            if (ShowsInterface) visual.Draw(e.Graphics, ClientSize, View, Dots, Status);
            else e.Graphics.Clear(Color.Black);
        }
        protected override void Dispose(bool disposing) { if (disposing && visual != null) visual.Dispose(); base.Dispose(disposing); }
    }

    sealed class BackgroundDialog : Form
    {
        LockVisual preview;
        string selected;
        readonly Panel canvas;
        readonly Label pathLabel;

        internal BackgroundDialog()
        {
            Text = "화면 잠금 · 배경 설정";
            Icon = AppInfo.Icon;
            ClientSize = new Size(720, 510);
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = MinimizeBox = false;
            StartPosition = FormStartPosition.CenterScreen;
            BackColor = Color.FromArgb(247, 248, 250);
            Font = new Font("맑은 고딕", 10);
            selected = File.Exists(Appearance.CustomPath) ? Appearance.CustomPath : null;
            preview = new LockVisual(Appearance.LoadBackground());
            Controls.Add(new Label { Text = "잠금 화면 개인 설정", Location = new Point(24, 20), Size = new Size(500, 42), Font = new Font("맑은 고딕", 17) });
            canvas = new Panel { Location = new Point(24, 76), Size = new Size(672, 300), BackColor = Color.Black };
            canvas.Paint += delegate(object sender, PaintEventArgs e) { preview.Draw(e.Graphics, canvas.ClientSize, CurtainView.Clock, "", ""); };
            Controls.Add(canvas);
            pathLabel = new Label { Text = selected == null ? "현재 Windows 배경 사용" : "사용자 지정 이미지", Location = new Point(24, 388), Size = new Size(672, 28), AutoEllipsis = true };
            Controls.Add(pathLabel);
            var browse = new Button { Text = "사진 찾아보기", Location = new Point(24, 438), Size = new Size(130, 36) };
            var reset = new Button { Text = "Windows 배경 사용", Location = new Point(164, 438), Size = new Size(160, 36) };
            var save = new Button { Text = "저장", Location = new Point(510, 438), Size = new Size(86, 36) };
            var cancel = new Button { Text = "취소", DialogResult = DialogResult.Cancel, Location = new Point(606, 438), Size = new Size(90, 36) };
            Controls.AddRange(new Control[] { browse, reset, save, cancel });
            browse.Click += delegate
            {
                using (var picker = new OpenFileDialog { Title = "잠금 화면 배경 선택", Filter = "배경 이미지|*.jpg;*.jpeg;*.png;*.bmp;*.gif|모든 파일|*.*" })
                {
                    if (picker.ShowDialog() != DialogResult.OK) return;
                    try
                    {
                        Bitmap image;
                        using (var loaded = Image.FromFile(picker.FileName)) image = new Bitmap(loaded);
                        ChangePreview(image);
                        selected = picker.FileName;
                        pathLabel.Text = selected;
                    }
                    catch (Exception error) { MessageBox.Show(error.Message, "이미지를 열 수 없습니다."); }
                }
            };
            reset.Click += delegate
            {
                selected = null;
                ChangePreview(Appearance.LoadBackground(null, false));
                pathLabel.Text = "현재 Windows 배경 사용";
            };
            save.Click += delegate
            {
                try { Appearance.SaveBackground(selected); DialogResult = DialogResult.OK; }
                catch (Exception error) { MessageBox.Show(error.Message, "배경 저장 실패"); }
            };
            AcceptButton = save;
            CancelButton = cancel;
        }

        void ChangePreview(Bitmap image) { preview.Dispose(); preview = new LockVisual(image); canvas.Invalidate(); }
        protected override void Dispose(bool disposing) { if (disposing && preview != null) preview.Dispose(); base.Dispose(disposing); }
    }
}
