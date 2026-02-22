using System;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Windows.Forms;
using Microsoft.Win32;
using RobloxChatLauncher;
using RobloxChatLauncher.Utils;

class Program
{
    static ChatForm chatForm;
    static ChatKeyboardHandler keyboardHandler;

    static void Main(string[] args)
    {
        // Initial registration is now conditional on bootstrapper detection
        var robloxClient = RobloxLocator.ResolveRobloxPlayer();
        string exePath = Process.GetCurrentProcess().MainModule.FileName; // Path to our launcher executable
        if (robloxClient == null)
        {
            MessageBox.Show("Could not find a valid Roblox client.");
            return;
        }

        Console.WriteLine($"Resolved Roblox client: {robloxClient.Type} ({robloxClient.ExecutablePath})");

        // Only ensure registry control if we resolved a bootstrapper
        // as vanilla Roblox won't take back control of the registry key
        // Prevents redundant checks to the registry on every launch when using the vanilla client
        if (robloxClient.Type == RobloxClientType.Bootstrapper)
            RegisterAsRobloxLauncher();

        // If no URI argument is provided, we assume the launcher is being run directly and just register for the protocol without launching anything
        // i.e., first run to register, then launching a game from the website will pass the URI argument to us to trigger the chat form
        if (args.Length == 0)
        {
            RegisterAsRobloxLauncher();
            Console.WriteLine($"Launcher registered. ({exePath})");
            return;
        }

        string uri = args[0];

        // 1. Launch the Roblox client
        Process.Start(new ProcessStartInfo
        {
            FileName = robloxClient.ExecutablePath,
            Arguments = uri,
            UseShellExecute = true
        });

        // 2. Start a background thread to wait for the actual game engine
        Thread chatThread = new Thread(() =>
        {
            Process robloxGame = WaitForRobloxProcess(60); // Wait up to 60 seconds

            if (robloxGame != null)
            {
                // THE TRIGGER: The game started, meaning the bootstrapper is done.
                // Check and fix registry only if the bootstrapper took control
                // (i.e., overwrote our registry key)
                if (robloxClient.Type == RobloxClientType.Bootstrapper)
                    RegisterAsRobloxLauncher();

                chatForm = new ChatForm(robloxGame);
                keyboardHandler = new ChatKeyboardHandler(chatForm);
                Application.Run(chatForm);
            }
        });

        chatThread.SetApartmentState(ApartmentState.STA);
        chatThread.Start();
    }

    static void RegisterAsRobloxLauncher()
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
        }
        catch(Exception ex)
        {
            MessageBox.Show($"Failed to write registry key: {ex.Message}");
        }
    }

    static Process WaitForRobloxProcess(int timeoutSeconds)
    {
        // Loop every 500ms until the actual game engine process appears
        for (int i = 0; i < timeoutSeconds * 2; i++)
        {
            var processes = Process.GetProcessesByName("RobloxPlayerBeta");
            if (processes.Length > 0)
                return processes[0];
            Thread.Sleep(500);
        }
        return null;
    }
}