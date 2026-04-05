using System.Diagnostics;
using System.Drawing;
using System.Windows.Forms;
using Microsoft.Win32;

using RobloxChatLauncher;
using RobloxChatLauncher.Core;
using RobloxChatLauncher.Localization;
using RobloxChatLauncher.Services;
using RobloxChatLauncher.Utils;

class Program
{
    static Mutex? programMutex;
    static ChatForm? chatForm;
    static ChatKeyboardHandler? keyboardHandler;
    static RegistryMonitor? registryMonitor;

    static void Main(string[] args)
    {
        // Check if we are being called by the Inno Setup Uninstaller
        bool isUninstall = args.Contains("--uninstall", StringComparer.OrdinalIgnoreCase);
        // Launch the Roblox client without a URI to launch to Roblox's homepage instead of a game
        bool isLaunchHomepage = args.Contains("--launch-homepage", StringComparer.OrdinalIgnoreCase);
        // Bypass the mutex check
        bool isAllowMultiple = args.Contains("--allow-multiple", StringComparer.OrdinalIgnoreCase);
        // Attach to any existing Roblox process immediately without waiting, useful for attaching to already running games
        bool isForceRun = args.Contains("--force-run", StringComparer.OrdinalIgnoreCase);

        // Check if we are being called by the Inno Setup Uninstaller
        if (isUninstall)
        {
            RobloxRegistryUtil.Restore();
            return; // Exit immediately
        }

        // Mutex (newer replaces older)
        if (!isAllowMultiple)
        {
            bool createdNew;
            programMutex = new Mutex(true, $"Local\\{Constants.APP_GUID}", out createdNew);

            if (!createdNew)
            {
                // Terminate existing instances
                Process current = Process.GetCurrentProcess();
                Process[] processes = Process.GetProcessesByName(current.ProcessName);

                foreach (Process process in processes)
                {
                    if (process.Id != current.Id &&
                        process.MainModule?.FileName == current.MainModule?.FileName)
                    {
                        try
                        {
                            if (!process.HasExited)
                            {
                                process.CloseMainWindow(); // Politely ask the other instance to exit

                                if (!process.WaitForExit(1000))
                                    process.Kill();
                            }
                        }
                        catch { }
                    }
                }

                // Wait until mutex becomes available
                try
                {
                    programMutex.WaitOne();
                }
                catch (AbandonedMutexException) { }
            }
        }

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
            return; // Exit immediately as there's no game to launch or attach to
        }

        // Resolve the Roblox client location
        // This will resolve either a bootstrapper or the vanilla client
        var robloxClient = RobloxLocator.ResolveRobloxPlayer();

        // Roblox Chat Launcher should always be first in the chain of launchers since we launch the client but they don't launch us
        if (robloxClient != null)
        {
            var key = Registry.ClassesRoot.OpenSubKey(@"roblox-player\shell\open\command");
            if (key != null)
            {
                registryMonitor = new RegistryMonitor(key, watchSubtree: false, debounceMilliseconds: 200);
                registryMonitor.RegistryChanged += () =>
                {
                    RobloxRegistryUtil.Register();
                    Debug.WriteLine("[RegistryMonitor] Registry change event raised and repaired.");
                };
                registryMonitor.Start();
            }
        }

        if (robloxClient != null && !isForceRun)
        {
            // If it's the homepage, we send no args. Otherwise, we send the URI from args[0].
            string arguments = isLaunchHomepage ? "" : args[0];

            if (Process.Start(new ProcessStartInfo
                {
                    FileName = robloxClient.ExecutablePath,
                    Arguments = arguments, // Either launches the homepage or the specific game passed in by the URI
                    UseShellExecute = true
                }) is Process p)
            {
                RobloxRegistryUtil.Register(); // Register immediately
            }
        }

        Thread chatThread = new Thread(() =>
        {
            // Pass isForceRun here to ignore the 3-second start time rule
            Process? robloxGame = WaitForRobloxProcess(120, isForceRun); // If another instance launches while we're waiting, mutex logic will prevent doubles

            if (robloxGame == null && isForceRun)
            {
                var procs = Process.GetProcessesByName("RobloxPlayerBeta");
                if (procs.Length > 0)
                    robloxGame = procs[0];
            }
        });

        chatThread.SetApartmentState(ApartmentState.STA);
        chatThread.Start();
    }
    static Process? WaitForRobloxProcess(int timeoutSeconds, bool ignoreStartTime)
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
