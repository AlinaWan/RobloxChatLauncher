# Command Documentation

| Command | Aliases | Action / Function |
| :--- | :--- | :--- |
| **/help** | `/?` | ```OpenUrl("https://github.com/AlinaWan/RobloxChatLauncher/tree/main/assets/docs/COMMANDS.md"); chatBox.AppendText("[System]: Opening website...\r\n")``` |
| **/about** | `/credits` | ```chatBox.AppendText($"About Roblox Chat Launcher:\r\n" + $"Made with ❤︎ by Riri.\r\n" + $"Developed in VS 2022 🎀 Built with .NET / WinForms.\r\n" + $"Server written in Node.js 🌸 Hosted on Render.com.\r\n" + $"Source: https://github.com/AlinaWan/RobloxChatLauncher\r\n" + $"And of course, credits to you 💖\r\n")``` |
| **/reconnect** | `/rc` | ```RestartWebSocketAsync(); // Calls RestartWebSocketAsync(); in Client.cs``` |
| **/echo** | None | ```ExecuteEchoRequest(args); // Calls ExecuteEchoRequest(args) in Client.cs``` |
| **/clear** | `/cls`, `/c` | ```chatBox.Clear()``` |
| **/id** | `/channel` | ```chatBox.AppendText($"[System]: Current Channel ID: {channelId}\r\n")``` |
| **/bug** | `/issue` | ```OpenUrl("https://github.com/AlinaWan/RobloxChatLauncher/issues/new"); chatBox.AppendText("[System]: Opening website...\r\n")``` |
