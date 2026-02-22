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
					return HandleMute(args);

                case "/unmute":
                    return HandleUnmute(args);

                case "/w":
                case "/whisper":
                    return await HandleWhisperAsync(args);

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
