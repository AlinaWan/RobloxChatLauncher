using System.Diagnostics;
using System.Windows.Forms;

using RobloxChatLauncher.Services;

namespace RobloxChatLauncher
{
    public partial class ChatForm : Form
    {
        /// <summary>
        /// Orchestrates command routing. 
        /// This gets called from the Send() method.
        /// </summary>
        private async Task<bool> HandleCommands(string input)
        {
            if (string.IsNullOrWhiteSpace(input) || !input.StartsWith("/"))
                return false;

            string[] parts = input.Split(' ', 2);
            string command = parts[0].ToLower();
            string args = parts.Length > 1 ? parts[1] : "";

            switch (command)
            {
                case "/help":
                case "/?":
                    OpenUrl("https://github.com/AlinaWan/RobloxChatLauncher/tree/main/assets/docs/COMMANDS.md");
                    chatBox.AppendText("[System]: Opening website...\r\n");
                    return true;

                case "/about":
                case "/credits":
                    chatBox.AppendText($"About Roblox Chat Launcher:\r\n" +
                                       $"Made with ❤︎ by Riri.\r\n" +
                                       $"Developed in VS 2026 🎀 Built with .NET / WinForms.\r\n" +
                                       $"Server written in Node.js 🌸 Hosted on Render.com.\r\n" +
                                       $"Source: https://github.com/AlinaWan/RobloxChatLauncher\r\n" +
                                       $"And of course, credits to you 💖\r\n");
                    return true;

                case "/reconnect":
                case "/rc":
                    await RestartWebSocketAsync(); // Calls RestartWebSocketAsync() in Client.cs
                    return true;

                case "/echo":
                    await ExecuteEchoRequest(args); // Calls ExecuteEchoRequest(args) in Client.cs
                    return true;

                case "/clear":
                case "/cls":
                case "/c":
                    chatBox.Clear();
                    return true;

                case "/id":
                case "/channel":
                    chatBox.AppendText($"[System]: Current Channel ID: {channelId}\r\n");
                    return true;

                case "/bug":
                case "/issue":
                    OpenUrl("https://github.com/AlinaWan/RobloxChatLauncher/issues/new");
                    chatBox.AppendText("[System]: Opening website...\r\n");
                    return true;

                case "/mute":
                    return HandleMute(args);

                case "/unmute":
                    return HandleUnmute(args);

                case "/whisper":
                case "/w":
                    return await HandleWhisperAsync(args);

                case "/console":
                case "/debug":
                    OpenDebugConsole();
                    return true;

                case "/closeconsole":
                case "/closedebug":
                    CloseDebugConsole();
                    return true;

                case "/verify":
                    if (string.IsNullOrWhiteSpace(args))
                    {
                        chatBox.AppendText("[System]: Usage: /verify <RobloxUsername>\r\n");
                    }
                    else
                    {
                        chatBox.AppendText($"[System]: Fetching code for {args}...\r\n");
                        var result = await _verifyService.StartVerification(args);
                        _pendingRobloxId = result.RobloxId;

                        chatBox.AppendText($"1. Copy this code: {result.Code}\r\n");
                        chatBox.AppendText($"2. Paste it into your Roblox Profile 'About' section.\r\n");
                        chatBox.AppendText($"3. Type /confirm to finish.\r\n");
                    }
                    return true;

                case "/confirm":
                    if (await _verifyService.ConfirmVerification(_pendingRobloxId))
                    {
                        chatBox.AppendText("[System]: 🎀 Account linked successfully!\r\n");
                    }
                    else
                    {
                        chatBox.AppendText("[System]: ❌ Code not found. Please check your profile.\r\n");
                    }
                    return true;

                case "/unverify":
                case "/logout":
                    chatBox.AppendText("[System]: Unlinking your Roblox account and clearing local data...\r\n");
                    bool unverified = await _verifyService.Unverify();

                    if (unverified)
                    {
                        chatBox.AppendText("[System]: 🗑️ Successfully unverified. You are now a Guest.\r\n");
                    }
                    else
                    {
                        // Even if the server call fails, we cleared local settings, 
                        // so the user is effectively a guest now anyway.
                        chatBox.AppendText("[System]: Local data cleared. (Server sync may have failed).\r\n");
                    }

                    // Optional: Trigger a reconnection to update their name to "Guest" immediately
                    await RestartWebSocketAsync();
                    return true;

                default:
                    chatBox.AppendText($"[System]: Unknown command '{command}'. Use '/?' or '/help' for a list of commands.\r\n");
                    return true; // Return true so it doesn't send the bad command to the server
            }
        }
        private void OpenUrl(string url)
        {
            try
            {
                Process.Start(new ProcessStartInfo
                {
                    FileName = url,
                    UseShellExecute = true // This is required for URLs
                });
            }
            catch (Exception ex)
            {
                chatBox.AppendText($"[Error]: Could not open link. {ex.Message}\r\n");
            }
        }
    }
}
