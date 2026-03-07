using System.Diagnostics;
using System.Globalization;
using System.Windows.Forms;

using RobloxChatLauncher.Localization;
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
                /// <summary>Opens the external command documentation URL.</summary>
                case "/help":
                case "/?":
                    OpenUrl("https://github.com/AlinaWan/RobloxChatLauncher/tree/main/assets/docs/COMMANDS.md");
                    chatBox.AppendText($"[{Strings.System}]: {Strings.OpeningWebsite}\r\n");
                    return true;

                /// <summary>Displays application metadata including developer credits, build environment, and source code links.</summary>
                case "/about":
                case "/credits":
                    chatBox.AppendText($"{Strings.AboutText}\r\n");
                    return true;

                /// <summary>Triggers an asynchronous restart of the WebSocket Client to refresh the server connection.</summary>
                case "/reconnect":
                case "/rc":
                    await RestartWebSocketAsync(); // Calls RestartWebSocketAsync() in Client.cs
                    return true;

                /// <summary>Echoes the provided arguments back to the chat box using an HTTP communication with the server.</summary>
                /// <param>args: The text to be echoed back.</param>
                case "/echo":
                    await ExecuteEchoRequest(args); // Calls ExecuteEchoRequest(args) in Client.cs
                    return true;

                /// <summary>Clears all text from the chat box.</summary>
                case "/clear":
                case "/cls":
                case "/c":
                    chatBox.Clear();
                    return true;

                /// <summary>Displays the current channel ID in the chat box.</summary>
                case "/id":
                case "/channel":
                    chatBox.AppendText($"[{Strings.System}]: {Strings.CurrentChannelID}: {channelId}\r\n");
                    return true;

                /// <summary>Opens the default web browser to the GitHub issues page for reporting bugs or requesting features.</summary>
                case "/bug":
                case "/issue":
                    OpenUrl("https://github.com/AlinaWan/RobloxChatLauncher/issues/new");
                    chatBox.AppendText($"[{Strings.System}]: {Strings.OpeningWebsite}\r\n");
                    return true;

                /// <summary>Handles muting a user by their username, preventing their messages from appearing in the chat box.</summary>
                /// <param>args: The username of the user to mute.</param>
                case "/mute":
                    return HandleMute(args);

                /// <summary>Handles unmuting a user by their username, allowing their messages to appear in the chat box again.</summary>
                /// <param>args: The username of the user to unmute.</param>
                case "/unmute":
                    return HandleUnmute(args);

                /// <summary>Handles sending a private whisper message to another user.</summary>
                /// <param>args: The username and message in the format "username message".</param>
                case "/whisper":
                case "/w":
                    return await HandleWhisperAsync(args);

                /// <summary>Opens the debug console window.</summary>
                case "/console":
                case "/debug":
                    OpenDebugConsole();
                    return true;

                /// <summary>Closes the debug console window.</summary>
                case "/closeconsole":
                case "/closedebug":
                    CloseDebugConsole();
                    return true;

                /// <summary>Checks for updates on GitHub and, if a new version is available, the application will restart automatically to install the update.</summary>
                /// <param>args: Optional argument "prerelease" to include prerelease versions in the update check.</param>
                case "/update":
                    chatBox.AppendText($"[{Strings.System}]: {Strings.CheckingForUpdates}\r\n");
                    try
                    {
                        // If the user types "/update prerelease", check for prereleases
                        bool includePrerelease = args.ToLower().Contains("prerelease");

                        // We use Task.Run so the UI doesn't freeze while downloading
                        await Task.Run(async () => {
                            await UpdateService.CheckAndDownloadUpdate(includePrerelease, (status) => {
                                // This invokes back to the UI thread to update the chatBox safely
                                this.Invoke(new Action(() => chatBox.AppendText($"[{Strings.System}]: {status}\r\n")));
                            });
                        });
                    }
                    catch (Exception ex)
                    {
                        chatBox.AppendText($"[{Strings.Error}]: {string.Format(Strings.UpdateCheckFailed, ex.Message)}\r\n");
                    }
                    return true;

                /// <summary>Initiates the Roblox account verification process by fetching a unique code for the given Roblox username.</summary>
                /// <param>args: The Roblox username to verify.</param>
                case "/verify":
                    if (string.IsNullOrWhiteSpace(args))
                    {
                        chatBox.AppendText($"[{Strings.System}]: {Strings.UsageVerify}\r\n");
                    }
                    else
                    {
                        chatBox.AppendText($"[{Strings.System}]: {string.Format(Strings.FetchingCode, args)}\r\n");
                        var verifyResult = await _verifyService.StartVerification(args);
                        _pendingRobloxId = verifyResult.RobloxId;

                        chatBox.AppendText($"{string.Format(Strings.VerifyStepsText, verifyResult.Code)}\r\n");
                    }
                    return true;

                /// <summary>Confirms the Roblox account verification by checking the previously generated code against the user's Roblox profile, and if successful, links the account and refreshes the connection to update the username.</summary>
                case "/confirm":
                    if (_pendingRobloxId == 0)
                    {
                        chatBox.AppendText($"[{Strings.System}]: {Strings.RunVerifyFirst}\r\n");
                        return true;
                    }

                    var confirmResult = await _verifyService.ConfirmVerification(_pendingRobloxId);

                    switch (confirmResult)
                    {
                        case VerificationResult.Success:
                            chatBox.AppendText($"[{Strings.System}]: {Strings.AccountLinked}\r\n");
                            // Trigger a reconnection to update their name to their verified username immediately
                            await RestartWebSocketAsync();
                            break;

                        case VerificationResult.CodeNotFound:
                            chatBox.AppendText($"[{Strings.System}]: {Strings.CodeNotFound}\r\n");
                            break;

                        case VerificationResult.HardwareIdFailed:
                            chatBox.AppendText($"[{Strings.System}]: {Strings.HardwareIdFailed}\r\n");
                            break;

                        default:
                            chatBox.AppendText($"[{Strings.System}]: {Strings.VerificationFailed}\r\n");
                            break;
                    }
                    return true;

                /// <summary>Unverifies the user's Roblox account by clearing local verification data and attempting to unlink the account on the server, then refreshes the connection to update the username to "Guest".</summary>
                case "/unverify":
                case "/logout":
                    chatBox.AppendText($"[{Strings.System}]: {Strings.UnlinkingAccount}\r\n");
                    bool unverified = await _verifyService.Unverify();

                    if (unverified)
                    {
                        chatBox.AppendText($"[{Strings.System}]: {Strings.UnverifiedSuccess}\r\n");
                    }
                    else
                    {
                        // Even if the server call fails, we cleared local settings, 
                        // so the user is effectively a guest now anyway.
                        chatBox.AppendText($"[{Strings.System}]: {Strings.LocalDataCleared}\r\n");
                    }

                    // Trigger a reconnection to update their name to "Guest" immediately
                    await RestartWebSocketAsync();
                    return true;

                /// <summary>Sends an emote mail to the server to request the specified emote be performed in-game. This command only works if the user is verified and the Roblox game has Roblox Chat Launcher integration enabled.</summary>
                /// <param>args: The name of the emote to perform.</param>
                case "/emote":
                case "/e":
                    if (string.IsNullOrWhiteSpace(args))
                    {
                        chatBox.AppendText($"[{Strings.System}]: {Strings.UsageEmote}\r\n");
                    }
                    else
                    {
                        await SendEmoteMailAsync(args.Trim());
                    }
                    return true;

                default:
                    chatBox.AppendText($"[{Strings.System}]: {string.Format(Strings.UnknownCommand, command)}\r\n");
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
                chatBox.AppendText($"[{Strings.Error}]: {string.Format(Strings.CouldNotOpenLink, ex.Message)}\r\n");
            }
        }
    }
}
