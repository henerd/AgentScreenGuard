using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Drawing;
using System.IO;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using Microsoft.Win32;

namespace AgentScreenGuard
{
    static class Program
    {
        [STAThread]
        static void Main(string[] args)
        {
            if (args.Length == 2 && args[0] == "--apply-update")
            {
                string error = UpdateInstaller.Run(args[1]);
                if (error != null) { MessageBox.Show(error, "업데이트", MessageBoxButtons.OK, MessageBoxIcon.Error); Environment.ExitCode = 1; }
                return;
            }
            bool created;
            using (var mutex = new Mutex(true, "Local\\AgentScreenGuard", out created))
            {
                if (!created)
                {
                    MessageBox.Show("이미 실행 중입니다. 트레이 아이콘 또는 Ctrl+Alt+L을 사용하세요.", "화면 잠금");
                    return;
                }
                Native.SetProcessDPIAware();
                Application.EnableVisualStyles();
                Application.SetCompatibleTextRenderingDefault(false);
                try
                {
                    using (var app = new GuardContext())
                    {
                        if (args.Length == 2 && args[0] == "--updated" && Updates.IsStagingPath(args[1]) && Path.GetFileName(args[1]) == "ready")
                            File.WriteAllText(args[1], AppInfo.Version);
                        Application.Run(app);
                    }
                }
                catch (Exception error) { MessageBox.Show(error.Message, "화면 잠금 오류", MessageBoxButtons.OK, MessageBoxIcon.Error); }
            }
        }
    }

    sealed class PinStore
    {
        const int Iterations = 210000;
        readonly byte[] salt;
        readonly byte[] hash;
        PinStore recovery;
        internal bool HasRecovery { get { return recovery != null; } }
        internal static readonly string FilePath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "AgentScreenGuard", "pin.dat");

        internal PinStore(string pin)
        {
            salt = new byte[16];
            using (var random = RandomNumberGenerator.Create()) random.GetBytes(salt);
            hash = Derive(pin);
        }

        PinStore(byte[] data)
        {
            if (data.Length != 48 && data.Length != 96) throw new InvalidDataException("PIN 설정 파일이 손상되었습니다.");
            salt = new byte[16];
            hash = new byte[32];
            Buffer.BlockCopy(data, 0, salt, 0, 16);
            Buffer.BlockCopy(data, 16, hash, 0, 32);
            if (data.Length == 96)
            {
                var extra = new byte[48];
                Buffer.BlockCopy(data, 48, extra, 0, 48);
                recovery = new PinStore(extra);
            }
        }

        byte[] Derive(string pin)
        {
            using (var derive = new Rfc2898DeriveBytes(pin, salt, Iterations, HashAlgorithmName.SHA256)) return derive.GetBytes(32);
        }

        internal bool Matches(string pin)
        {
            var actual = Derive(pin);
            int difference = 0;
            for (int i = 0; i < hash.Length; i++) difference |= hash[i] ^ actual[i];
            Array.Clear(actual, 0, actual.Length);
            return difference == 0;
        }

        internal static bool ValidPin(string value)
        {
            return value.Length >= 4 && value.Length <= 12 && !Array.Exists(value.ToCharArray(), c => c < '0' || c > '9');
        }

        internal bool MatchesRecovery(string value)
        {
            return recovery != null && recovery.Matches(value.Replace("-", "").Replace(" ", "").ToUpperInvariant());
        }

        internal void KeepRecovery(PinStore previous) { recovery = previous.recovery; }

        internal PinStore Copy() { return new PinStore(Encode()); }

        internal string CreateRecovery()
        {
            const string alphabet = "0123456789ABCDEFGHJKMNPQRSTVWXYZ";
            var bytes = new byte[20];
            using (var random = RandomNumberGenerator.Create()) random.GetBytes(bytes);
            var code = new StringBuilder();
            for (int i = 0; i < bytes.Length; i++)
            {
                if (i > 0 && i % 5 == 0) code.Append('-');
                code.Append(alphabet[bytes[i] % alphabet.Length]);
            }
            string result = code.ToString();
            recovery = new PinStore(result.Replace("-", ""));
            return result;
        }

        byte[] Encode()
        {
            var data = new byte[HasRecovery ? 96 : 48];
            Buffer.BlockCopy(salt, 0, data, 0, 16);
            Buffer.BlockCopy(hash, 0, data, 16, 32);
            if (HasRecovery)
            {
                Buffer.BlockCopy(recovery.salt, 0, data, 48, 16);
                Buffer.BlockCopy(recovery.hash, 0, data, 64, 32);
            }
            return data;
        }

        internal static PinStore Load(string path = null)
        {
            path = path ?? FilePath;
            if (!File.Exists(path)) return null;
            var data = ProtectedData.Unprotect(File.ReadAllBytes(path), null, DataProtectionScope.CurrentUser);
            try { return new PinStore(data); }
            finally { Array.Clear(data, 0, data.Length); }
        }

        internal void Save(string path = null)
        {
            path = path ?? FilePath;
            var data = Encode();
            var encrypted = ProtectedData.Protect(data, null, DataProtectionScope.CurrentUser);
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            var temporary = path + ".tmp";
            File.WriteAllBytes(temporary, encrypted);
            if (File.Exists(path)) File.Replace(temporary, path, null);
            else File.Move(temporary, path);
            Array.Clear(data, 0, data.Length);
        }
    }

    sealed class PinDialog : TrayDialog
    {
        internal PinStore Result;
        internal string RecoveryCode;

        internal PinDialog(PinStore previous, bool recovered = false)
        {
            bool needCurrent = previous != null && !recovered;
            Text = previous == null ? "화면 잠금 · PIN 설정" : "화면 잠금 · PIN 변경";
            Icon = AppInfo.Icon;
            ClientSize = new Size(420, needCurrent ? 435 : 390);
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = MinimizeBox = false;
            StartPosition = FormStartPosition.CenterScreen;
            Font = new Font("맑은 고딕", 10);
            Controls.Add(new Label { Text = "화면 가리기를 해제할 숫자 PIN 4~12자리\nWindows 로그인 암호와 별도로 설정합니다.", Location = new Point(22, 18), Size = new Size(376, 48) });
            Controls.Add(new Label { Text = AppInfo.Usage, Location = new Point(22, 82), Size = new Size(376, 112), Font = new Font("맑은 고딕", 9) });
            int y = 207;
            TextBox current = needCurrent ? AddInput("현재 PIN", ref y) : null;
            var first = AddInput("새 PIN", ref y);
            var second = AddInput("PIN 확인", ref y);
            var message = new Label { Location = new Point(22, y), Size = new Size(376, 30), ForeColor = Color.Firebrick };
            Controls.Add(message);
            var save = new Button { Text = "저장", Location = new Point(222, y + 38), Size = new Size(82, 32) };
            var cancel = new Button { Text = "취소", DialogResult = DialogResult.Cancel, Location = new Point(314, y + 38), Size = new Size(82, 32) };
            Controls.Add(save);
            Controls.Add(cancel);
            AcceptButton = save;
            CancelButton = cancel;
            save.Click += async delegate
            {
                var pin = first.Text;
                if (!PinStore.ValidPin(pin))
                { message.Text = "숫자 4~12자리를 입력하세요."; return; }
                if (pin != second.Text) { message.Text = "PIN 확인이 일치하지 않습니다."; return; }
                save.Enabled = false;
                cancel.Enabled = false;
                ControlBox = false;
                try
                {
                    var oldPin = current == null ? null : current.Text;
                    if (needCurrent && !await Task.Run(() => previous.Matches(oldPin)))
                    { message.Text = "현재 PIN이 올바르지 않습니다."; return; }
                    var next = await Task.Run(() => new PinStore(pin));
                    if (previous != null && previous.HasRecovery && !recovered) next.KeepRecovery(previous);
                    else RecoveryCode = await Task.Run(() => next.CreateRecovery());
                    next.Save();
                    Result = next;
                    DialogResult = DialogResult.OK;
                }
                catch (Exception error) { message.Text = error.Message; }
                finally
                {
                    if (!IsDisposed) { save.Enabled = cancel.Enabled = true; ControlBox = true; }
                }
            };
        }

        TextBox AddInput(string label, ref int y)
        {
            Controls.Add(new Label { Text = label, Location = new Point(22, y + 3), Size = new Size(100, 28) });
            var box = new TextBox { Location = new Point(128, y), Size = new Size(268, 28), MaxLength = 12, UseSystemPasswordChar = true };
            Controls.Add(box);
            y += 45;
            return box;
        }
    }

    sealed class CommandWindow : NativeWindow, IDisposable
    {
        internal Action<int> Key;
        internal Action Lock;
        internal Action<Point> Click;
        readonly Queue<Action> pending = new Queue<Action>();
        internal CommandWindow() { CreateHandle(new CreateParams { Caption = "AgentScreenGuard.Commands", Parent = new IntPtr(-3) }); }
        internal void Enqueue(Action action)
        {
            lock (pending) pending.Enqueue(action);
            Native.PostMessage(Handle, 0x8002, IntPtr.Zero, IntPtr.Zero);
        }
        protected override void WndProc(ref Message message)
        {
            if (message.Msg == 0x8001 && Key != null) Key(message.WParam.ToInt32());
            if (message.Msg == 0x0312 && Lock != null) Lock();
            if (message.Msg == 0x8003 && Click != null) Click(new Point(message.WParam.ToInt32(), message.LParam.ToInt32()));
            if (message.Msg == 0x8002)
            {
                Action action;
                lock (pending) action = pending.Count == 0 ? null : pending.Dequeue();
                if (action != null) action();
            }
            base.WndProc(ref message);
        }
        public void Dispose() { DestroyHandle(); }
    }

    sealed class InputFilter : IDisposable
    {
        readonly Thread thread;
        readonly ManualResetEvent ready = new ManualResetEvent(false);
        readonly IntPtr target;
        readonly HashSet<int> pressed = new HashSet<int>();
        Native.HookProc keyboardProc, mouseProc;
        IntPtr keyboard, mouse;
        uint threadId;
        Exception failure;

        internal InputFilter(IntPtr target)
        {
            this.target = target;
            thread = new Thread(Run) { IsBackground = true, Name = "현장 입력 차단" };
            thread.Start();
            ready.WaitOne();
            if (failure != null) { thread.Join(); ready.Dispose(); throw failure; }
        }

        void Run()
        {
            try
            {
                threadId = Native.GetCurrentThreadId();
                Native.Message message;
                Native.PeekMessage(out message, IntPtr.Zero, 0, 0, 0);
                keyboardProc = Keyboard;
                mouseProc = Mouse;
                var module = Native.GetModuleHandle(null);
                keyboard = Native.SetWindowsHookEx(13, keyboardProc, module, 0);
                mouse = Native.SetWindowsHookEx(14, mouseProc, module, 0);
                if (keyboard == IntPtr.Zero || mouse == IntPtr.Zero) throw new Win32Exception(Marshal.GetLastWin32Error(), "현장 입력 차단을 시작하지 못했습니다.");
                ready.Set();
                while (Native.GetMessage(out message, IntPtr.Zero, 0, 0) > 0)
                { Native.TranslateMessage(ref message); Native.DispatchMessage(ref message); }
            }
            catch (Exception error) { failure = error; }
            finally
            {
                if (keyboard != IntPtr.Zero) Native.UnhookWindowsHookEx(keyboard);
                if (mouse != IntPtr.Zero) Native.UnhookWindowsHookEx(mouse);
                ready.Set();
            }
        }

        IntPtr Keyboard(int code, IntPtr wParam, IntPtr lParam)
        {
            if (code < 0 || (Marshal.ReadInt32(lParam, 8) & 0x10) != 0) return Native.CallNextHookEx(keyboard, code, wParam, lParam);
            int key = Marshal.ReadInt32(lParam);
            int message = wParam.ToInt32();
            if (message == 0x0100 || message == 0x0104)
            {
                if (pressed.Add(key) && key != 13) Native.PostMessage(target, 0x8001, new IntPtr(key), IntPtr.Zero);
            }
            else if (pressed.Remove(key) && key == 13) Native.PostMessage(target, 0x8001, new IntPtr(key), IntPtr.Zero);
            return new IntPtr(1);
        }

        IntPtr Mouse(int code, IntPtr wParam, IntPtr lParam)
        {
            if (code < 0 || (Marshal.ReadInt32(lParam, 12) & 1) != 0) return Native.CallNextHookEx(mouse, code, wParam, lParam);
            if (wParam.ToInt32() == 0x0200) return Native.CallNextHookEx(mouse, code, wParam, lParam);
            if (wParam.ToInt32() == 0x0202)
                Native.PostMessage(target, 0x8003, new IntPtr(Marshal.ReadInt32(lParam)), new IntPtr(Marshal.ReadInt32(lParam, 4)));
            return new IntPtr(1);
        }

        public void Dispose()
        {
            Native.PostThreadMessage(threadId, 0x0012, IntPtr.Zero, IntPtr.Zero);
            thread.Join();
            ready.Dispose();
        }
    }

    sealed class GuardContext : ApplicationContext
    {
        readonly CommandWindow commands = new CommandWindow();
        readonly NotifyIcon tray;
        readonly System.Windows.Forms.Timer timer = new System.Windows.Forms.Timer { Interval = 100 };
        readonly List<Curtain> curtains = new List<Curtain>();
        readonly StringBuilder digits = new StringBuilder();
        readonly ToolStripMenuItem versionItem, startupItem, updateItem, exitItem;
        readonly StartupRegistration startup = new StartupRegistration(Application.ExecutablePath);
        PinStore pin;
        InputFilter filter;
        Form activeDialog;
        bool verifying, disposed, settingsOpen, updating;
        DateTime retryAfter;
        DateTime caretResetUtc = DateTime.UtcNow;
        int failures;
        CurtainView view;
        string failureMessage = "";
        internal bool IsCovered { get { return filter != null; } }

        internal GuardContext() : this(PinStore.Load(), false) { }

        internal GuardContext(PinStore initialPin, bool test)
        {
            pin = initialPin;
            commands.Key = OnPhysicalKey;
            commands.Lock = RequestCover;
            commands.Click = OnPhysicalClick;
            var menu = new ContextMenuStrip();
            versionItem = new ToolStripMenuItem("버전 " + AppInfo.Version) { Enabled = false };
            startupItem = new ToolStripMenuItem("윈도우 시작 시 자동 실행") { CheckOnClick = true, Checked = !test && startup.Enabled };
            startupItem.Click += delegate
            {
                try { startup.Enabled = startupItem.Checked; }
                catch (Exception error) { startupItem.Checked = startup.Enabled; MessageBox.Show(error.Message, "자동 실행 설정 실패"); }
            };
            updateItem = new ToolStripMenuItem("업데이트 체크", null, delegate { CheckUpdates(); });
            exitItem = new ToolStripMenuItem("종료", null, delegate { ExitThread(); });
            menu.Items.AddRange(new ToolStripItem[] { versionItem, startupItem, updateItem, exitItem });
            tray = new NotifyIcon { Icon = AppInfo.Icon, Text = "화면 잠금 · 두 번 클릭: 설정 · Ctrl+Alt+L", Visible = true, ContextMenuStrip = menu };
            tray.DoubleClick += delegate { ConfigureSettings(); };
            timer.Tick += Tick;
            timer.Start();
            SystemEvents.DisplaySettingsChanged += DisplaysChanged;
            if (!test)
            {
                if (!Native.RegisterHotKey(commands.Handle, 1, 0x4003, (uint)Keys.L))
                    tray.ShowBalloonTip(5000, "단축키 등록 실패", "트레이 아이콘을 두 번 누르고 ‘지금 잠그기’를 선택하세요.", ToolTipIcon.Warning);
                commands.Enqueue(delegate
                {
                    if (pin == null) ConfigurePin();
                    else if (!pin.HasRecovery) ConfigureRecovery();
                });
            }
        }

        void ConfigurePin(bool recovered = false)
        {
            if (IsCovered) return;
            SetMenuEnabled(false);
            try
            {
                using (var dialog = new PinDialog(pin, recovered))
                    if (ShowDialog(dialog) == DialogResult.OK)
                    {
                        pin = dialog.Result;
                        if (dialog.RecoveryCode != null)
                            using (var recovery = new RecoveryDialog(dialog.RecoveryCode)) ShowDialog(recovery);
                    }
            }
            finally { SetMenuEnabled(true); }
        }

        void ConfigureRecovery()
        {
            if (IsCovered) return;
            if (pin == null) { ConfigurePin(); return; }
            SetMenuEnabled(false);
            try
            {
                using (var dialog = new RecoveryDialog(pin))
                    if (ShowDialog(dialog) == DialogResult.OK && dialog.Result != null) pin = dialog.Result;
            }
            finally { SetMenuEnabled(true); }
        }

        void SetMenuEnabled(bool enabled)
        {
            enabled = enabled && activeDialog == null && !settingsOpen && !updating && !IsCovered;
            startupItem.Enabled = updateItem.Enabled = exitItem.Enabled = enabled;
        }

        DialogResult ShowDialog(Form dialog)
        {
            var previous = activeDialog;
            activeDialog = dialog;
            try { return previous == null ? dialog.ShowDialog() : dialog.ShowDialog(previous); }
            finally { activeDialog = previous; }
        }

        void ConfigureSettings()
        {
            if (activeDialog != null)
            {
                Native.ShowWindow(activeDialog.Handle, 9);
                activeDialog.Activate();
                return;
            }
            if (IsCovered || settingsOpen || updating || !exitItem.Enabled) return;
            bool cover = false;
            settingsOpen = true;
            SetMenuEnabled(false);
            try
            {
                using (var dialog = new SettingsDialog(() => ConfigurePin(), ConfigureRecovery, ConfigureBackground))
                    cover = ShowDialog(dialog) == DialogResult.OK;
            }
            finally { settingsOpen = false; SetMenuEnabled(true); }
            if (cover) RequestCover();
        }

        async void CheckUpdates()
        {
            if (IsCovered || settingsOpen || updating || !exitItem.Enabled) return;
            updating = true;
            SetMenuEnabled(false);
            updateItem.Text = "업데이트 확인 중...";
            try
            {
                var release = await Updates.Latest();
                if (release.Version <= AppInfo.CurrentVersion)
                {
                    MessageBox.Show("최신 버전입니다. (" + AppInfo.Version + ")", "업데이트", MessageBoxButtons.OK, MessageBoxIcon.Information);
                    return;
                }
                updateItem.Text = "새 버전 다운로드 중...";
                string planPath = await Updates.Prepare(release, Application.ExecutablePath);
                Updates.StartInstaller(planPath);
                ExitThread();
            }
            catch (Exception error) { MessageBox.Show("업데이트를 완료하지 못했습니다.\n" + error.Message, "업데이트", MessageBoxButtons.OK, MessageBoxIcon.Error); }
            finally
            {
                updating = false;
                if (!disposed) { updateItem.Text = "업데이트 체크"; SetMenuEnabled(true); }
            }
        }

        void ConfigureBackground()
        {
            if (IsCovered) return;
            SetMenuEnabled(false);
            try { using (var dialog = new BackgroundDialog()) ShowDialog(dialog); }
            finally { SetMenuEnabled(true); }
        }

        internal void RequestCover()
        {
            if (IsCovered || settingsOpen || updating || !exitItem.Enabled) return;
            if (pin == null) { ConfigurePin(); if (pin == null) return; }
            try { Cover(); }
            catch (Exception error)
            {
                Uncover();
                MessageBox.Show(error.Message, "화면 가리기 실패", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        void Tick(object sender, EventArgs e)
        {
            if (!IsCovered) return;
            foreach (var curtain in curtains) Native.SetWindowPos(curtain.Handle, new IntPtr(-1), 0, 0, 0, 0, 0x0013);
            RefreshStatus();
        }

        void Cover()
        {
            view = CurtainView.Clock;
            failureMessage = "";
            filter = new InputFilter(commands.Handle);
            try
            {
                RebuildCurtains();
                Native.SetThreadExecutionState(0x80000003);
                SetMenuEnabled(false);
            }
            catch { Uncover(); throw; }
        }

        void RebuildCurtains()
        {
            var next = new List<Curtain>();
            try
            {
                var screens = (Screen[])Screen.AllScreens.Clone();
                Array.Sort(screens, (a, b) => b.Primary.CompareTo(a.Primary));
                var bounds = new HashSet<Rectangle>();
                foreach (var screen in screens)
                {
                    if (!bounds.Add(screen.Bounds)) continue;
                    var curtain = new Curtain(screen.Bounds, screen.Primary);
                    next.Add(curtain);
                    curtain.Show();
                }
            }
            catch { foreach (var curtain in next) curtain.Dispose(); throw; }
            foreach (var curtain in curtains) curtain.Dispose();
            curtains.Clear();
            curtains.AddRange(next);
            RefreshStatus();
        }

        void DisplaysChanged(object sender, EventArgs e)
        {
            commands.Enqueue(delegate
            {
                if (!IsCovered) return;
                try { RebuildCurtains(); }
                catch { Native.LockWorkStation(); Uncover(); }
            });
        }

        void SelectView(CurtainView next)
        {
            view = next;
            digits.Clear();
            caretResetUtc = DateTime.UtcNow;
            RefreshStatus();
        }

        void OnPhysicalClick(Point point)
        {
            if (!IsCovered || verifying) return;
            foreach (var curtain in curtains)
            {
                if (!curtain.ShowsInterface || !curtain.Bounds.Contains(point)) continue;
                if (view == CurtainView.Clock) { SelectView(CurtainView.Pin); return; }
                var local = new Point(point.X - curtain.Left, point.Y - curtain.Top);
                if (curtain.RecoveryLinkBounds.Contains(local)) OnPhysicalKey(112);
                else if (curtain.SubmitBounds.Contains(local)) OnPhysicalKey(13);
            }
        }

        async void OnPhysicalKey(int key)
        {
            if (!IsCovered || verifying) return;
            if (key == 112)
            {
                SelectView(view == CurtainView.Recovery ? CurtainView.Pin : CurtainView.Recovery);
                return;
            }
            if (key == 27)
            {
                SelectView(view == CurtainView.Recovery ? CurtainView.Pin : digits.Length == 0 ? CurtainView.Clock : view);
                return;
            }
            if (DateTime.UtcNow < retryAfter || key == 16 || key == 17 || key == 18 || key >= 160 && key <= 165) return;
            if (view == CurtainView.Clock)
            {
                SelectView(CurtainView.Pin);
                if (key == 13) return;
            }
            int maximum = view == CurtainView.Recovery ? 20 : 12;
            if (key >= 48 && key <= 57 && digits.Length < maximum) digits.Append((char)key);
            else if (key >= 96 && key <= 105 && digits.Length < maximum) digits.Append((char)('0' + key - 96));
            else if (view == CurtainView.Recovery && key >= 65 && key <= 90 && digits.Length < maximum) digits.Append((char)key);
            else if (key == 8 && digits.Length > 0) digits.Length--;
            else if (key == 13 && digits.Length > 0)
            {
                bool recovering = view == CurtainView.Recovery;
                string candidate = digits.ToString();
                digits.Clear();
                verifying = true;
                RefreshStatus();
                bool matches = await Task.Run(() => recovering ? pin.MatchesRecovery(candidate) : pin.Matches(candidate));
                if (disposed) return;
                verifying = false;
                if (matches)
                {
                    failures = 0;
                    retryAfter = DateTime.MinValue;
                    Uncover();
                    if (recovering) ConfigurePin(true);
                    return;
                }
                failures++;
                failureMessage = recovering ? "복구 코드가 다릅니다." : "PIN이 다릅니다.";
                retryAfter = DateTime.UtcNow.AddSeconds(Math.Min(30, Math.Pow(2, Math.Min(failures, 5))));
            }
            caretResetUtc = DateTime.UtcNow;
            RefreshStatus();
        }

        void RefreshStatus()
        {
            var now = DateTime.UtcNow;
            bool inputReady = view != CurtainView.Clock && !verifying && now >= retryAfter;
            bool caretVisible = inputReady && (now - caretResetUtc).TotalMilliseconds % 1000 < 500;
            string status = verifying ? "확인 중..." : now < retryAfter
                ? failureMessage + " " + Math.Ceiling((retryAfter - now).TotalSeconds) + "초 후 다시 입력하세요."
                : view == CurtainView.Recovery ? pin.HasRecovery ? "보관한 복구 코드 20자리를 입력하세요." : "복구 코드가 없습니다. Ctrl+Alt+Del → 작업 관리자에서 앱을 종료하세요."
                : view == CurtainView.Pin ? "PIN 입력 후 Enter를 누르세요." : "";
            string dots = new string('●', digits.Length);
            foreach (var curtain in curtains)
            {
                if (!curtain.ShowsInterface) continue;
                string minute = DateTime.Now.ToString("yyyyMMddHHmm");
                if (curtain.Status == status && curtain.Dots == dots && curtain.View == view && curtain.Minute == minute && curtain.InputReady == inputReady && curtain.CaretVisible == caretVisible) continue;
                curtain.Status = status;
                curtain.Dots = dots;
                curtain.View = view;
                curtain.Minute = minute;
                curtain.InputReady = inputReady;
                curtain.CaretVisible = caretVisible;
                curtain.Invalidate();
            }
        }

        internal void Uncover()
        {
            foreach (var curtain in curtains) curtain.Dispose();
            curtains.Clear();
            if (filter != null) { filter.Dispose(); filter = null; }
            digits.Clear();
            Native.SetThreadExecutionState(0x80000000);
            SetMenuEnabled(true);
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing && !disposed)
            {
                disposed = true;
                timer.Dispose();
                SystemEvents.DisplaySettingsChanged -= DisplaysChanged;
                Uncover();
                Native.UnregisterHotKey(commands.Handle, 1);
                tray.Dispose();
                commands.Dispose();
            }
            base.Dispose(disposing);
        }
    }

    static class Native
    {
        internal delegate IntPtr HookProc(int code, IntPtr wParam, IntPtr lParam);
        [StructLayout(LayoutKind.Sequential)]
        internal struct Message { internal IntPtr hwnd; internal uint message; internal UIntPtr wParam; internal IntPtr lParam; internal uint time; internal Point point; internal uint privateData; }
        [DllImport("user32.dll")] internal static extern bool SetProcessDPIAware();
        [DllImport("user32.dll")] internal static extern bool ShowWindow(IntPtr hwnd, int command);
        [DllImport("user32.dll", SetLastError = true)] internal static extern bool SetWindowDisplayAffinity(IntPtr hwnd, uint affinity);
        [DllImport("user32.dll", SetLastError = true)] internal static extern bool SetLayeredWindowAttributes(IntPtr hwnd, uint key, byte alpha, uint flags);
        [DllImport("user32.dll", SetLastError = true)] internal static extern IntPtr SetWindowsHookEx(int type, HookProc proc, IntPtr module, uint thread);
        [DllImport("user32.dll")] internal static extern bool UnhookWindowsHookEx(IntPtr hook);
        [DllImport("user32.dll")] internal static extern IntPtr CallNextHookEx(IntPtr hook, int code, IntPtr wParam, IntPtr lParam);
        [DllImport("kernel32.dll", CharSet = CharSet.Unicode)] internal static extern IntPtr GetModuleHandle(string name);
        [DllImport("kernel32.dll")] internal static extern uint GetCurrentThreadId();
        [DllImport("user32.dll")] internal static extern bool PostMessage(IntPtr hwnd, uint message, IntPtr wParam, IntPtr lParam);
        [DllImport("user32.dll")] internal static extern bool PostThreadMessage(uint thread, uint message, IntPtr wParam, IntPtr lParam);
        [DllImport("user32.dll")] internal static extern int GetMessage(out Message message, IntPtr hwnd, uint minimum, uint maximum);
        [DllImport("user32.dll")] internal static extern bool PeekMessage(out Message message, IntPtr hwnd, uint minimum, uint maximum, uint remove);
        [DllImport("user32.dll")] internal static extern bool TranslateMessage(ref Message message);
        [DllImport("user32.dll")] internal static extern IntPtr DispatchMessage(ref Message message);
        [DllImport("user32.dll")] internal static extern bool SetWindowPos(IntPtr hwnd, IntPtr after, int x, int y, int width, int height, uint flags);
        [DllImport("user32.dll")] internal static extern bool RegisterHotKey(IntPtr hwnd, int id, uint modifiers, uint key);
        [DllImport("user32.dll")] internal static extern bool UnregisterHotKey(IntPtr hwnd, int id);
        [DllImport("user32.dll")] internal static extern bool LockWorkStation();
        [DllImport("kernel32.dll")] internal static extern uint SetThreadExecutionState(uint flags);
    }

}
