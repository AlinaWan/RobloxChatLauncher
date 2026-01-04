using System;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Windows.Forms;
using Microsoft.Win32;
using System.Web;
using System.Runtime.InteropServices;
using Gma.System.MouseKeyHook;
using System.Drawing;
using System.Text;

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
    public bool IsChatting { get; set; }
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

    public void Send()
    {
        if (!string.IsNullOrWhiteSpace(rawInputText))
        {
            chatBox.AppendText($"You: {rawInputText}\r\n");
            chatBox.SelectionStart = chatBox.Text.Length;
            chatBox.ScrollToCaret();
            NativeMethods.HideCaret(chatBox.Handle);
        }

        rawInputText = "";
        isChatting = false;
        targetOpacity = 0.7f;
        SyncInput();
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
        if (!chatMode)
        {
            if (e.KeyCode == Keys.OemQuestion)
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
