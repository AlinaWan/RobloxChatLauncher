using System;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using Microsoft.Win32;
using System.Web;
using System.Runtime.InteropServices;
using System.Drawing;
using System.Text;
using System.ComponentModel;
using System.Net.Http;

using Gma.System.MouseKeyHook;
using Newtonsoft.Json;

class Program
{
    static Process robloxProcess;
    static ChatForm chatForm;
    static ChatKeyboardHandler keyboardHandler;

    static void Main(string[] args)
    {
        RegisterAsRobloxLauncher();

        if (args.Length == 0)
        {
            Console.WriteLine("Launcher registered. Waiting for roblox-player launch.");
            return;
        }

        string uri = args[0];

        // Console.WriteLine("Roblox Launch Detected");
        // DEBUG: Print full URI for inspection
        // Console.WriteLine(uri);
        // DEBUG: Keep the console open for inspection
        // Console.WriteLine("Keeping console open for inspection. Press Enter to continue launching Roblox...");
        // Console.ReadLine();

        string robloxExe = ResolveRobloxPlayerPath();
        if (robloxExe == null)
        {
            Console.WriteLine("RobloxPlayerBeta.exe not found.");
            return;
        }

        robloxProcess = Process.Start(new ProcessStartInfo
        {
            FileName = robloxExe,
            Arguments = uri,
            UseShellExecute = false
        });

        Thread chatThread = new Thread(() =>
        {
            chatForm = new ChatForm(robloxProcess);
            keyboardHandler = new ChatKeyboardHandler(chatForm);
            Application.Run(chatForm);
        });

        chatThread.SetApartmentState(ApartmentState.STA);
        chatThread.Start();
    }

    /*
    static string ResolveRobloxPlayerPath()
    {
        string versionsDir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Roblox", "Versions");

        if (!Directory.Exists(versionsDir)) return null;

        foreach (var dir in Directory.GetDirectories(versionsDir))
        {
            string exe = Path.Combine(dir, "RobloxPlayerBeta.exe");
            if (File.Exists(exe)) return exe;
        }

        return null;
    }
    */

    static string ResolveRobloxPlayerPath()
    {
        using var key = Registry.ClassesRoot.OpenSubKey(@"roblox-player\shell\open\command");
        if (key == null) return null;
    
        var versionFolder = key.GetValue("version") as string;
        if (string.IsNullOrEmpty(versionFolder)) return null;
    
        string exePath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Roblox", "Versions", versionFolder, "RobloxPlayerBeta.exe");
    
        return File.Exists(exePath) ? exePath : null;
    }

    static void RegisterAsRobloxLauncher()
    {
        string exePath = Process.GetCurrentProcess().MainModule.FileName;
        using var key = Registry.ClassesRoot.CreateSubKey(@"roblox-player\shell\open\command");
        key.SetValue("", $"\"{exePath}\" \"%1\"");
    }
}

// --------------------------------------------------
// Native Win32 helpers
// --------------------------------------------------
static class NativeMethods
{
    [DllImport("user32.dll")]
    public static extern bool IsIconic(IntPtr hWnd);

    [DllImport("user32.dll")]
    public static extern bool HideCaret(IntPtr hWnd);

    [DllImport("user32.dll")]
    public static extern int ToUnicodeEx(
        uint wVirtKey,
        uint wScanCode,
        byte[] lpKeyState,
        [Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder pwszBuff,
        int cchBuff,
        uint wFlags,
        IntPtr dwhkl);

    [DllImport("user32.dll")]
    public static extern bool GetKeyboardState(byte[] lpKeyState);

    [DllImport("user32.dll")]
    public static extern IntPtr GetKeyboardLayout(uint idThread);
}

// --------------------------------------------------
// Custom input box (fake caret, custom paint)
// --------------------------------------------------
class ChatInputBox : TextBox
{
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public bool IsChatting { get; set; }
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public string RawText { get; set; } = "";

    bool caretVisible = true;
    System.Windows.Forms.Timer caretTimer;

    public ChatInputBox()
    {
        SetStyle(ControlStyles.UserPaint, true);
        ReadOnly = true;
        BorderStyle = BorderStyle.FixedSingle;

        caretTimer = new System.Windows.Forms.Timer { Interval = 500 };
        caretTimer.Tick += (s, e) =>
        {
            caretVisible = !caretVisible;
            Invalidate();
        };
        caretTimer.Start();
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        e.Graphics.Clear(BackColor);

        string text;
        Color color;

        if (!IsChatting && string.IsNullOrEmpty(RawText))
        {
            text = "Press / to type";
            color = Color.FromArgb(128, Color.Gray);
        }
        else
        {
            text = RawText + (IsChatting && caretVisible ? "|" : "");
            color = ForeColor;
        }

        TextRenderer.DrawText(
            e.Graphics,
            text,
            Font,
            ClientRectangle,
            color,
            TextFormatFlags.Left | TextFormatFlags.VerticalCenter);
    }
}

// --------------------------------------------------
// Chat window
// --------------------------------------------------
class ChatForm : Form
{
    Process robloxProcess;

    System.Windows.Forms.Timer pollTimer;
    System.Windows.Forms.Timer fadeTimer;

    TextBox chatBox;
    ChatInputBox inputBox;

    float targetOpacity = 0.7f;
    const float fadeStep = 0.05f;

    bool isChatting;
    string rawInputText = "";

    private static readonly HttpClient client = new HttpClient()
    { 
    // If the server doesn't respond in x seconds, throw an exception
    Timeout = TimeSpan.FromSeconds(60) // Set to 60 as Render free-tier may take time to wake up
    };

public ChatForm(Process proc)
    {
        robloxProcess = proc;

        Width = 320;
        Height = 260;
        TopMost = true;
        Text = "Chat";
        Opacity = 0.7f;

        chatBox = new TextBox
        {
            Multiline = true,
            ReadOnly = true,
            Dock = DockStyle.Top,
            Height = 200,
            BackColor = Color.Black,
            ForeColor = Color.White,
            ScrollBars = ScrollBars.Vertical
        };

        inputBox = new ChatInputBox
        {
            Dock = DockStyle.Bottom,
            BackColor = Color.Black,
            ForeColor = Color.White
        };

        Controls.Add(chatBox);
        Controls.Add(inputBox);

        // Hide real Win32 caret defensively
        chatBox.GotFocus += (s, e) => NativeMethods.HideCaret(chatBox.Handle);
        chatBox.MouseDown += (s, e) => NativeMethods.HideCaret(chatBox.Handle);
        inputBox.GotFocus += (s, e) => ActiveControl = null;
        inputBox.MouseDown += (s, e) => ActiveControl = null;

        pollTimer = new System.Windows.Forms.Timer { Interval = 500 };
        pollTimer.Tick += PollRobloxWindow;
        pollTimer.Start();

        fadeTimer = new System.Windows.Forms.Timer { Interval = 50 };
        fadeTimer.Tick += UpdateOpacity;
        fadeTimer.Start();
    }

    void PollRobloxWindow(object sender, EventArgs e)
    {
        if (robloxProcess.HasExited)
        {
            Close();
            return;
        }

        IntPtr hWnd = robloxProcess.MainWindowHandle;
        if (hWnd == IntPtr.Zero) return;

        if (NativeMethods.IsIconic(hWnd))
            WindowState = FormWindowState.Minimized;
        else if (WindowState == FormWindowState.Minimized)
            WindowState = FormWindowState.Normal;
    }

    void UpdateOpacity(object sender, EventArgs e)
    {
        if (Math.Abs(Opacity - targetOpacity) < 0.01f) return;
        Opacity += Opacity < targetOpacity ? fadeStep : -fadeStep;
    }

    public void StartChatMode()
    {
        isChatting = true;
        rawInputText = "";
        targetOpacity = 1.0f;
        SyncInput();
    }

    public void AppendTextFromKey(string text)
    {
        rawInputText += text;
        SyncInput();
    }

    public void Backspace()
    {
        if (rawInputText.Length > 0)
            rawInputText = rawInputText[..^1];
        SyncInput();
    }

    public async Task Send()
    {
        if (!string.IsNullOrWhiteSpace(rawInputText))
        {
            string userMessage = rawInputText;

            // 1. Immediate UI Feedback
            chatBox.AppendText($"You: {userMessage}\r\n");
            rawInputText = "";
            isChatting = false;
            targetOpacity = 0.7f;
            SyncInput();

            try
            {
                // 2. Network Call
                var content = new StringContent(userMessage, Encoding.UTF8, "text/plain");
                // PaaS echo server for POC demo testing
                var response = await client.PostAsync("https://RobloxChatLauncherDemo.onrender.com/echo", content);

                if (response.IsSuccessStatusCode)
                {
                    string echoResponse = await response.Content.ReadAsStringAsync();

                    this.Invoke((MethodInvoker)delegate {
                        chatBox.AppendText($"Server: {echoResponse}\r\n");
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
                            messageText = "Your last message was not sent as it violates community guidelines.";
                            break;
                        case "queue_full":
                            messageText = "Your last message was rejected because the server queue is full. Please try again shortly.";
                            break;
                        case "api_error":
                            messageText = "Your last message could not be processed due to a server error. Please try again.";
                            break;
                        default:
                            messageText = "Your last message was not sent due to unknown reasons.";
                            break;
                    }
                
                    this.Invoke((MethodInvoker)delegate {
                        chatBox.AppendText(messageText + "\r\n");
                    });
                }
            }
            // 3. Catch Timeout Specifically
            catch (TaskCanceledException)
            {
                this.Invoke((MethodInvoker)delegate {
                    chatBox.AppendText("System: Request timed out. (Render server may be waking up)\r\n");
                });
            }
            // 4. Catch General Errors (DNS, No Internet, etc.)
            catch (Exception ex)
            {
                this.Invoke((MethodInvoker)delegate {
                    chatBox.AppendText($"System: Connection error: {ex.Message}\r\n");
                });
            }
            finally
            {
                // Ensure the chat always scrolls to the bottom and hides caret
                this.Invoke((MethodInvoker)delegate {
                    chatBox.SelectionStart = chatBox.Text.Length;
                    chatBox.ScrollToCaret();
                    NativeMethods.HideCaret(chatBox.Handle);
                });
            }
        }
    }

    void SyncInput()
    {
        inputBox.RawText = rawInputText;
        inputBox.IsChatting = isChatting;
        inputBox.Invalidate();
    }
}

// --------------------------------------------------
// Keyboard hook (layout-correct, shift-safe)
// --------------------------------------------------
class ChatKeyboardHandler : IDisposable
{
    IKeyboardMouseEvents hook;
    ChatForm form;
    bool chatMode;

    public ChatKeyboardHandler(ChatForm chatForm)
    {
        form = chatForm;
        hook = Hook.GlobalEvents();
        hook.KeyDown += OnKeyDown;
    }

    void OnKeyDown(object sender, KeyEventArgs e)
    {
        // Ignore all input if the chat window is minimized
        if (form.WindowState == FormWindowState.Minimized)
            return;

        if (!chatMode)
        {
            if (e.KeyCode == Keys.OemQuestion) // slash key
            {
                chatMode = true;
                form.StartChatMode();
                e.Handled = true;
            }
            return;
        }

        if (e.KeyCode == Keys.Enter)
        {
            form.Send();
            chatMode = false;
            e.Handled = true;
            return;
        }

        if (e.KeyCode == Keys.Back)
        {
            form.Backspace();
            e.Handled = true;
            return;
        }

        string text = TranslateKey(e);
        if (!string.IsNullOrEmpty(text))
        {
            form.AppendTextFromKey(text);
            e.Handled = true;
        }
    }

    string TranslateKey(KeyEventArgs e)
    {
        byte[] state = new byte[256];
        if (!NativeMethods.GetKeyboardState(state))
            return null;

        StringBuilder sb = new StringBuilder(8);
        IntPtr layout = NativeMethods.GetKeyboardLayout(0);

        int result = NativeMethods.ToUnicodeEx(
            (uint)e.KeyValue,
            0,
            state,
            sb,
            sb.Capacity,
            0,
            layout);

        return result > 0 ? sb.ToString() : null;
    }

    public void Dispose()
    {
        hook.KeyDown -= OnKeyDown;
        hook.Dispose();
    }
}
