using System;
using System.Drawing;
using System.Windows.Forms;
using System.Runtime.InteropServices;
using System.Text;
using System.Net.Http;
using System.Net.WebSockets;
using System.Threading;
using System.Threading.Tasks;
using System.ComponentModel;
using System.Diagnostics;
using Gma.System.MouseKeyHook;
using Newtonsoft.Json;

using RobloxChatLauncher.Utils;

namespace RobloxChatLauncher
{
    public partial class ChatForm : Form
    {
        // Collection of muted users (case-insensitive)
        private System.Collections.Generic.HashSet<string> mutedUsers = new System.Collections.Generic.HashSet<string>(StringComparer.OrdinalIgnoreCase);

        // WebSocket connection and message handling
        private async Task ConnectWebSocket(CancellationToken ct)
        {
            int maxRetries = 12;
            int delayMilliseconds = 5000;

            for (int i = 1; i <= maxRetries; i++)
            {
                // EXIT if a newer connection request has started
                if (ct.IsCancellationRequested)
                    return;

                try
                {
                    wsClient?.Dispose();
                    wsClient = new ClientWebSocket();

                    this.Invoke((MethodInvoker)(() => chatBox.AppendText($"[System]: Connecting to server {channelId}...\r\n")));

                    // Use the passed 'ct' token here
                    await wsClient.ConnectAsync(new Uri($"wss://{BASE_URL}/"), ct);

                    var joinPayload = new
                    {
                        type = "join",
                        channelId = this.channelId
                    };
                    string json = JsonConvert.SerializeObject(joinPayload);

                    await wsClient.SendAsync(new ArraySegment<byte>(Encoding.UTF8.GetBytes(json)),
                        WebSocketMessageType.Text, true, ct);

                    this.Invoke((MethodInvoker)(() => chatBox.AppendText($"[System]: Connected successfully!\r\n")));

                    _ = Task.Run(() => ReceiveLoop(ct), ct);
                    return;
                }
                catch (Exception ex)
                {
                    if (ct.IsCancellationRequested)
                        return;

                    if (i == maxRetries)
                    {
                        this.Invoke((MethodInvoker)(() => chatBox.AppendText($"[System]: Connection failed: {ex.Message}\r\n")));
                    }
                    else
                    {
                        // Task.Delay must also use the token so it wakes up immediately 
                        // if a new server is detected
                        await Task.Delay(delayMilliseconds, ct);
                    }
                }
            }
        }

        private async Task RestartWebSocketAsync()
        {
            // Handle WebSocket cleanup and new connection
            wsCts?.Cancel();
            wsCts?.Dispose();
            wsCts = new CancellationTokenSource();

            await ConnectWebSocket(wsCts.Token);
        }

        private async Task ReceiveLoop(CancellationToken ct)
        {
            var buffer = new byte[4096];
            try
            {
                while (wsClient.State == WebSocketState.Open && !ct.IsCancellationRequested)
                {
                    var result = await wsClient.ReceiveAsync(
                        new ArraySegment<byte>(buffer),
                        ct // Use the passed token
                    );

                    if (result.MessageType == WebSocketMessageType.Close)
                    {
                        // server closed connection (often unclean on PaaS)
                        _ = ConnectWebSocket(ct); // ct passed here
                        break;
                    }

                    var message = Encoding.UTF8.GetString(buffer, 0, result.Count);
                    dynamic data = JsonConvert.DeserializeObject(message);

                    this.Invoke((MethodInvoker)delegate
                    {
                        // Normal chat message
                        if (data.type == "message")
                        {
                            string sender = (string)data.sender;
                            string text = (string)data.text;

                            string rawName = sender;

                            // Handle whisper message formats to extract the actual speaker name for mute checking
                            // If it's an incoming whisper: "From Guest 12345"
                            if (sender.StartsWith("From "))
                            {
                                rawName = sender.Substring(5); // Remove "From "
                            }
                            // If it's an outgoing whisper: "To Guest 12345"
                            else if (sender.StartsWith("To "))
                            {
                                rawName = sender.Substring(3); // Remove "To "
                            }

                            // Now check the cleaned name against the mute list
                            if (!mutedUsers.Contains(rawName))
                            {
                                chatBox.AppendText($"[{sender}]: {text}\r\n");
                            }
                        }
                        // Rejection handling
                        else if (data.status == "rejected")
                        {
                            string reason = data.reason;
                            string messageText;

                            switch (reason)
                            {
                                case "moderation":
                                    messageText = "Your message was not sent as it violates community guidelines.";
                                    break;
                                case "queue_full":
                                    messageText = "Your message was rejected because the server queue is full. Please try again shortly.";
                                    break;
                                case "api_error":
                                    messageText = "Your message could not be processed due to a server error. Please try again.";
                                    break;
                                default:
                                    messageText = "Your message was not sent due to unknown reasons.";
                                    break;
                            }

                            chatBox.AppendText($"[System]: {messageText}\r\n");
                        }
                    });
                }
            }
            catch (OperationCanceledException) { }
            catch (Exception ex)
            {
                chatBox.AppendText($"[System]: WS Receive Error: {ex.Message}\r\n");
            }
        }
        public async Task Send()
        {
            if (string.IsNullOrWhiteSpace(rawInputText))
            {
                ExitChatUI();
                return;
            }

            string userMessage = rawInputText;
            ExitChatUI(); // Helper to reset targetOpacity/SyncInput

            // 1. Check if it's a command first
            bool isCommand = await HandleCommands(userMessage);

            // 2. If not a command, send normally via WebSocket
            if (!isCommand)
            {
                await SendWebSocketMessage(userMessage);
            }
        }

        // Small helper to keep Send() tidy
        private void ExitChatUI()
        {
            rawInputText = "";
            isChatting = false;
            targetOpacity = chatOffOpacity;
            SyncInput();
        }

        // Echo Logic
        private async Task ExecuteEchoRequest(string userMessage)
        {
            // chatBox.AppendText($"You: {userMessage}\r\n");
            try
            {
                // 2. Network Call
                var content = new StringContent(userMessage, Encoding.UTF8, "text/plain");
                // PaaS echo server for POC demo testing
                var response = await client.PostAsync($"https://{BASE_URL}/echo", content);

                if (response.IsSuccessStatusCode)
                {
                    string echoResponse = await response.Content.ReadAsStringAsync();

                    this.Invoke((MethodInvoker)delegate
                    {
                        chatBox.AppendText($"[Server]: {echoResponse} (Only you can see this message.)\r\n");
                    });
                }
                else if (response.StatusCode == System.Net.HttpStatusCode.Forbidden)
                {
                    var json = await response.Content.ReadAsStringAsync();
                    dynamic data = Newtonsoft.Json.JsonConvert.DeserializeObject(json);
                    string reason = data?.reason;

                    string messageText;
                    switch (reason)
                    {
                        case "moderation":
                            messageText = "Your message was not sent as it violates community guidelines.";
                            break;
                        case "queue_full":
                            messageText = "Your message was rejected because the server queue is full. Please try again shortly.";
                            break;
                        case "api_error":
                            messageText = "Your message could not be processed due to a server error. Please try again.";
                            break;
                        default:
                            messageText = "Your message was not sent due to unknown reasons.";
                            break;
                    }

                    this.Invoke((MethodInvoker)delegate
                    {
                        chatBox.AppendText(messageText + "\r\n");
                    });
                }
            }
            // 3. Catch Timeout Specifically
            catch (TaskCanceledException)
            {
                this.Invoke((MethodInvoker)delegate
                {
                    chatBox.AppendText("[System]: Request timed out. (Render server may be waking up)\r\n");
                });
            }
            // 4. Catch General Errors (DNS, No Internet, etc.)
            catch (Exception ex)
            {
                this.Invoke((MethodInvoker)delegate
                {
                    chatBox.AppendText($"[System]: Connection error: {ex.Message}\r\n");
                });
            }
            finally
            {
                // Ensure the chat always scrolls to the bottom and hides caret
                this.Invoke((MethodInvoker)delegate
                {
                    chatBox.SelectionStart = chatBox.Text.Length;
                    chatBox.ScrollToCaret();
                    NativeMethods.HideCaret(chatBox.Handle);
                });
            }
        }

        // WebSocket Sender
        private async Task SendWebSocketMessage(string text)
        {
            // Don't append the user's own message here; the server will broadcast it back
            if (wsClient == null || wsClient.State != WebSocketState.Open)
            {
                chatBox.AppendText("[System]: WebSocket not connected. Use '/rc' or '/reconnect' to connect to server.\r\n");
                return;
            }

            try
            {
                var payload = new
                {
                    type = "message",
                    text = text
                };
                string json = JsonConvert.SerializeObject(payload);
                byte[] bytes = Encoding.UTF8.GetBytes(json);

                await wsClient.SendAsync(new ArraySegment<byte>(bytes),
                    WebSocketMessageType.Text, true, wsCts.Token);

            }
            catch (TaskCanceledException)
            {
                chatBox.AppendText("[System]: WS send timed out. (Render server may be waking up)\r\n");
            }
            catch (Exception ex)
            {
                chatBox.AppendText($"[System]: WS Send Error: {ex.Message}\r\n");
            }
        }

        private async Task SendWhisperWebSocket(string target, string text)
        {
            if (wsClient?.State != WebSocketState.Open)
                return;

            var payload = new
            {
                type = "whisper",
                target = target,
                text = text
            };
            string json = JsonConvert.SerializeObject(payload);
            byte[] bytes = Encoding.UTF8.GetBytes(json);

            await wsClient.SendAsync(new ArraySegment<byte>(bytes), WebSocketMessageType.Text, true, wsCts.Token);

            // DO NOT chatBox.AppendText here anymore. 
            // Wait for the server to send the "To {target}" message back.
        }

        // Commented out because it will always force a Global
        // connection before the JobId is found
        /*
        protected override void OnShown(EventArgs e)
        {
            base.OnShown(e);
            _ = ConnectWebSocket(wsCts.Token);
        }
        */

        protected override void OnFormClosed(FormClosedEventArgs e)
        {
            // Only dispose if RobloxProcess_Exited hasn't done it yet
            _robloxService?.Dispose();
            _robloxService = null;

            // Standard WebSocket cleanup
            try
            {
                wsCts?.Cancel();
            }
            catch (ObjectDisposedException) { }

            wsCts?.Dispose();
            wsClient?.Dispose();

            if (winEventHook != IntPtr.Zero)
            {
                NativeMethods.UnhookWinEvent(winEventHook);
                winEventHook = IntPtr.Zero;
            }

            base.OnFormClosed(e);
        }
    }
}
