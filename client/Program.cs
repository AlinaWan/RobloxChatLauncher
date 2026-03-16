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
    static RegistryMonitor registryMonitor;

    static void Main(string[] args)
    {
        // Check if we are being called by the Inno Setup Uninstaller
        if (args.Length > 0 && args[0].Equals("--uninstall", StringComparison.OrdinalIgnoreCase))
        {
            RobloxRegistryUtil.Restore();
            return; // Exit immediately
        }

        // Start watching the Roblox protocol registry key in case aggressive bootstrappers such as Fishstrap
        // hijack it from us. This is a simple low overhead way to ensure we can re-register ourselves if needed
        // without relying on the user to run the launcher shortcut again
        var key = Registry.ClassesRoot.OpenSubKey(@"roblox-player\shell\open\command");

        if (key != null)
        {
            registryMonitor = new RegistryMonitor(key);

            registryMonitor.RegistryChanged += () =>
            {
                Thread.Sleep(200); // Debounce if an aggressive bootstrapper spams the key
                RobloxRegistryUtil.Register();
            };

            registryMonitor.Start();
        }

        // Check if the --force-run argument is present to bypass the 3-second rule for attaching to Roblox processes
        // And this also bypasses the if (args.Length == 0) check as well
        bool isForceRun = args.Length > 0 && args[0].Equals("--force-run", StringComparison.OrdinalIgnoreCase);

        // If no arguments are provided, we assume the user is trying to register this launcher as the default Roblox URI handler
        if (args.Length == 0)
        {
            if (RobloxRegistryUtil.Register())
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
                chatForm = new ChatForm(robloxGame, isForceRun);
                keyboardHandler = new ChatKeyboardHandler(chatForm);
                Application.Run(chatForm);
            }
        });

        chatThread.SetApartmentState(ApartmentState.STA);
        chatThread.Start();
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
