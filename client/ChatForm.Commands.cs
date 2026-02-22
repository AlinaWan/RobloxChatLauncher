using System;
using System.Diagnostics;
using System.Threading.Tasks;
using System.Windows.Forms;

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
									   $"Developed in VS 2022 🎀 Built with .NET / WinForms.\r\n" +
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
                    if (string.IsNullOrWhiteSpace(args))
                    {
                        chatBox.AppendText("[System]: Usage: /mute <speaker>\r\n");
                    }
                    else
                    {
                        mutedUsers.Add(args.Trim());
                        chatBox.AppendText($"[System]: Speaker '{args.Trim()}' has been muted.\r\n");
                    }
                    return true;

                case "/unmute":
                    if (string.IsNullOrWhiteSpace(args))
                    {
                        chatBox.AppendText("[System]: Usage: /unmute <speaker>\r\n");
                    }
                    else
                    {
                        if (mutedUsers.Remove(args.Trim()))
                            chatBox.AppendText($"[System]: Speaker '{args.Trim()}' has been unmuted.\r\n");
                        else
                            chatBox.AppendText($"[System]: Speaker '{args.Trim()}' was not muted.\r\n");
                    }
                    return true;

                case "/w":
                case "/whisper":
                    string target = "";
                    string msg = "";

                    if (args.StartsWith("\""))
                    {
                        // Find the closing quote
                        int endQuoteIndex = args.IndexOf("\"", 1);
                        if (endQuoteIndex != -1)
                        {
                            target = args.Substring(1, endQuoteIndex - 1);
                            msg = args.Substring(endQuoteIndex + 1).Trim();
                        }
                    }
                    else
                    {
                        // No quotes? Fallback to the first space
                        string[] whisperParts = args.Split(' ', 2);
                        if (whisperParts.Length == 2)
                        {
                            target = whisperParts[0];
                            msg = whisperParts[1];
                        }
                    }

                    if (string.IsNullOrEmpty(target) || string.IsNullOrEmpty(msg))
                    {
                        chatBox.AppendText("[System]: Usage: /w \"<speaker 12345>\" message or /w <speaker> message\r\n");
                    }
                    else
                    {
                        await SendWhisperWebSocket(target, msg);
                    }
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
