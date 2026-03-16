using System.Diagnostics;
using System.Drawing;
using System.Windows.Forms;
using Microsoft.Win32;

using RobloxChatLauncher.Localization;
using RobloxChatLauncher.Utils;

namespace RobloxChatLauncher.Services
{
    public static class RobloxRegistryService
    {
        static bool RegisterAsRobloxLauncher()
        {
            try
            {
                const string keyPath = @"roblox-player\shell\open\command";

                using (var key = Registry.ClassesRoot.OpenSubKey(keyPath))
                {
                    string exePath = Process.GetCurrentProcess().MainModule.FileName; // Path to our launcher executable
                    string current = key?.GetValue("") as string;

                    // Only write if the registry is missing or doesn't point to us
                    if (current == null || !current.Contains(exePath))
                    {
                        using (var writeKey = Registry.ClassesRoot.CreateSubKey(keyPath))
                        {
                            // Change the RobloxPlayerBeta.exe path to point to our
                            // launcher's path with the "%1" argument to pass the URI through
                            writeKey.SetValue("", $"\"{exePath}\" \"%1\"");
                        }
                    }
                }
                return true;
            }
            catch (Exception ex)
            {
                MessageBox.Show($"{string.Format(Strings.RegistryWriteFailed, ex.Message)}", $"{Strings.Error}", MessageBoxButtons.OK, MessageBoxIcon.Error);
                return false;
            }
        }

        // This method attempts to restore the original Roblox registry key during uninstallation
        static void RestoreRobloxRegistry()
        {
            try
            {
                var originalClient = RobloxLocator.ResolveRobloxPlayer();
                if (originalClient == null)
                    return;

                const string keyPath = @"roblox-player\shell\open\command";

                // Construct the original command string
                // Bootstrappers and Vanilla usually expect the URI as the first argument
                string originalCommand = $"\"{originalClient.ExecutablePath}\" \"%1\"";

                using (var key = Registry.ClassesRoot.CreateSubKey(keyPath))
                {
                    key.SetValue("", originalCommand);
                }

                Console.WriteLine($"{string.Format(Strings.RestoredRegistry, originalCommand)}");
            }
            catch (Exception ex)
            {
                // Since this runs hidden during uninstall, we log to a file or ignore
                File.WriteAllText("uninstall_log.txt", ex.ToString());
            }
        }

        public static bool Register()
        {
            return RegisterAsRobloxLauncher();
        }

        public static void Restore()
        {
            RestoreRobloxRegistry();
        }
    }
}