using System;
using System.Diagnostics;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Reflection;
using System.Security.Cryptography;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Web.Script.Serialization;
using System.Windows.Forms;

namespace AgentScreenGuard
{
    sealed class ReleaseAsset
    {
        public string name { get; set; }
        public string browser_download_url { get; set; }
        public string digest { get; set; }
        public long size { get; set; }
    }

    sealed class ReleaseInfo
    {
        public string tag_name { get; set; }
        public bool draft { get; set; }
        public bool prerelease { get; set; }
        public ReleaseAsset[] assets { get; set; }
        internal Version Version { get { return Updates.ParseVersion(tag_name); } }
    }

    sealed class UpdatePlan
    {
        public int ParentProcessId { get; set; }
        public string TargetPath { get; set; }
        public string PayloadPath { get; set; }
        public string Sha256 { get; set; }
        public string Version { get; set; }
    }

    static class Updates
    {
        internal static readonly string Root = Path.Combine(Path.GetDirectoryName(PinStore.FilePath), "updates");

        internal static Version ParseVersion(string text)
        {
            if (String.IsNullOrWhiteSpace(text)) throw new InvalidDataException("배포 버전 정보가 없습니다.");
            string value = text.Trim();
            if (value.StartsWith("v", StringComparison.OrdinalIgnoreCase)) value = value.Substring(1);
            Version version;
            if (!Regex.IsMatch(value, @"^\d+\.\d+\.\d+(\.\d+)?$") || !Version.TryParse(value, out version))
                throw new InvalidDataException("지원하지 않는 배포 버전입니다.");
            return new Version(version.Major, version.Minor, version.Build, Math.Max(0, version.Revision));
        }

        internal static ReleaseInfo ParseRelease(string json)
        {
            var release = new JavaScriptSerializer().Deserialize<ReleaseInfo>(json);
            if (release == null || release.draft || release.prerelease || release.assets == null)
                throw new InvalidDataException("정식 배포 정보를 찾지 못했습니다.");
            ParseVersion(release.tag_name);
            return release;
        }

        internal static string Hash(string path)
        {
            using (var file = File.OpenRead(path))
            using (var algorithm = SHA256.Create()) return BitConverter.ToString(algorithm.ComputeHash(file)).Replace("-", "").ToLowerInvariant();
        }

        internal static void VerifyPayload(string path, string hash, string version)
        {
            if (hash == null || !Regex.IsMatch(hash, "^[a-fA-F0-9]{64}$") || !String.Equals(Hash(path), hash, StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("업데이트 파일 검증에 실패했습니다.");
            if (AssemblyName.GetAssemblyName(path).Version != ParseVersion(version))
                throw new InvalidDataException("업데이트 파일의 버전이 배포 정보와 다릅니다.");
        }

        static HttpClient CreateClient()
        {
            ServicePointManager.SecurityProtocol |= SecurityProtocolType.Tls12;
            var client = new HttpClient { Timeout = TimeSpan.FromSeconds(90) };
            client.DefaultRequestHeaders.UserAgent.ParseAdd("AgentScreenGuard/" + AppInfo.Version);
            return client;
        }

        internal static async Task<ReleaseInfo> Latest()
        {
            using (var client = CreateClient())
            using (var response = await client.GetAsync("https://api.github.com/repos/" + AppInfo.Repository + "/releases/latest"))
            {
                if (response.StatusCode == HttpStatusCode.NotFound) throw new InvalidOperationException("등록된 정식 배포가 아직 없습니다.");
                response.EnsureSuccessStatusCode();
                return ParseRelease(await response.Content.ReadAsStringAsync());
            }
        }

        static string AssetUrl(ReleaseInfo release, ReleaseAsset asset)
        {
            string expected = "https://github.com/" + AppInfo.Repository + "/releases/download/" + Uri.EscapeDataString(release.tag_name) + "/" + Uri.EscapeDataString(asset.name);
            if (!String.Equals(expected, asset.browser_download_url, StringComparison.Ordinal))
                throw new InvalidDataException("배포 파일 주소가 올바르지 않습니다.");
            return expected;
        }

        internal static async Task<string> Prepare(ReleaseInfo release, string targetPath)
        {
            var asset = release.assets.SingleOrDefault(a => a.name == AppInfo.FileName);
            if (asset == null || asset.size <= 0 || asset.size > 30 * 1024 * 1024) throw new InvalidDataException("업데이트 실행 파일을 찾지 못했습니다.");
            string folder = Path.Combine(Root, Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(folder);
            string payload = Path.Combine(folder, "payload.exe");
            using (var client = CreateClient())
            {
                string hash = asset.digest != null && asset.digest.StartsWith("sha256:", StringComparison.Ordinal) ? asset.digest.Substring(7) : null;
                if (hash == null)
                {
                    var checksum = release.assets.SingleOrDefault(a => a.name == AppInfo.FileName + ".sha256");
                    if (checksum == null) throw new InvalidDataException("업데이트 검증 파일을 찾지 못했습니다.");
                    string contents = await client.GetStringAsync(AssetUrl(release, checksum));
                    var match = Regex.Match(contents.Trim(), @"^([a-fA-F0-9]{64})\s+\*?AgentScreenGuard\.exe$");
                    if (!match.Success) throw new InvalidDataException("업데이트 검증 정보가 올바르지 않습니다.");
                    hash = match.Groups[1].Value;
                }
                using (var response = await client.GetAsync(AssetUrl(release, asset), HttpCompletionOption.ResponseHeadersRead))
                {
                    response.EnsureSuccessStatusCode();
                    using (var input = await response.Content.ReadAsStreamAsync())
                    using (var output = new FileStream(payload, FileMode.CreateNew, FileAccess.Write))
                    {
                        var buffer = new byte[81920];
                        int count;
                        long total = 0;
                        while ((count = await input.ReadAsync(buffer, 0, buffer.Length)) != 0)
                        {
                            total += count;
                            if (total > asset.size || total > 30 * 1024 * 1024) throw new InvalidDataException("업데이트 파일 크기가 올바르지 않습니다.");
                            await output.WriteAsync(buffer, 0, count);
                        }
                        if (total != asset.size) throw new InvalidDataException("업데이트 파일을 모두 받지 못했습니다.");
                    }
                }
                VerifyPayload(payload, hash, release.tag_name);
                string planPath = Path.Combine(folder, "plan.json");
                var plan = new UpdatePlan { ParentProcessId = Process.GetCurrentProcess().Id, TargetPath = Path.GetFullPath(targetPath), PayloadPath = payload, Sha256 = hash, Version = release.tag_name };
                File.WriteAllText(planPath, new JavaScriptSerializer().Serialize(plan));
                File.Copy(Application.ExecutablePath, Path.Combine(folder, "installer.exe"));
                return planPath;
            }
        }

        internal static void StartInstaller(string planPath)
        {
            Process.Start(new ProcessStartInfo(Path.Combine(Path.GetDirectoryName(planPath), "installer.exe"), "--apply-update " + Quote(planPath)) { UseShellExecute = false, CreateNoWindow = true });
        }

        internal static string Quote(string value) { return "\"" + value.Replace("\"", "\\\"") + "\""; }
        internal static string HealthPath(string planPath) { return Path.Combine(Path.GetDirectoryName(planPath), "ready"); }

        internal static bool IsStagingPath(string path)
        {
            string root = Path.GetFullPath(Root) + Path.DirectorySeparatorChar;
            return Path.GetFullPath(path).StartsWith(root, StringComparison.OrdinalIgnoreCase);
        }
    }

    static class UpdateInstaller
    {
        internal static string Run(string planPath)
        {
            UpdatePlan plan = null;
            string candidate = null, backup = null;
            Process started = null;
            bool parentExited = false, replaced = false;
            try
            {
                if (!Updates.IsStagingPath(planPath) || Path.GetFileName(planPath) != "plan.json") throw new InvalidDataException("업데이트 경로가 올바르지 않습니다.");
                plan = new JavaScriptSerializer().Deserialize<UpdatePlan>(File.ReadAllText(planPath));
                if (plan == null || plan.ParentProcessId <= 0 || !Path.IsPathRooted(plan.TargetPath) || !String.Equals(Path.GetFileName(plan.TargetPath), AppInfo.FileName, StringComparison.OrdinalIgnoreCase)
                    || !String.Equals(Path.GetFullPath(plan.PayloadPath), Path.Combine(Path.GetDirectoryName(planPath), "payload.exe"), StringComparison.OrdinalIgnoreCase))
                    throw new InvalidDataException("업데이트 요청이 올바르지 않습니다.");
                try
                {
                    using (var parent = Process.GetProcessById(plan.ParentProcessId))
                    {
                        string parentPath = null;
                        try { if (!parent.HasExited) parentPath = parent.MainModule.FileName; }
                        catch (InvalidOperationException) { if (!parent.HasExited) throw; }
                        catch (Win32Exception) { if (!parent.HasExited) throw; }
                        if (parentPath != null && !String.Equals(parentPath, Path.GetFullPath(plan.TargetPath), StringComparison.OrdinalIgnoreCase)) throw new InvalidDataException("업데이트 대상 프로세스가 다릅니다.");
                        if (!parent.WaitForExit(30000)) throw new TimeoutException("실행 중인 프로그램이 종료되지 않았습니다.");
                    }
                }
                catch (ArgumentException) { }
                parentExited = true;
                Updates.VerifyPayload(plan.PayloadPath, plan.Sha256, plan.Version);
                if (Updates.ParseVersion(plan.Version) <= AssemblyName.GetAssemblyName(plan.TargetPath).Version) throw new InvalidDataException("설치된 버전보다 최신인 파일만 적용할 수 있습니다.");
                candidate = plan.TargetPath + ".update-" + Guid.NewGuid().ToString("N");
                backup = plan.TargetPath + ".rollback-" + Guid.NewGuid().ToString("N");
                File.Copy(plan.PayloadPath, candidate);
                Updates.VerifyPayload(candidate, plan.Sha256, plan.Version);
                File.Replace(candidate, plan.TargetPath, backup, true);
                replaced = true;
                string health = Updates.HealthPath(planPath);
                if (File.Exists(health)) File.Delete(health);
                started = Process.Start(new ProcessStartInfo(plan.TargetPath, "--updated " + Updates.Quote(health)) { WorkingDirectory = Path.GetDirectoryName(plan.TargetPath), UseShellExecute = false });
                var deadline = DateTime.UtcNow.AddSeconds(15);
                while (DateTime.UtcNow < deadline)
                {
                    if (File.Exists(health))
                    {
                        replaced = false;
                        TryDelete(backup);
                        TryDelete(health);
                        TryDelete(plan.PayloadPath);
                        TryDelete(planPath);
                        return null;
                    }
                    if (started.HasExited) break;
                    Thread.Sleep(100);
                }
                throw new InvalidOperationException("새 버전의 시작을 확인하지 못했습니다.");
            }
            catch (Exception error)
            {
                try
                {
                    if (replaced)
                    {
                        if (started != null && !started.HasExited) { started.Kill(); started.WaitForExit(5000); }
                        File.Replace(backup, plan.TargetPath, null, true);
                    }
                    if (parentExited && plan != null && File.Exists(plan.TargetPath)) Process.Start(new ProcessStartInfo(plan.TargetPath) { WorkingDirectory = Path.GetDirectoryName(plan.TargetPath), UseShellExecute = false });
                }
                catch (Exception restoreError) { return "업데이트 복구에 실패했습니다.\n" + restoreError.Message; }
                return (replaced ? "이전 버전으로 복구했습니다.\n" : "업데이트를 적용하지 못했습니다.\n") + error.Message;
            }
            finally
            {
                if (candidate != null) TryDelete(candidate);
                if (started != null) started.Dispose();
            }
        }

        static void TryDelete(string path)
        {
            try { if (File.Exists(path)) File.Delete(path); }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }
    }
}
