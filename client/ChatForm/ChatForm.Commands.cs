using System.Diagnostics;
using System.Globalization;
using System.Windows.Forms;

using RobloxChatLauncher.Localization;
using RobloxChatLauncher.Services;
using RobloxChatLauncher.Utils;

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
                    RichChatBox.AppendSystemMessage(chatBox, Strings.OpeningWebsite);
                    return true;

                /// <summary>Displays application metadata including developer credits, build environment, and source code links.</summary>
                case "/about":
                case "/credits":
                    RichChatBox.AppendText(chatBox, Strings.AboutText);
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
                    RichChatBox.AppendSystemMessage(chatBox, $"{Strings.CurrentChannelID}: {channelId}");
                    return true;

                /// <summary>Opens the default web browser to the GitHub issues page for reporting bugs or requesting features.</summary>
                case "/bug":
                case "/issue":
                    OpenUrl("https://github.com/AlinaWan/RobloxChatLauncher/issues/new");
                    RichChatBox.AppendSystemMessage(chatBox, Strings.OpeningWebsite);
                    return true;

                /// <summary>Handles muting a user by their username, preventing their messages from appearing in the chat box.</summary>
                /// <param>args: The username of the user to mute.</param>
                case "/mute":
                    return HandleMute(args);

                /// <summary>Sets or displays the local message filter preference.</summary>
                /// <param>args: One of strict, default, or relaxed. Empty args displays current setting.</param>
                case "/filter":
                    return HandleFilterPreferenceCommand(args);

                /// <summary>Handles unmuting a user by their username, allowing their messages to appear in the chat box again.</summary>
                /// <param>args: The username of the user to unmute.</param>
                case "/unmute":
                    return HandleUnmute(args);

                /// <summary>Handles sending a private whisper message to another user.</summary>
                /// <param>args: The username and message in the format "username message".</param>
                case "/whisper":
                case "/w":
                    return await HandleWhisperAsync(args);

                /// <summary>Opens or closes the debug console window.</summary>
                case "/console":
                case "/debug":
                    HandleDebugConsole();
                    return true;

                /// <summary>Checks for updates on GitHub and, if a new version is available, the application will restart automatically to install the update.</summary>
                /// <param>args: Optional argument "prerelease" to include prerelease versions in the update check.</param>
                case "/update":
                    RichChatBox.AppendSystemMessage(chatBox, Strings.CheckingForUpdates);
                    try
                    {
                        // If the user types "/update prerelease", check for prereleases
                        bool includePrerelease = args.ToLower().Contains("prerelease");

                        // We use Task.Run so the UI doesn't freeze while downloading
                        await Task.Run(async () => {
                            await UpdateService.CheckAndDownloadUpdate(includePrerelease, (status) => {
                                // This invokes back to the UI thread to update the chatBox safely
                                this.Invoke(new Action(() => RichChatBox.AppendSystemMessage(chatBox, status)));
                            });
                        });
                    }
                    catch (Exception ex)
                    {
                        RichChatBox.AppendSystemMessage(chatBox, string.Format(Strings.UpdateCheckFailed, ex.Message));
                    }
                    return true;

                /// <summary>Checks the server for an existing link based on this device and logs in if found.</summary>
                case "/login":
                    RichChatBox.AppendSystemMessage(chatBox, Strings.AttemptingLogin);
                    bool loginSuccess = await _verifyService.Login();
                    if (loginSuccess)
                    {
                        RichChatBox.AppendSystemMessage(chatBox, Strings.LoginSuccess);
                        await RestartWebSocketAsync();
                    }
                    else
                    {
                        RichChatBox.AppendSystemMessage(chatBox, Strings.NoAccountLinked);
                    }
                    return true;

                /// <summary>Clears local verification status only. Does not delete server data.</summary>
                case "/logout":
                    _verifyService.Logout(); // Calls the local-only logout
                    RichChatBox.AppendSystemMessage(chatBox, Strings.LoggedOut);
                    await RestartWebSocketAsync();
                    return true;

                /// <summary>Initiates the Roblox account verification process by fetching a unique code for the given Roblox username.</summary>
                /// <param>args: The Roblox username to verify.</param>
                case "/verify":
                    if (string.IsNullOrWhiteSpace(args))
                    {
                        RichChatBox.AppendSystemMessage(chatBox, Strings.UsageVerify);
                    }
                    else
                    {
                        RichChatBox.AppendSystemMessage(chatBox, string.Format(Strings.FetchingCode, args));
                        var verifyResult = await _verifyService.StartVerification(args);
                        _pendingRobloxId = verifyResult.RobloxId;

                        RichChatBox.AppendText(chatBox, string.Format(Strings.VerifyStepsText, verifyResult.Code));
                    }
                    return true;

                /// <summary>Confirms the Roblox account verification by checking the previously generated code against the user's Roblox profile, and if successful, links the account and refreshes the connection to update the username.</summary>
                case "/confirm":
                    if (_pendingRobloxId == 0)
                    {
                        RichChatBox.AppendSystemMessage(chatBox, Strings.RunVerifyFirst);
                        return true;
                    }

                    var confirmResult = await _verifyService.ConfirmVerification(_pendingRobloxId);

                    switch (confirmResult)
                    {
                        case VerificationResult.Success:
                            RichChatBox.AppendSystemMessage(chatBox, Strings.AccountLinked);
                            // Trigger a reconnection to update their name to their verified username immediately
                            await RestartWebSocketAsync();
                            break;

                        case VerificationResult.CodeNotFound:
                            RichChatBox.AppendSystemMessage(chatBox, Strings.CodeNotFound);
                            break;

                        case VerificationResult.HardwareIdFailed:
                            RichChatBox.AppendSystemMessage(chatBox, Strings.HardwareIdFailed);
                            break;

                        default:
                            RichChatBox.AppendSystemMessage(chatBox, Strings.VerificationFailed);
                            break;
                    }
                    return true;

                /// <summary>Unverifies the user's Roblox account by clearing local verification data and attempting to unlink the account on the server, then refreshes the connection to update the username to "Guest".</summary>
                case "/unverify":
                    RichChatBox.AppendSystemMessage(chatBox, Strings.UnlinkingAccount);
                    bool unverified = await _verifyService.Unverify();

                    if (unverified)
                    {
                        RichChatBox.AppendSystemMessage(chatBox, Strings.UnverifiedSuccess);
                    }
                    else
                    {
                        RichChatBox.AppendSystemMessage(chatBox, Strings.LocalDataCleared);
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
                        RichChatBox.AppendSystemMessage(chatBox, Strings.UsageEmote);
                    }
                    else
                    {
                        await SendEmoteMailAsync(args.Trim());
                    }
                    return true;

                default:
                    RichChatBox.AppendSystemMessage(chatBox, string.Format(Strings.UnknownCommand, command));
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
                RichChatBox.AppendSystemMessage(chatBox, string.Format(Strings.CouldNotOpenLink, ex.Message));
            }
        }
    }
}
