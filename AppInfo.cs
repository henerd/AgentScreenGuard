using System;
using System.Drawing;
using System.Reflection;

[assembly: AssemblyTitle("Screen Guard")]
[assembly: AssemblyProduct("Screen Guard")]
[assembly: AssemblyCompany("henerd")]
[assembly: AssemblyVersion(AgentScreenGuard.AppInfo.Version)]
[assembly: AssemblyFileVersion(AgentScreenGuard.AppInfo.Version)]
[assembly: AssemblyInformationalVersion(AgentScreenGuard.AppInfo.Version)]

namespace AgentScreenGuard
{
    static class AppInfo
    {
        internal const string Version = "1.0.4";
        internal const string Repository = "henerd/AgentScreenGuard";
        internal const string FileName = "AgentScreenGuard.exe";
        internal const string Usage = "화면 잠금: Ctrl+Alt+L\n해제: 현장 키보드로 PIN 입력 후 Enter\n설정: 트레이 아이콘 두 번 클릭\nPIN 분실: PIN 입력 화면에서 F1 또는 ‘PIN을 잊으셨나요?’";
        internal static readonly Version CurrentVersion = Updates.ParseVersion(Version);
        internal static readonly Icon Icon = LoadIcon();

        static Icon LoadIcon()
        {
            using (var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream("AgentScreenGuard.AppIcon"))
                return stream == null ? (Icon)SystemIcons.Shield.Clone() : new Icon(stream, 32, 32);
        }
    }
}
