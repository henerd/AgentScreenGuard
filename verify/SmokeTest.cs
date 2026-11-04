using System;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using Microsoft.Win32;

namespace AgentScreenGuard
{
    static class SmokeTest
    {
        static string Log = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "verification.log");
        [DllImport("user32.dll")] static extern bool GetWindowDisplayAffinity(IntPtr hwnd, out uint affinity);
        [STAThread]
        static void Main(string[] args)
        {
            Application.SetUnhandledExceptionMode(UnhandledExceptionMode.ThrowException);
            try { Run(args); }
            catch (Exception error) { File.AppendAllText(Log, "FAIL: " + error + "\r\n"); Environment.ExitCode = 1; }
        }

        static void Run(string[] args)
        {
            if (args.Length == 2 && args[0] == "--update-install")
            {
                string error = UpdateInstaller.Run(args[1]);
                if (error != null) { File.WriteAllText(Path.Combine(Path.GetDirectoryName(args[1]), "result.txt"), error); Environment.ExitCode = 1; }
                return;
            }
            if (args.Contains("--metadata-only")) { Log = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "metadata-verification.log"); CheckMetadata(); return; }
            if (args.Contains("--network-only"))
            {
                Log = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "network-verification.log");
                File.WriteAllText(Log, "");
                var release = Updates.Latest().GetAwaiter().GetResult();
                Record("공개 저장소 정식 버전 확인", release.Version == AppInfo.CurrentVersion && release.assets.Any(a => a.name == AppInfo.FileName));
                string plan = Updates.Prepare(release, Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "network-probe", AppInfo.FileName)).GetAwaiter().GetResult();
                Record("실제 배포 실행 파일 다운로드 및 검증", File.Exists(plan) && File.Exists(Path.Combine(Path.GetDirectoryName(plan), "payload.exe")));
                return;
            }
            if (args.Contains("--render")) { Render(); return; }
            if (args.Contains("--data-only")) { Log = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "data-verification.log"); CheckData(); return; }
            if (args.Contains("--input-only")) { Log = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "local-verification.log"); CheckLocalInput(); return; }
            if (args.Contains("--recovery-only")) { Log = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "recovery-verification.log"); CheckRecovery(); return; }
            if (args.Contains("--export-ui")) { Log = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "export-verification.log"); CheckExportUi(); return; }
            File.WriteAllText(Log, "");
            Native.SetProcessDPIAware();
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            var secret = new PinStore("943781");
            Record("PIN 검증", secret.Matches("943781") && !secret.Matches("943780"));
            using (var app = new GuardContext(secret, true))
            using (var fixture = new Form { Text = "Agent Screen Guard - verification", ClientSize = new Size(640, 300), StartPosition = FormStartPosition.CenterScreen })
            using (var timer = new Timer { Interval = 100 })
            {
                var text = new TextBox { Name = "RemoteText", Text = "원격 입력 확인", Location = new Point(30, 70), Size = new Size(580, 32), Font = new Font("맑은 고딕", 14) };
                var button = new Button { Text = "원격 클릭 확인", Location = new Point(30, 130), Size = new Size(220, 50) };
                fixture.Controls.Add(new Label { Text = "가림창 뒤에서 AI 캡처·입력·클릭을 확인합니다.", Location = new Point(30, 25), Size = new Size(580, 30) });
                fixture.Controls.Add(text);
                fixture.Controls.Add(button);
                button.Click += delegate { File.AppendAllText(Log, "원격 버튼 클릭: PASS\r\n"); button.Text = "원격 클릭 성공"; };
                text.TextChanged += delegate { if (text.Text.Contains("REMOTE_INPUT_OK")) File.AppendAllText(Log, "원격 텍스트 입력: PASS\r\n"); };
                fixture.Show();
                var started = DateTime.UtcNow;
                bool requested = false, inspected = false;
                timer.Tick += delegate
                {
                    double elapsed = (DateTime.UtcNow - started).TotalSeconds;
                    if (!requested && elapsed >= 15) { requested = true; app.RequestCover(); }
                    if (app.IsCovered && !inspected)
                    {
                        inspected = true;
                        var covers = Application.OpenForms.Cast<Form>().OfType<Curtain>().ToArray();
                        Record("모든 모니터 가림창", covers.Length == Screen.AllScreens.Select(s => s.Bounds).Distinct().Count() && covers.All(c => c.Visible));
                        Record("주 모니터에만 입력 화면", covers.Count(c => c.ShowsInterface) == 1 && covers.Single(c => c.ShowsInterface).Bounds == Screen.PrimaryScreen.Bounds);
                        Record("캡처 제외 속성", covers.All(c => { uint affinity; return GetWindowDisplayAffinity(c.Handle, out affinity) && affinity == 0x11; }));
                        File.AppendAllText(Log, "COVER_READY\r\n");
                    }
                    if (elapsed >= 65)
                    {
                        timer.Stop();
                        app.Uncover();
                        Record("자동 복구", !app.IsCovered);
                        fixture.Close();
                        app.ExitThread();
                    }
                };
                timer.Start();
                Application.Run(app);
            }
        }

        static void CheckLocalInput()
        {
            File.WriteAllText(Log, "");
            var pinBefore = File.Exists(PinStore.FilePath) ? File.ReadAllBytes(PinStore.FilePath) : null;
            Native.SetProcessDPIAware();
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            using (var fixture = new Form { Text = "로컬 입력 처리 검증", ShowInTaskbar = false, Size = new Size(1, 1) })
            using (var app = new GuardContext(new PinStore("9437"), true))
            using (var timer = new Timer { Interval = 100 })
            {
                fixture.CreateControl();
                var tray = (NotifyIcon)typeof(GuardContext).GetField("tray", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(app);
                Record("트레이 메뉴 네 항목과 버전 텍스트", tray.ContextMenuStrip.Items.Count == 4 && !tray.ContextMenuStrip.Items[0].Enabled && tray.ContextMenuStrip.Items[0].Text == "버전 " + AppInfo.Version && ((ToolStripMenuItem)tray.ContextMenuStrip.Items[1]).CheckOnClick);
                var started = DateTime.UtcNow;
                int stage = 0;
                timer.Tick += delegate
                {
                    double elapsed = (DateTime.UtcNow - started).TotalSeconds;
                    if (stage == 0) { app.RequestCover(); stage = 1; }
                    else if (stage == 1 && app.IsCovered)
                    {
                        var input = typeof(GuardContext).GetField("filter", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(app);
                        var data = Marshal.AllocHGlobal(32);
                        try
                        {
                            for (int i = 0; i < 32; i++) Marshal.WriteByte(data, i, 0);
                            var mouse = typeof(InputFilter).GetMethod("Mouse", BindingFlags.Instance | BindingFlags.NonPublic);
                            Record("현장 마우스 차단", (IntPtr)mouse.Invoke(input, new object[] { 0, new IntPtr(0x0201), data }) == new IntPtr(1));
                            Record("현장 키보드 차단", PhysicalKeys(input, "0000"));
                            stage = 2;
                        }
                        finally { Marshal.FreeHGlobal(data); }
                    }
                    else if (stage == 2 && elapsed > 2)
                    {
                        var covers = Application.OpenForms.Cast<Form>().OfType<Curtain>().ToArray();
                        Record("오류 PIN 잠금 유지", app.IsCovered && covers.Any(c => c.Status.Contains("PIN이 다릅니다")));
                        var input = typeof(GuardContext).GetField("filter", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(app);
                        PhysicalKeys(input, "", new[] { 112 });
                        stage = 5;
                    }
                    else if (stage == 5 && elapsed > 2.2)
                    {
                        Record("복구 화면 전환", Application.OpenForms.Cast<Form>().OfType<Curtain>().Where(c => c.ShowsInterface).All(c => c.View == CurtainView.Recovery));
                        var input = typeof(GuardContext).GetField("filter", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(app);
                        PhysicalKeys(input, "", new[] { 112 });
                        stage = 3;
                    }
                    else if (stage == 3 && elapsed > 4)
                    {
                        var input = typeof(GuardContext).GetField("filter", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(app);
                        Record("4자리 PIN 키 전달", PhysicalKeys(input, "9437"));
                        stage = 4;
                    }
                    else if (stage == 4 && elapsed > 6)
                    {
                        Record("정상 PIN 가림 해제", !app.IsCovered);
                        Record("운영 PIN 파일 보존", pinBefore == null ? !File.Exists(PinStore.FilePath) : File.Exists(PinStore.FilePath) && pinBefore.SequenceEqual(File.ReadAllBytes(PinStore.FilePath)));
                        timer.Stop();
                        app.ExitThread();
                    }
                    if (elapsed > 10) { app.Uncover(); timer.Stop(); app.ExitThread(); }
                };
                timer.Start();
                Application.Run(app);
            }
        }

        static bool PhysicalKeys(object input, string value, int[] suffix = null)
        {
            var hook = typeof(InputFilter).GetMethod("Keyboard", BindingFlags.Instance | BindingFlags.NonPublic);
            var data = Marshal.AllocHGlobal(32);
            bool blocked = true;
            try
            {
                for (int i = 0; i < 32; i++) Marshal.WriteByte(data, i, 0);
                foreach (int key in value.Replace("-", "").Select(c => (int)c).Concat(suffix ?? new[] { 13 }))
                {
                    Marshal.WriteInt32(data, key);
                    blocked &= (IntPtr)hook.Invoke(input, new object[] { 0, new IntPtr(0x0100), data }) == new IntPtr(1);
                    blocked &= (IntPtr)hook.Invoke(input, new object[] { 0, new IntPtr(0x0101), data }) == new IntPtr(1);
                }
                return blocked;
            }
            finally { Marshal.FreeHGlobal(data); }
        }

        static void CheckRecovery()
        {
            File.WriteAllText(Log, "");
            var secret = new PinStore("9437");
            string code = secret.CreateRecovery();
            Native.SetProcessDPIAware();
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            using (var fixture = new Form())
            using (var app = new GuardContext(secret, true))
            using (var timer = new Timer { Interval = 100 })
            {
                fixture.CreateControl();
                int stage = 0;
                var started = DateTime.UtcNow;
                timer.Tick += delegate
                {
                    if (stage == 0) { app.RequestCover(); stage = 1; }
                    else if (stage == 1 && app.IsCovered)
                    {
                        stage = 2;
                        var input = typeof(GuardContext).GetField("filter", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(app);
                        PhysicalKeys(input, "", new[] { 112 });
                        PhysicalKeys(input, code);
                    }
                    else if (stage == 2)
                    {
                        var reset = Application.OpenForms.Cast<Form>().OfType<PinDialog>().FirstOrDefault();
                        if (reset != null)
                        {
                            Record("복구 코드로 가림 해제", !app.IsCovered);
                            Record("기존 PIN 없이 새 PIN 설정 가능", reset.Controls.OfType<TextBox>().Count() == 2);
                            stage = 3;
                            reset.DialogResult = DialogResult.Cancel;
                        }
                    }
                    else if (stage == 3) { timer.Stop(); app.ExitThread(); }
                    if ((DateTime.UtcNow - started).TotalSeconds > 10) throw new Exception("복구 흐름 시간 초과");
                };
                timer.Start();
                Application.Run(app);
            }
        }

        static void CheckData()
        {
            File.WriteAllText(Log, "");
            string root = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "test-data");
            Directory.CreateDirectory(root);
            string credentials = Path.Combine(root, "pin.dat");
            string source = Path.Combine(root, "source.png");
            string saved = Path.Combine(root, "background.img");
            try
            {
                Record("PIN 길이 및 숫자 검증", PinStore.ValidPin("0123") && PinStore.ValidPin("123456789012") && !PinStore.ValidPin("123") && !PinStore.ValidPin("1234567890123") && !PinStore.ValidPin("abcd"));
                var legacy = new PinStore("943781");
                legacy.Save(credentials);
                var restored = PinStore.Load(credentials);
                Record("기존 PIN 파일 호환", restored.Matches("943781") && !restored.HasRecovery);
                string recovery = restored.CreateRecovery();
                restored.Save(credentials);
                restored = PinStore.Load(credentials);
                Record("복구 코드 암호화 보관 및 읽기", restored.MatchesRecovery(recovery) && restored.MatchesRecovery(recovery.ToLowerInvariant()) && !restored.MatchesRecovery("WRONG"));
                var changed = new PinStore("0123");
                changed.KeepRecovery(restored);
                changed.Save(credentials);
                restored = PinStore.Load(credentials);
                Record("4자리 PIN 변경 및 복구 코드 유지", restored.Matches("0123") && !restored.Matches("943781") && restored.MatchesRecovery(recovery));
                string replacement = restored.CreateRecovery();
                restored.Save(credentials);
                restored = PinStore.Load(credentials);
                Record("복구 코드 재발급 시 이전 코드 폐기", !restored.MatchesRecovery(recovery) && restored.MatchesRecovery(replacement));
                using (var image = new Bitmap(120, 80))
                {
                    using (var graphics = Graphics.FromImage(image)) graphics.Clear(Color.CornflowerBlue);
                    image.Save(source, System.Drawing.Imaging.ImageFormat.Png);
                }
                Appearance.SaveBackground(source, saved);
                File.Delete(source);
                using (var image = Appearance.LoadBackground(saved, false)) Record("배경 복사 보관 및 원본 삭제 후 읽기", image.Width == 120 && image.Height == 80 && image.GetPixel(10, 10).ToArgb() == Color.CornflowerBlue.ToArgb());
                Appearance.SaveBackground(null, saved);
                Record("기본 배경으로 복원", !File.Exists(saved));
                using (var picker = RecoveryExport.CreatePicker()) Record("저장 창 다운로드 경로 및 새 파일 허용", picker.InitialDirectory == RecoveryExport.DownloadsDirectory && !picker.CheckFileExists && picker.DefaultExt == "txt");
                RecoveryExport.Write(source, recovery);
                Record("복구 코드 새 파일 생성", File.Exists(source) && File.ReadAllText(source).Contains(recovery));
                string export = RecoveryExport.SaveToDownloads(recovery);
                try { Record("다운로드 폴더 저장 및 내용 확인", File.ReadAllText(export).Contains(recovery) && Path.GetDirectoryName(export) == RecoveryExport.DownloadsDirectory); }
                finally { File.Delete(export); }
                using (var curtain = new Curtain(new Rectangle(0, 0, 640, 360), false))
                using (var image = new Bitmap(640, 360))
                {
                    bool black = true;
                    foreach (CurtainView mode in Enum.GetValues(typeof(CurtainView)))
                    {
                        curtain.View = mode;
                        curtain.Status = "표시되면 안 되는 안내";
                        curtain.Dots = "●●●●";
                        curtain.DrawToBitmap(image, new Rectangle(Point.Empty, image.Size));
                        for (int y = 0; y < image.Height; y++)
                            for (int x = 0; x < image.Width; x++) black &= image.GetPixel(x, y).ToArgb() == Color.Black.ToArgb();
                    }
                    Record("보조 모니터 모든 화면이 완전한 검정", black && curtain.SubmitBounds.IsEmpty && curtain.RecoveryLinkBounds.IsEmpty);
                }
            }
            finally
            {
                foreach (string file in new[] { credentials, credentials + ".tmp", source, saved, saved + ".tmp" }) if (File.Exists(file)) File.Delete(file);
            }
        }

        static void Render()
        {
            Native.SetProcessDPIAware();
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            using (var visual = new LockVisual(Appearance.LoadBackground()))
            {
                foreach (CurtainView view in Enum.GetValues(typeof(CurtainView)))
                    using (var image = new Bitmap(1600, 900))
                    {
                        using (var graphics = Graphics.FromImage(image)) visual.Draw(graphics, image.Size, view, view == CurtainView.Clock ? "" : "●●●●", view == CurtainView.Recovery ? "보관한 복구 코드 20자리를 입력하세요." : "");
                        image.Save(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "preview-" + view + ".png"), System.Drawing.Imaging.ImageFormat.Png);
                    }
            }
            using (var curtain = new Curtain(new Rectangle(0, 0, 1600, 900), false))
            using (var image = new Bitmap(1600, 900))
            {
                curtain.DrawToBitmap(image, new Rectangle(Point.Empty, image.Size));
                image.Save(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "preview-Secondary.png"), System.Drawing.Imaging.ImageFormat.Png);
            }
            using (var setup = new PinDialog(null)) RenderForm(setup, "Setup");
            using (var settings = new SettingsDialog(() => { }, () => { }, () => { })) RenderForm(settings, "Settings");
        }

        static void RenderForm(Form form, string name)
        {
            form.StartPosition = FormStartPosition.Manual;
            form.Location = new Point(-32000, -32000);
            form.Show();
            Application.DoEvents();
            using (var image = new Bitmap(form.Width, form.Height))
            {
                form.DrawToBitmap(image, new Rectangle(Point.Empty, image.Size));
                image.Save(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "preview-" + name + ".png"), System.Drawing.Imaging.ImageFormat.Png);
            }
            form.Hide();
        }

        static void CheckMetadata()
        {
            File.WriteAllText(Log, "");
            Record("버전 비교 정규화", Updates.ParseVersion("v1.0.0") == AppInfo.CurrentVersion && Updates.ParseVersion("1.0.1") > AppInfo.CurrentVersion);
            Record("시험 배포 버전 거부", Rejected(() => Updates.ParseRelease("{\"tag_name\":\"v1.1.0\",\"prerelease\":true,\"assets\":[]}")) && Rejected(() => Updates.ParseVersion("v1.0.0-beta")));
            string executable = System.Reflection.Assembly.GetExecutingAssembly().Location;
            Updates.VerifyPayload(executable, Updates.Hash(executable), AppInfo.Version);
            Record("파일 해시 및 버전 검증", Rejected(() => Updates.VerifyPayload(executable, new string('0', 64), AppInfo.Version)) && Rejected(() => Updates.VerifyPayload(executable, Updates.Hash(executable), "9.9.9")));
            Record("아이콘 리소스 포함", AppInfo.Icon.Width == 32 && AppInfo.Icon.Height == 32);
            string keyPath = "Software\\AgentScreenGuard.Tests\\" + Guid.NewGuid().ToString("N");
            try
            {
                var startup = new StartupRegistration("C:\\Program Files\\Screen Guard\\AgentScreenGuard.exe", keyPath);
                Record("자동 실행 기본 해제", !startup.Enabled);
                startup.Enabled = true;
                Record("자동 실행 등록", startup.Enabled);
                startup.Enabled = false;
                Record("자동 실행 등록 해제", !startup.Enabled);
            }
            finally { Registry.CurrentUser.DeleteSubKeyTree(keyPath, false); }
        }

        static bool Rejected(Action action)
        {
            try { action(); return false; }
            catch (InvalidDataException) { return true; }
        }

        static void CheckExportUi()
        {
            File.WriteAllText(Log, "");
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            using (var form = new Form { Text = "Recovery file export verification", ClientSize = new Size(420, 170), StartPosition = FormStartPosition.CenterScreen })
            {
                var button = new Button { Text = "저장 창 열기", Location = new Point(30, 35), Size = new Size(350, 50) };
                var status = new Label { Text = "가상 코드로 새 파일 저장을 검증합니다.", Location = new Point(30, 105), Size = new Size(350, 45) };
                form.Controls.AddRange(new Control[] { button, status });
                button.Click += delegate
                {
                    using (var picker = RecoveryExport.CreatePicker())
                    {
                        picker.FileName = "화면잠금-저장검증-" + Guid.NewGuid().ToString("N").Substring(0, 8) + ".txt";
                        if (picker.ShowDialog(form) != DialogResult.OK) return;
                        if (File.Exists(picker.FileName)) throw new Exception("검증은 새 파일에만 수행합니다.");
                        const string dummy = "TEST0-TEST0-TEST0-TEST0";
                        try
                        {
                            RecoveryExport.Write(picker.FileName, dummy);
                            Record("실제 저장 창에서 새 파일 생성", File.ReadAllText(picker.FileName).Contains(dummy));
                            status.Text = "저장 검증 성공";
                            button.Text = "검증 완료";
                        }
                        finally { if (File.Exists(picker.FileName)) File.Delete(picker.FileName); }
                    }
                };
                Application.Run(form);
            }
        }

        static void Record(string name, bool passed)
        {
            File.AppendAllText(Log, name + ": " + (passed ? "PASS" : "FAIL") + "\r\n");
            if (!passed) throw new Exception(name + " 실패");
        }
    }
}
