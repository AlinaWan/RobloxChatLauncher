using System.Diagnostics;
using System.Net.Http.Json;
using Microsoft.Win32;
using Semver;

namespace RobloxChatLauncher.Services
{
    public class UpdateService
    {
        private const string RepoOwner = "AlinaWan";
        private const string RepoName = "RobloxChatLauncher";
        private const string RegistryPath = @"SOFTWARE\WOW6432Node\Microsoft\Windows\CurrentVersion\Uninstall\{B0BACAFE-D326-4A7B-B6BA-1437C0DEBABE}_is1";

        public static async Task CheckAndDownloadUpdate(bool includePrerelease, Action<string> logCallback)
        {
            try
            {
                // 1. Get Local Version via Registry
                using var baseKey = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Registry64);
                using var subKey = baseKey.OpenSubKey(RegistryPath);

                if (subKey == null)
                {
                    logCallback?.Invoke("Running from source; update skipped.");
                    return;
                }

                var localRaw = subKey.GetValue("DisplayVersion")?.ToString() ?? "0.0.0";
                var localVersion = SemVersion.Parse(localRaw, SemVersionStyles.Any);

                // 2. Fetch Release from GitHub
                ChatForm.Client.DefaultRequestHeaders.UserAgent.Clear();
                ChatForm.Client.DefaultRequestHeaders.UserAgent.ParseAdd("RobloxChatLauncher-Updater");

                GitHubRelease targetRelease;
                try
                {
                    if (!includePrerelease)
                    {
                        targetRelease = await ChatForm.Client.GetFromJsonAsync<GitHubRelease>($"https://api.github.com/repos/{RepoOwner}/{RepoName}/releases/latest");
                    }
                    else
                    {
                        var releases = await ChatForm.Client.GetFromJsonAsync<List<GitHubRelease>>($"https://api.github.com/repos/{RepoOwner}/{RepoName}/releases?per_page=5");
                        targetRelease = releases?.OrderByDescending(r => SemVersion.Parse(r.TagName, SemVersionStyles.Any), SemVersion.PrecedenceComparer).FirstOrDefault();
                    }
                }
                catch (HttpRequestException ex) when (ex.StatusCode == System.Net.HttpStatusCode.NotFound)
                {
                    logCallback?.Invoke("No stable releases found. Use '/update prerelease' to check for alpha/beta versions.");
                    return;
                }

                if (targetRelease == null)
                {
                    logCallback?.Invoke("Could not find any releases.");
                    return;
                }

                var remoteVersion = SemVersion.Parse(targetRelease.TagName, SemVersionStyles.Any);

                // 3. Compare and Execute
                if (SemVersion.PrecedenceComparer.Compare(remoteVersion, localVersion) > 0)
                {
                    var asset = targetRelease.Assets.FirstOrDefault(a => a.Name.EndsWith("Installer.exe", StringComparison.OrdinalIgnoreCase));
                    if (asset == null)
                        return;

                    logCallback?.Invoke($"Downloading update ({localVersion} → {remoteVersion})...");

                    // Download to Temp folder
                    string tempPath = Path.Combine(Path.GetTempPath(), asset.Name);
                    var data = await ChatForm.Client.GetByteArrayAsync(asset.DownloadUrl);
                    await File.WriteAllBytesAsync(tempPath, data);

                    logCallback?.Invoke("Installing update... The app will close shortly.");

                    string logPath = Path.Combine(Path.GetTempPath(), "RobloxChatLauncher_install_log.txt");

                    // Run the installer
                    Process.Start(new ProcessStartInfo
                    {
                        FileName = tempPath,
                        Arguments = $"/VERYSILENT /SUPPRESSMSGBOXES /CLOSEAPPLICATIONS /RESTARTAPPLICATIONS /LOG=\"{logPath}\"",
                        UseShellExecute = true,
                    });

                    // Exit immediately so the installer can overwrite RobloxChatLauncher.exe
                    System.Windows.Forms.Application.Exit();
                }
                else
                {
                    logCallback?.Invoke($"Already up to date ({localVersion})");
                }
            }
            catch (Exception ex)
            {
                logCallback?.Invoke($"{ex.Message}");
            }
        }

        private class GitHubRelease
        {
            [System.Text.Json.Serialization.JsonPropertyName("tag_name")]
            public string TagName { get; set; } = "";

            [System.Text.Json.Serialization.JsonPropertyName("assets")]
            public List<GitHubAsset> Assets { get; set; } = new();
        }
        private class GitHubAsset
        {
            [System.Text.Json.Serialization.JsonPropertyName("name")]
            public string Name { get; set; } = "";

            [System.Text.Json.Serialization.JsonPropertyName("browser_download_url")]
            public string DownloadUrl { get; set; } = "";
        }
    }
}