using System.Diagnostics;
using System.Drawing;
using System.Windows.Forms;
using Microsoft.Win32;

using RobloxChatLauncher;
using RobloxChatLauncher.Localization;
using RobloxChatLauncher.Services;
using RobloxChatLauncher.Utils;

class Program
{
    static ChatForm chatForm;
    static ChatKeyboardHandler keyboardHandler;

    static void Main(string[] args)
    {
        // Check if we are being called by the Inno Setup Uninstaller
        if (args.Length > 0 && args[0].Equals("--uninstall", StringComparison.OrdinalIgnoreCase))
        {
            RestoreRobloxRegistry();
            return; // Exit immediately
        }

        // Check if the --force-run argument is present to bypass the 3-second rule for attaching to Roblox processes
        // And this also bypasses the if (args.Length == 0) check as well
        bool isForceRun = args.Length > 0 && args[0].Equals("--force-run", StringComparison.OrdinalIgnoreCase);

        // If no arguments are provided, we assume the user is trying to register this launcher as the default Roblox URI handler
        if (args.Length == 0)
        {
            if (RegisterAsRobloxLauncher())
            {
                using (NotifyIcon trayIcon = new NotifyIcon())
                {
                    trayIcon.Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath);
                    trayIcon.Visible = true;
                    trayIcon.ShowBalloonTip(3000, $"{Strings.LauncherRegisteredBalloonTitle}", $"{Strings.LauncherRegisteredBalloonText}", ToolTipIcon.None);
                    Thread.Sleep(1000);
                }
            }
            return;
        }

        if (!isForceRun)
        {
            // The first argument should be the Roblox URI, so we attempt to launch Roblox with it
            string uri = args[0];

            var robloxClient = RobloxLocator.ResolveRobloxPlayer();
            if (robloxClient != null)
            {
                Process.Start(new ProcessStartInfo { FileName = robloxClient.ExecutablePath, Arguments = uri, UseShellExecute = true });
            }
        }

        Thread chatThread = new Thread(() =>
        {
            // Pass isForceRun here to ignore the 3-second start time rule
            Process robloxGame = WaitForRobloxProcess(60, isForceRun);

            if (robloxGame == null && isForceRun)
            {
                var procs = Process.GetProcessesByName("RobloxPlayerBeta");
                if (procs.Length > 0)
                    robloxGame = procs[0];
            }

            if (robloxGame != null)
            {
                chatForm = new ChatForm(robloxGame);
                keyboardHandler = new ChatKeyboardHandler(chatForm);
                Application.Run(chatForm);
            }
        });

        chatThread.SetApartmentState(ApartmentState.STA);
        chatThread.Start();
    }

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

    static Process WaitForRobloxProcess(int timeoutSeconds, bool ignoreStartTime)
    {
        // Record when the launcher actually started, so we can ignore Roblox processes that started long before
        DateTime launcherStartTime = DateTime.Now;

        // Loop every 500ms until the actual game engine process appears
        for (int i = 0; i < timeoutSeconds * 2; i++)
        {
            var processes = Process.GetProcessesByName("RobloxPlayerBeta");
            foreach (var proc in processes)
            {
                try
                {
                    // If ignoreStartTime is true, it attaches to any existing Roblox process immediately
                    if (ignoreStartTime || proc.StartTime > launcherStartTime.AddSeconds(-3))
                    {
                        return proc;
                    }
                }
                catch { /* Process might have exited already */ }
            }
            Thread.Sleep(500);
        }
        return null;
    }
}
