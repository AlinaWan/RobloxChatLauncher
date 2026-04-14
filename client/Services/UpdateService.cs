using System.Diagnostics;
using System.Net.Http.Json;
using Microsoft.Win32;
using Semver;

using RobloxChatLauncher.Localization;
using RobloxChatLauncher.Core;

namespace RobloxChatLauncher.Services
{
    public enum UpdateMode
    {
        Background, // no auto-relaunch
        Manual      // auto-relaunch with /FORCERUN
    }

    public class UpdateService
    {
        private static readonly SemaphoreSlim UpdateLock = new(1, 1);
        public static string PendingUpdatePath { get; private set; } = string.Empty;
        private static readonly string RegistryPath = $@"SOFTWARE\WOW6432Node\Microsoft\Windows\CurrentVersion\Uninstall\{Constants.APP_GUID}";

        public static async Task CheckAndDownloadUpdate(UpdateMode mode, bool includePrerelease, Action<string> logCallback)
        {
            await UpdateLock.WaitAsync();

            try
            {
                if (mode == UpdateMode.Manual && includePrerelease)
                {
                    // Always recheck for updates when manually checking with prerelease included
                    PendingUpdatePath = string.Empty;
                }

                using var client = new HttpClient();
                client.DefaultRequestHeaders.UserAgent.ParseAdd("RobloxChatLauncher-Updater");

                // If there's already a pending update from a previous check, we can skip the version check and just run it (if manual)
                if (!string.IsNullOrEmpty(PendingUpdatePath) && File.Exists(PendingUpdatePath))
                {
                    if (mode == UpdateMode.Manual)
                    {
                        RunInstaller(PendingUpdatePath, relaunch: true);
                    }
                    return;
                }

                // Get Local Version via Registry
                using var baseKey = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Registry64);
                using var subKey = baseKey.OpenSubKey(RegistryPath);

                if (subKey == null)
                {
                    if (mode == UpdateMode.Manual)
                    {
                        logCallback?.Invoke($"{Strings.RunningFromSource}");
                    }
                    return;
                }

                var localRaw = subKey.GetValue("DisplayVersion")?.ToString() ?? "0.0.0";
                var localVersion = SemVersion.Parse(localRaw, SemVersionStyles.Any);

                // Fetch Release from GitHub
                client.DefaultRequestHeaders.UserAgent.Clear();
                client.DefaultRequestHeaders.UserAgent.ParseAdd("RobloxChatLauncher-Updater");

                GitHubRelease targetRelease;
                try
                {
                    if (!includePrerelease)
                    {
                        targetRelease = await client.GetFromJsonAsync<GitHubRelease>($"https://api.github.com/repos/{Constants.REPO_OWNER}/{Constants.REPO_NAME}/releases/latest");
                    }
                    else
                    {
                        var releases = await client.GetFromJsonAsync<List<GitHubRelease>>($"https://api.github.com/repos/{Constants.REPO_OWNER}/{Constants.REPO_NAME}/releases?per_page=5");
                        targetRelease = releases?.OrderByDescending(r => SemVersion.Parse(r.TagName, SemVersionStyles.Any), SemVersion.PrecedenceComparer).FirstOrDefault();
                    }
                }
                catch (HttpRequestException ex) when (ex.StatusCode == System.Net.HttpStatusCode.NotFound)
                {
                    if (mode == UpdateMode.Manual)
                    {
                        logCallback?.Invoke($"{Strings.NoStableReleases}");
                    }
                    return;
                }

                if (targetRelease == null)
                {
                    if (mode == UpdateMode.Manual)
                    {
                        logCallback?.Invoke($"{Strings.CouldNotFindReleases}");
                    }
                    return;
                }

                var remoteVersion = SemVersion.Parse(targetRelease.TagName, SemVersionStyles.Any);

                // Compare and Download
                if (SemVersion.PrecedenceComparer.Compare(remoteVersion, localVersion) > 0)
                {
                    var asset = targetRelease.Assets.FirstOrDefault(a => a.Name.EndsWith("Installer.exe", StringComparison.OrdinalIgnoreCase));
                    if (asset == null)
                        return;
                    
                    // EXTREMELY IMPORTANT: File name MUST be unique to avoid conflicts with previous downloads
                    string uniqueName = $"{Guid.NewGuid()}_{asset.Name}";
                    string tempPath = Path.Combine(Path.GetTempPath(), uniqueName);

                    // Check if the file already exists on disk from a previous background check
                    if (!File.Exists(tempPath))
                    {
                        logCallback?.Invoke($"{string.Format(Strings.DownloadingUpdate, localVersion, remoteVersion)}");

                        // Download atomically
                        string tempDownload = tempPath + ".download";
                        var data = await client.GetByteArrayAsync(asset.DownloadUrl);
                        await File.WriteAllBytesAsync(tempDownload, data);
                        File.Move(tempDownload, tempPath, true);
                    }

                    PendingUpdatePath = tempPath;

                    // Decide whether to install now or wait
                    if (mode == UpdateMode.Manual)
                    {
                        logCallback?.Invoke($"{Strings.InstallingUpdate}");
                        RunInstaller(PendingUpdatePath, relaunch: true);
                    }
                }
                else if (mode == UpdateMode.Manual)
                {
                    logCallback?.Invoke($"{string.Format(Strings.AlreadyUpToDate, localVersion)}");
                }
            }
            catch (Exception ex)
            {
                if (mode == UpdateMode.Manual)
                {
                    logCallback?.Invoke($"{ex.Message}");
                }
            }
            finally
            {
                UpdateLock.Release();
            }
        }

        public static void RunInstaller(string path, bool relaunch)
        {
            if (string.IsNullOrEmpty(path) || !File.Exists(path))
                return;

            string logPath = Path.Combine(Path.GetTempPath(), "RobloxChatLauncher_install_log.txt");

            // Pass the /FORCERUN flag to restart the app after installation and attach to the current Roblox process
            // /CLEANINSTALL uninstalls the previous version before installing the new one
            // /NORESTORE tells installer to not change registry key back to Roblox on uninstall of the old version
            string forceRunArg = relaunch ? "/FORCERUN" : "";
            string arguments = $"/VERYSILENT /SUPPRESSMSGBOXES /CLOSEAPPLICATIONS /RESTARTAPPLICATIONS /LOG=\"{logPath}\" {forceRunArg} /CLEANINSTALL /NORESTORE";

            Process.Start(new ProcessStartInfo
            {
                FileName = path,
                Arguments = arguments.Trim(),
                UseShellExecute = true,
            });

            // Exit immediately so the installer can overwrite RobloxChatLauncher.exe
            Environment.Exit(0);
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
