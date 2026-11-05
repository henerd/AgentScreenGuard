using System;
using System.Drawing;
using System.IO;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;
using Microsoft.Win32;

namespace AgentScreenGuard
{
    static class RecoveryExport
    {
        internal static string DownloadsDirectory
        {
            get
            {
                using (var key = Registry.CurrentUser.OpenSubKey("Software\\Microsoft\\Windows\\CurrentVersion\\Explorer\\User Shell Folders"))
                {
                    string path = key == null ? null : key.GetValue("{374DE290-123F-4565-9164-39C4925E467B}") as string;
                    if (!String.IsNullOrEmpty(path)) return Environment.ExpandEnvironmentVariables(path);
                }
                return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads");
            }
        }

        internal static SaveFileDialog CreatePicker()
        {
            return new SaveFileDialog
            {
                Title = "복구 코드 저장", FileName = "복구코드.txt", Filter = "텍스트 파일|*.txt",
                InitialDirectory = DownloadsDirectory, CheckFileExists = false, CheckPathExists = true,
                DefaultExt = "txt", AddExtension = true, OverwritePrompt = true, RestoreDirectory = true
            };
        }

        internal static void Write(string path, string code)
        {
            File.WriteAllText(path, Content(code), Encoding.UTF8);
        }

        static string Content(string code)
        {
            return "화면 잠금 복구 코드\r\n\r\n" + code + "\r\n\r\nPIN 입력 화면에서 F1을 누르거나 'PIN을 잊으셨나요?'를 클릭한 후 코드를 입력하세요.\r\n이 파일을 다른 기기나 안전한 장소에도 보관하세요.\r\n";
        }

        internal static string SaveToDownloads(string code)
        {
            string folder = DownloadsDirectory;
            Directory.CreateDirectory(folder);
            string path = Path.Combine(folder, "복구코드-" + DateTime.Now.ToString("yyyyMMdd-HHmmss-fff") + ".txt");
            using (var file = new FileStream(path, FileMode.CreateNew, FileAccess.Write))
            using (var writer = new StreamWriter(file, Encoding.UTF8))
                writer.Write(Content(code));
            return path;
        }
    }

    sealed class RecoveryDialog : TrayDialog
    {
        internal PinStore Result;
        readonly Label notice, message;
        readonly TextBox secret;
        readonly Button issue, copy, save, quickSave, close;
        string recoveryCode;

        internal RecoveryDialog(string code) : this((PinStore)null) { ShowCode(code); }

        internal RecoveryDialog(PinStore current)
        {
            Text = "화면 잠금 · 복구 코드";
            Icon = AppInfo.Icon;
            ClientSize = new Size(580, 330);
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = MinimizeBox = false;
            StartPosition = FormStartPosition.CenterScreen;
            BackColor = Color.FromArgb(247, 248, 250);
            Font = new Font("맑은 고딕", 10);
            Controls.Add(new Label { Text = "PIN을 잊었을 때를 대비하세요", Location = new Point(24, 20), Size = new Size(532, 42), Font = new Font("맑은 고딕", 17) });
            notice = new Label { Text = "현재 PIN을 확인한 후 복구 코드를 발급합니다.\n재발급하면 이전 복구 코드는 사용할 수 없습니다.", Location = new Point(24, 78), Size = new Size(532, 60) };
            secret = new TextBox { Location = new Point(24, 151), Size = new Size(390, 34), UseSystemPasswordChar = true, MaxLength = 12, Font = new Font("Segoe UI", 14) };
            issue = new Button { Text = "발급", Location = new Point(426, 148), Size = new Size(130, 40) };
            message = new Label { Location = new Point(24, 200), Size = new Size(532, 42), ForeColor = Color.Firebrick };
            copy = new Button { Text = "코드 복사", Location = new Point(24, 265), Size = new Size(105, 36), Enabled = false };
            save = new Button { Text = "파일로 보관", Location = new Point(139, 265), Size = new Size(120, 36), Enabled = false };
            quickSave = new Button { Text = "다운로드에 저장", Location = new Point(269, 265), Size = new Size(155, 36), Enabled = false };
            close = new Button { Text = "닫기", Location = new Point(466, 265), Size = new Size(90, 36), DialogResult = DialogResult.Cancel };
            Controls.AddRange(new Control[] { notice, secret, issue, message, copy, save, quickSave, close });
            AcceptButton = issue;
            CancelButton = close;
            issue.Click += async delegate
            {
                if (current == null) return;
                issue.Enabled = close.Enabled = false;
                ControlBox = false;
                string candidate = secret.Text;
                try
                {
                    if (!await Task.Run(() => current.Matches(candidate))) { message.Text = "현재 PIN이 올바르지 않습니다."; return; }
                    var next = current.Copy();
                    string code = await Task.Run(() => next.CreateRecovery());
                    next.Save();
                    Result = next;
                    ShowCode(code);
                }
                catch (Exception error) { message.Text = error.Message; }
                finally { if (!IsDisposed) { issue.Enabled = close.Enabled = true; ControlBox = true; } }
            };
            copy.Click += delegate
            {
                try { Clipboard.SetText(recoveryCode); message.Text = "복사했습니다. 다른 사람이 접근할 수 없는 곳에 보관하세요."; }
                catch (Exception error) { message.Text = error.Message; }
            };
            save.Click += delegate
            {
                using (var picker = RecoveryExport.CreatePicker())
                {
                    if (picker.ShowDialog() != DialogResult.OK) return;
                    try
                    {
                        RecoveryExport.Write(picker.FileName, recoveryCode);
                        message.Text = "저장 완료: " + picker.FileName;
                    }
                    catch (Exception) { message.Text = "저장할 수 없습니다. ‘다운로드에 저장’을 사용하거나 다른 폴더를 선택하세요."; }
                }
            };
            quickSave.Click += delegate
            {
                try { message.Text = "저장 완료: " + RecoveryExport.SaveToDownloads(recoveryCode); }
                catch (Exception error) { message.Text = "다운로드 폴더에 저장하지 못했습니다. 파일로 보관에서 다른 폴더를 선택하세요.\n" + error.Message; }
            };
            close.Click += delegate { if (Result != null) DialogResult = DialogResult.OK; };
        }

        void ShowCode(string code)
        {
            recoveryCode = code;
            notice.Text = "이 코드는 다시 표시되지 않습니다. 다른 기기나 안전한 곳에 보관하세요.\n잠금 화면의 ‘PIN을 잊으셨나요?’ 또는 F1에서 사용할 수 있습니다.";
            secret.UseSystemPasswordChar = false;
            secret.MaxLength = 64;
            secret.Text = code;
            secret.ReadOnly = true;
            secret.Width = 532;
            issue.Visible = false;
            copy.Enabled = save.Enabled = quickSave.Enabled = true;
            message.Text = "문서·바탕 화면에 저장이 막히면 ‘다운로드에 저장’을 사용하세요.";
            message.ForeColor = Color.FromArgb(70, 70, 70);
            AcceptButton = close;
        }

        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            if (Result != null) DialogResult = DialogResult.OK;
            base.OnFormClosing(e);
        }
    }
}
