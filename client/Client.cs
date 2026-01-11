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

using Utils;

namespace ChatLauncherApp
{
    public partial class ChatForm : Form
    {
        // WebSocket connection and message handling
        private async Task ConnectWebSocket()
        {
            // The Render server may take time to wake up, so we implement retries
            int maxRetries = 12;
            int delayMilliseconds = 5000; // seconds between tries
            // Goal: Retry for up to 1 minute as
            // that's how long Render free-tier usually takes to wake up

            for (int i = 1; i <= maxRetries; i++)
            {
                try
                {
                    // Console.WriteLine($"[DEBUG] Attempt {i}/{maxRetries} - starting connection...");

                    // Clean up old client if it exists
                    wsCts?.Cancel();
                    wsClient?.Dispose();
                    wsClient = new ClientWebSocket();
                    wsCts = new CancellationTokenSource();

                    // Console.WriteLine("[DEBUG] ClientWebSocket created");

                    this.Invoke((MethodInvoker)(() => chatBox.AppendText($"[System]: Connecting to server...\r\n")));

                    // Console.WriteLine($"[DEBUG] Connecting to wss://{BASE_URL}/ ...");
                    // Try to connect
                    await wsClient.ConnectAsync(new Uri($"wss://{BASE_URL}/"), wsCts.Token);
                    // Console.WriteLine("[DEBUG] Connected to WebSocket successfully");

                    // If we reach here, connection was successful
                    var joinPayload = new
                    {
                        type = "join",
                        channelId = this.channelId
                    };
                    string json = JsonConvert.SerializeObject(joinPayload);
                    await wsClient.SendAsync(new ArraySegment<byte>(Encoding.UTF8.GetBytes(json)),
                        WebSocketMessageType.Text, true, wsCts.Token);

                    this.Invoke((MethodInvoker)(() => chatBox.AppendText("[System]: Connected successfully!\r\n")));

                    _ = Task.Run(ReceiveLoop);
                    return; // Exit the method successfully
                }
                catch (Exception ex)
                {
                    // Log full exception details to see inner exceptions and stack trace
                    // Console.WriteLine($"[DEBUG] Attempt {i} failed:");
                    // Console.WriteLine(ex.ToString());

                    if (i == maxRetries)
                    {
                        this.Invoke((MethodInvoker)(() => chatBox.AppendText($"[System]: Connection failed: {ex.Message}\r\n")));
                    }
                    else
                    {
                        // Wait before trying again
                        await Task.Delay(delayMilliseconds);
                    }
                }
            }
        }

        private async Task ReceiveLoop()
        {
            var buffer = new byte[4096];

            try
            {
                while (wsClient.State == WebSocketState.Open)
                {
                    var result = await wsClient.ReceiveAsync(
                        new ArraySegment<byte>(buffer),
                        wsCts.Token
                    );

                    if (result.MessageType == WebSocketMessageType.Close)
                    {
                        // server closed connection (often unclean on PaaS)
                        _ = ConnectWebSocket();
                        break;
                    }

                    var message = Encoding.UTF8.GetString(buffer, 0, result.Count);
                    dynamic data = JsonConvert.DeserializeObject(message);

                    this.Invoke((MethodInvoker)delegate
                    {
                        // Normal chat message
                        if (data.type == "message")
                        {
                            chatBox.AppendText($"[{data.sender}]: {data.text}\r\n");
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
            // If empty, just exit chat mode and return
            if (string.IsNullOrWhiteSpace(rawInputText))
            {
                isChatting = false;
                rawInputText = "";
                targetOpacity = chatOffOpacity;
                SyncInput();
                return;
            }

            string userMessage = rawInputText;

            // 1. Immediate UI Feedback
            rawInputText = "";
            isChatting = false;
            targetOpacity = chatOffOpacity;
            SyncInput();

            // --- ROUTING LOGIC ---
            // Check if it's an echo command
            if (userMessage.StartsWith("/echo ", StringComparison.OrdinalIgnoreCase))
            {
                // Extract the text after "/echo "
                string echoPayload = userMessage.Substring(6);
                await ExecuteEchoRequest(echoPayload);
            }
            else
            {
                // Default: Send via WebSocket for the channel broadcast
                await SendWebSocketMessage(userMessage);
            }
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
                chatBox.AppendText("[System]: WebSocket not connected. Try /echo for HTTP mode.\r\n");
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

        protected override void OnShown(EventArgs e)
        {
            base.OnShown(e);
            _ = ConnectWebSocket();
        }

        protected override void OnFormClosed(FormClosedEventArgs e)
        {
            // WebSocket cleanup
            wsCts?.Cancel();
            wsClient?.Dispose();

            // Hook cleanup
            if (winEventHook != IntPtr.Zero)
            {
                NativeMethods.UnhookWinEvent(winEventHook);
                winEventHook = IntPtr.Zero;
            }

            base.OnFormClosed(e);
        }
    }
}
