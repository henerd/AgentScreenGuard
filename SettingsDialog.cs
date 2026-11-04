using System;
using System.Drawing;
using System.Windows.Forms;
using Microsoft.Win32;

namespace AgentScreenGuard
{
    sealed class StartupRegistration
    {
        const string ValueName = "AgentScreenGuard";
        readonly string executable, keyPath;
        internal StartupRegistration(string path, string registryPath = "Software\\Microsoft\\Windows\\CurrentVersion\\Run")
        { executable = path; keyPath = registryPath; }

        internal bool Enabled
        {
            get
            {
                using (var key = Registry.CurrentUser.OpenSubKey(keyPath))
                    return key != null && String.Equals(key.GetValue(ValueName) as string, "\"" + executable + "\"", StringComparison.OrdinalIgnoreCase);
            }
            set
            {
                using (var key = Registry.CurrentUser.CreateSubKey(keyPath))
                {
                    if (value) key.SetValue(ValueName, "\"" + executable + "\"", RegistryValueKind.String);
                    else key.DeleteValue(ValueName, false);
                }
            }
        }
    }

    sealed class SettingsDialog : Form
    {
        internal SettingsDialog(Action changePin, Action recovery, Action background)
        {
            Text = "화면 잠금 · 설정";
            Icon = AppInfo.Icon;
            ClientSize = new Size(520, 420);
            Font = new Font("맑은 고딕", 10);
            BackColor = Color.FromArgb(247, 248, 250);
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = MinimizeBox = false;
            StartPosition = FormStartPosition.CenterScreen;
            Controls.Add(new Label { Text = "화면 잠금", Location = new Point(24, 18), Size = new Size(340, 44), Font = new Font("맑은 고딕", 20) });
            Controls.Add(new Label { Text = "버전 " + AppInfo.Version, Location = new Point(370, 34), Size = new Size(125, 26), TextAlign = ContentAlignment.MiddleRight });
            Controls.Add(new Label { Text = AppInfo.Usage, Location = new Point(24, 84), Size = new Size(472, 124) });
            var pin = AddButton("PIN 변경", 24, 224, 226);
            var restore = AddButton("복구 코드 발급 / 재발급", 268, 224, 228);
            var picture = AddButton("잠금 화면 배경 변경", 24, 278, 472);
            var cover = AddButton("지금 잠그기", 268, 356, 136);
            var close = AddButton("닫기", 414, 356, 82);
            pin.Click += delegate { changePin(); };
            restore.Click += delegate { recovery(); };
            picture.Click += delegate { background(); };
            cover.DialogResult = DialogResult.OK;
            close.DialogResult = DialogResult.Cancel;
            CancelButton = close;
        }

        Button AddButton(string title, int x, int y, int width)
        {
            var button = new Button { Text = title, Location = new Point(x, y), Size = new Size(width, 40) };
            Controls.Add(button);
            return button;
        }
    }
}
