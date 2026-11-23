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
        [DllImport("user32.dll")] static extern bool IsWindowVisible(IntPtr hwnd);
        [DllImport("user32.dll")] static extern short GetAsyncKeyState(int key);
        [DllImport("user32.dll")] static extern IntPtr GetForegroundWindow();
        [DllImport("user32.dll")] static extern void keybd_event(byte key, byte scan, uint flags, UIntPtr extraInfo);
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
            if (args.Contains("--settings-only")) { Log = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "settings-verification.log"); CheckSettings(); return; }
            if (args.Contains("--data-only")) { Log = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "data-verification.log"); CheckData(); return; }
            if (args.Contains("--input-only")) { Log = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "local-verification.log"); CheckLocalInput(); return; }
            if (args.Contains("--release-only")) { Log = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "release-verification.log"); CheckInputRelease(); return; }
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
                var blinkStarted = DateTime.MinValue;
                bool caretOnSeen = false, caretOffSeen = false;
                int stage = 0;
                timer.Tick += delegate
                {
                    double elapsed = (DateTime.UtcNow - started).TotalSeconds;
                    if (stage == 0)
                    {
                        app.RequestCover();
                        Record("키 상태 대기 없이 즉시 잠금", app.IsCovered);
                        var covers = Application.OpenForms.Cast<Form>().OfType<Curtain>().ToArray();
                        app.RequestCover();
                        Record("잠금 중 재요청 시 가림창 유지", covers.SequenceEqual(Application.OpenForms.Cast<Form>().OfType<Curtain>()));
                        stage = 1;
                    }
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
                    else if (stage == 2 && Application.OpenForms.Cast<Form>().OfType<Curtain>().Any(c => c.Status.Contains("PIN이 다릅니다")))
                    {
                        var covers = Application.OpenForms.Cast<Form>().OfType<Curtain>().ToArray();
                        Record("오류 PIN 잠금 유지", app.IsCovered && covers.Any(c => c.Status.Contains("PIN이 다릅니다")));
                        Record("재시도 대기 중 입력 표시 비활성", covers.Where(c => c.ShowsInterface).All(c => !c.InputReady && !c.CaretVisible));
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
                    else if (stage == 3 && Application.OpenForms.Cast<Form>().OfType<Curtain>().Any(c => c.ShowsInterface && c.InputReady))
                    {
                        Record("재시도 대기 후 입력 표시 복원", Application.OpenForms.Cast<Form>().OfType<Curtain>().Where(c => c.ShowsInterface).All(c => c.InputReady));
                        blinkStarted = DateTime.UtcNow;
                        stage = 6;
                    }
                    else if (stage == 6)
                    {
                        var primary = Application.OpenForms.Cast<Form>().OfType<Curtain>().Single(c => c.ShowsInterface);
                        caretOnSeen |= primary.CaretVisible;
                        caretOffSeen |= !primary.CaretVisible;
                        if ((DateTime.UtcNow - blinkStarted).TotalSeconds < 1.2) return;
                        Record("입력 가능한 동안 커서 깜빡임", caretOnSeen && caretOffSeen && primary.InputReady);
                        var input = typeof(GuardContext).GetField("filter", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(app);
                        Record("4자리 PIN 키 전달", PhysicalKeys(input, "9437"));
                        blinkStarted = DateTime.UtcNow;
                        stage = 4;
                    }
                    else if (stage == 4 && !app.IsCovered)
                    {
                        Record("정상 PIN 가림 해제", !app.IsCovered);
                        Record("운영 PIN 파일 보존", pinBefore == null ? !File.Exists(PinStore.FilePath) : File.Exists(PinStore.FilePath) && pinBefore.SequenceEqual(File.ReadAllBytes(PinStore.FilePath)));
                        timer.Stop();
                        app.ExitThread();
                    }
                    if (elapsed > 30) { app.Uncover(); timer.Stop(); app.ExitThread(); Record("입력 검증 시간 내 완료", false); }
                };
                timer.Start();
                Application.Run(app);
            }
        }

        static void CheckInputRelease()
        {
            File.WriteAllText(Log, "");
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            byte[] heldKeys = { 0xA0, 0xA1, 0xA2, 0xA3, 0xA4, 0xA5, 0x87 };
            var marker = new UIntPtr(0x53475244);
            using (var fixture = new TrayDialog { Text = "Screen Guard · 키 해제 검증", ClientSize = new Size(460, 140) })
            using (var app = new GuardContext(new PinStore("9437"), true))
            using (var timer = new Timer { Interval = 200 })
            {
                fixture.Controls.Add(new Label { Text = "Ctrl · Alt · Shift 해제 후 Windows 키 상태를 검사합니다.", Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleCenter });
                IntPtr bridgeHandle = IntPtr.Zero;
                object filter = null;
                var keyboardCallback = typeof(InputFilter).GetMethod("Keyboard", BindingFlags.Instance | BindingFlags.NonPublic);
                // Only marked test events exercise the physical-input hook path.
                // Production still permits injected input for ongoing AI work.
                Native.HookProc bridge = delegate(int code, IntPtr message, IntPtr data)
                {
                    if (code < 0 || Marshal.ReadIntPtr(data, 16) != new IntPtr((long)marker.ToUInt64()))
                        return (IntPtr)keyboardCallback.Invoke(filter, new object[] { code, message, data });
                    int flags = Marshal.ReadInt32(data, 8);
                    Marshal.WriteInt32(data, 8, flags & ~0x12);
                    try { return (IntPtr)keyboardCallback.Invoke(filter, new object[] { code, message, data }); }
                    finally { Marshal.WriteInt32(data, 8, flags); }
                };
                int stage = 0;
                var started = DateTime.UtcNow;
                timer.Tick += delegate
                {
                    if ((DateTime.UtcNow - started).TotalSeconds > 60) throw new Exception("키 해제 검사 시간 초과");
                    if (stage == 0)
                    {
                        if (GetForegroundWindow() != fixture.Handle) return;
                        Record("검증 창 입력 포커스", GetForegroundWindow() == fixture.Handle);
                        foreach (byte key in heldKeys) keybd_event(key, 0, key == 0xA3 || key == 0xA5 ? 1u : 0u, marker);
                        stage = 1;
                    }
                    else if (stage == 1)
                    {
                        Record("좌우 Alt Ctrl Shift 및 일반 키 눌림 재현", heldKeys.All(key => GetAsyncKeyState(key) < 0));
                        app.RequestCover();
                        Record("키를 누른 채 즉시 잠금", app.IsCovered);
                        filter = typeof(GuardContext).GetField("filter", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(app);
                        var keyboardHandle = typeof(InputFilter).GetField("keyboard", BindingFlags.Instance | BindingFlags.NonPublic);
                        Record("실제 훅 콜백을 검증 스레드에 연결", Native.UnhookWindowsHookEx((IntPtr)keyboardHandle.GetValue(filter)));
                        keyboardHandle.SetValue(filter, IntPtr.Zero);
                        bridgeHandle = Native.SetWindowsHookEx(13, bridge, Native.GetModuleHandle(null), 0);
                        Record("검증 입력 경로 준비", bridgeHandle != IntPtr.Zero);
                        foreach (byte key in heldKeys.Reverse()) keybd_event(key, 0, (key == 0xA3 || key == 0xA5 ? 1u : 0u) | 2u, marker);
                        stage = 2;
                    }
                    else if (stage == 2)
                    {
                        Record("잠금 중 기존 키 해제로 Windows 눌림 상태 해제", heldKeys.All(key => GetAsyncKeyState(key) >= 0));
                        Record("기존 키 해제는 PIN 입력으로 처리하지 않음", Application.OpenForms.Cast<Form>().OfType<Curtain>().All(c => c.View == CurtainView.Clock));
                        keybd_event(0x87, 0, 0, marker);
                        keybd_event(0x87, 0, 2, marker);
                        stage = 3;
                    }
                    else if (stage == 3)
                    {
                        Record("잠금 후 새 현장 키 입력은 Windows로 전달 차단", GetAsyncKeyState(0x87) >= 0);
                        Record("차단한 현장 키를 PIN 화면에 전달", Application.OpenForms.Cast<Form>().OfType<Curtain>().Any(c => c.View == CurtainView.Pin));
                        keybd_event(0xA2, 0, 0, UIntPtr.Zero);
                        stage = 4;
                    }
                    else if (stage == 4)
                    {
                        Record("잠금 중 주입 입력 전달", GetAsyncKeyState(0xA2) < 0);
                        keybd_event(0xA2, 0, 2, UIntPtr.Zero);
                        stage = 5;
                    }
                    else
                    {
                        Native.UnhookWindowsHookEx(bridgeHandle);
                        bridgeHandle = IntPtr.Zero;
                        app.Uncover();
                        Record("잠금 해제 후 Alt Ctrl Shift 눌림 상태 없음", heldKeys.All(key => GetAsyncKeyState(key) >= 0));
                        timer.Stop();
                        app.ExitThread();
                    }
                };
                try
                {
                    fixture.Show();
                    fixture.Activate();
                    timer.Start();
                    Application.Run(app);
                }
                finally
                {
                    timer.Stop();
                    if (bridgeHandle != IntPtr.Zero) Native.UnhookWindowsHookEx(bridgeHandle);
                    foreach (byte key in heldKeys.Reverse()) keybd_event(key, 0, (key == 0xA3 || key == 0xA5 ? 1u : 0u) | 2u, UIntPtr.Zero);
                    app.Uncover();
                    GC.KeepAlive(bridge);
                }
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
                foreach (string sample in new[] { "Empty", "Full", "RecoveryFull", "BlinkOff", "Waiting" })
                    using (var image = new Bitmap(1600, 900))
                    {
                        bool ready = sample != "Waiting";
                        string dots = sample == "Empty" ? "" : new string('●', sample == "RecoveryFull" ? 20 : sample == "Full" ? 12 : 4);
                        using (var graphics = Graphics.FromImage(image))
                            visual.Draw(graphics, image.Size, sample == "RecoveryFull" ? CurtainView.Recovery : CurtainView.Pin, dots,
                                ready ? "PIN 입력 후 Enter를 누르세요." : "PIN이 다릅니다. 2초 후 다시 입력하세요.", ready, sample != "BlinkOff");
                        image.Save(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "preview-" + sample + ".png"), System.Drawing.Imaging.ImageFormat.Png);
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

        static void CheckSettings()
        {
            File.WriteAllText(Log, "");
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            using (var app = new GuardContext(new PinStore("9437"), true))
            using (var timer = new Timer { Interval = 200 })
            {
                int stage = 0;
                SettingsDialog settings = null;
                var configure = typeof(GuardContext).GetMethod("ConfigureSettings", BindingFlags.Instance | BindingFlags.NonPublic);
                timer.Tick += delegate
                {
                    if (stage == 0)
                    {
                        stage = 1;
                        configure.Invoke(app, null);
                        Record("설정의 지금 잠그기로 즉시 잠금", app.IsCovered);
                        app.Uncover();
                        var tray = (NotifyIcon)typeof(GuardContext).GetField("tray", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(app);
                        Record("설정 닫은 후 종료 메뉴 복원", tray.ContextMenuStrip.Items[3].Enabled);
                        timer.Stop();
                        app.ExitThread();
                    }
                    else if (stage == 1)
                    {
                        settings = Application.OpenForms.Cast<Form>().OfType<SettingsDialog>().Single();
                        Record("숨김 실행 후 설정 창 표시", settings.Visible && IsWindowVisible(settings.Handle));
                        configure.Invoke(app, null);
                        Record("설정 재요청 시 기존 창 유지", Application.OpenForms.Cast<Form>().OfType<SettingsDialog>().Single() == settings);
                        stage = 2;
                        typeof(GuardContext).GetMethod("ConfigureBackground", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(app, null);
                        Record("배경 창 닫은 후 설정 유지", settings.Visible && IsWindowVisible(settings.Handle));
                        var tray = (NotifyIcon)typeof(GuardContext).GetField("tray", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(app);
                        Record("중첩 창 닫아도 설정 중 종료 방지 유지", !tray.ContextMenuStrip.Items[3].Enabled);
                        settings.Controls.OfType<Button>().Single(button => button.Text == "지금 잠그기").PerformClick();
                    }
                    else if (stage == 2)
                    {
                        var background = Application.OpenForms.Cast<Form>().OfType<BackgroundDialog>().Single();
                        Record("배경 창 표시와 소유 창 연결", background.Visible && IsWindowVisible(background.Handle) && background.Owner == settings);
                        stage = 3;
                        background.Close();
                    }
                };
                timer.Start();
                Application.Run(app);
            }
        }

        static void CheckMetadata()
        {
            File.WriteAllText(Log, "");
            Record("버전 비교 정규화", Updates.ParseVersion("v" + AppInfo.Version) == AppInfo.CurrentVersion && Updates.ParseVersion("1.0.1") > Updates.ParseVersion("1.0.0"));
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
