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

using RobloxChatLauncher.Services;
using RobloxChatLauncher.Utils;

namespace RobloxChatLauncher
{
    // --------------------------------------------------
    // Custom input box (fake caret, custom paint)
    // --------------------------------------------------
    class ChatInputBox : TextBox
    {
        [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
        public bool IsChatting
        {
            get; set;
        }
        [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
        public string RawText { get; set; } = "";

        bool caretVisible = true;
        System.Windows.Forms.Timer caretTimer;

        public ChatInputBox()
        {
            SetStyle(ControlStyles.UserPaint, true);
            ReadOnly = true;
            BorderStyle = BorderStyle.FixedSingle;

            // We need to use a fake caret because since we never truly focus the overlay,
            // the win32 caret doesn't work
            caretTimer = new System.Windows.Forms.Timer { Interval = 500 };
            caretTimer.Tick += (s, e) =>
            {
                caretVisible = !caretVisible;
                this.Invalidate(false);
            };
            caretTimer.Start();
        }

        protected override void OnResize(EventArgs e)
        {
            this.Invalidate(); // Forces a clean redraw of the text and arrow
            base.OnResize(e);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            e.Graphics.Clear(BackColor);

            string text;
            Color color;

            if (!IsChatting && string.IsNullOrEmpty(RawText))
            {
                text = "Press / key | Ctrl+Shift+C to hide";
                color = Color.FromArgb(180, 200, 200, 200); // Gray placeholder
            }
            else
            {
                text = RawText + (IsChatting && caretVisible ? "|" : "");
                color = ForeColor; // White text
            }

            // Set a 10px margin so text doesn't hit the edge
            Rectangle textRect = new Rectangle(10, 0, ClientRectangle.Width - 50, ClientRectangle.Height);

            TextRenderer.DrawText(
                e.Graphics,
                text,
                Font,
                textRect,
                color,
                TextFormatFlags.Left | TextFormatFlags.VerticalCenter);

            // Draw the arrow icon on the right
            TextRenderer.DrawText(e.Graphics, "➤", Font,
                new Rectangle(Width - 35, 0, 30, Height), color,
                TextFormatFlags.Right | TextFormatFlags.VerticalCenter);
        }
    }

    // --------------------------------------------------
    // Class for round hide/unhide button
    // --------------------------------------------------
    public class RoundButton : Control
    {
        public event EventHandler Clicked;
        public event EventHandler<Point> Dragged; // Notify form of movement
        public event EventHandler DragEnded;

        private Image imgOn;
        private Image imgOff;
        private System.Windows.Forms.Timer holdTimer;
        private System.Windows.Forms.Timer visualTimer; // Timer for smooth animation
        private bool isDragging = false;
        private Point lastMousePos;
        private float holdProgress = 0f; // 0 to 1.0
        private DateTime holdStartTime;
        private const int HOLD_THRESHOLD = 300; // Milliseconds to hold before release (for dragging)

        [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
        public bool IsActive { get; set; } = true;

        public RoundButton()
        {
            this.DoubleBuffered = true; // Prevents flickering during animation
            this.SetStyle(ControlStyles.Selectable, false);
            LoadRobloxIcons();

            // Timer for the logic
            holdTimer = new System.Windows.Forms.Timer { Interval = HOLD_THRESHOLD };
            holdTimer.Tick += (s, e) =>
            {
                holdTimer.Stop();
                visualTimer.Stop();
                holdProgress = 0;
                isDragging = true;
                this.Cursor = Cursors.SizeAll;
                this.Invalidate();
            };

            // Timer for the visual progress
            visualTimer = new System.Windows.Forms.Timer { Interval = 16 }; // ~60 FPS
            visualTimer.Tick += (s, e) =>
            {
                var elapsed = (DateTime.Now - holdStartTime).TotalMilliseconds;
                holdProgress = (float)Math.Min(elapsed / HOLD_THRESHOLD, 1.0);
                this.Invalidate(); // Redraw the progress bar
            };

            this.SizeChanged += (s, e) => UpdateRegion();
        }

        private void UpdateRegion()
        {
            using (var path = new System.Drawing.Drawing2D.GraphicsPath())
            {
                path.AddEllipse(0, 0, Width, Height);
                this.Region = new Region(path);
            }
        }

        // Mouse event overrides for dragging behavior
        protected override void OnMouseMove(MouseEventArgs e)
        {
            if (isDragging)
            {
                // Calculate the movement delta
                int deltaX = e.X - lastMousePos.X;
                int deltaY = e.Y - lastMousePos.Y;

                Dragged?.Invoke(this, new Point(deltaX, deltaY));
            }
            base.OnMouseMove(e);
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            if (e.Button == MouseButtons.Left)
            {
                lastMousePos = e.Location;
                holdStartTime = DateTime.Now;
                holdProgress = 0;
                holdTimer.Start();
                visualTimer.Start();
            }
            base.OnMouseDown(e);
        }

        protected override void OnMouseUp(MouseEventArgs e)
        {
            holdTimer.Stop();
            visualTimer.Stop();

            // If we released before the drag threshold, it's a click
            if (!isDragging && holdProgress < 1.0 && holdProgress > 0)
            {
                Clicked?.Invoke(this, EventArgs.Empty);
            }

            holdProgress = 0;
            isDragging = false;
            this.Cursor = Cursors.Hand;
            DragEnded?.Invoke(this, EventArgs.Empty);
            this.Invalidate();
            base.OnMouseUp(e);
        }

        private void LoadRobloxIcons()
        {
            try
            {
                // Get the Roblox version folder to construct the path to the chat icons
                string versionFolder = RobloxChatLauncher.Utils.RobloxLocator.GetVanillaRobloxVersionFolder();
                string basePath = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                    "Roblox", "Versions", versionFolder, "content", "textures", "ui", "TopBar");

                // Roblox also has `chatOn@2x.png`, `chatOn@3x.png`, `chatOff@2x.png`, and `chatOff@3x.png`
                // if needed in the future for higher DPI displays.
                // There are also voice chat icons in `content/textures/ui/VoiceChat/` if voice chat
                // support is ever added.
                string pathOn = Path.Combine(basePath, "chatOn.png");
                string pathOff = Path.Combine(basePath, "chatOff.png");

                if (File.Exists(pathOn))
                    imgOn = Image.FromFile(pathOn);
                if (File.Exists(pathOff))
                    imgOff = Image.FromFile(pathOff);
            }
            catch { /* Fallback to manual drawing or default icons if path fails */ }
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            // Important: Use AntiAlias for smooth circles
            e.Graphics.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
            e.Graphics.Clear(Color.Magenta); // Background transparency key

            // Draw the dark circular background (Roblox style)
            using (var brush = new SolidBrush(Color.FromArgb(50, 50, 50)))
                e.Graphics.FillEllipse(brush, 0, 0, Width, Height);

            // Draw the chat icon (imgOn or imgOff)
            Image currentImg = IsActive ? imgOn : imgOff;

            if (currentImg != null)
            {
                // Center the icon image inside the circle
                int x = (Width - currentImg.Width) / 2;
                int y = (Height - currentImg.Height) / 2;
                e.Graphics.DrawImage(currentImg, x, y);
            }

            // Draw the Progress Ring
            if (holdProgress > 0 && holdProgress < 1.0)
            {
                float penWidth = 4f;
                // Deflate rectangle so the stroke isn't cut off by the Region clip
                RectangleF rect = new RectangleF(penWidth / 2, penWidth / 2,
                                               Width - penWidth, Height - penWidth);

                using (Pen progressPen = new Pen(Color.DeepSkyBlue, penWidth))
                {
                    float sweepAngle = holdProgress * 360f;
                    // -90 degrees starts the arc at the 12 o'clock position
                    e.Graphics.DrawArc(progressPen, rect, -90, sweepAngle);
                }
            }
        }

        protected override void OnClick(EventArgs e)
        {
            Clicked?.Invoke(this, e);
            base.OnClick(e);
        }
    }

    // --------------------------------------------------
    // Class for resize grip control
    // --------------------------------------------------
    public class ResizeGrip : Control
    {
        private Point lastMousePos;
        private bool isResizing = false;
        public event EventHandler<Size> ResizeDragged;

        public ResizeGrip()
        {
            this.SetStyle(ControlStyles.SupportsTransparentBackColor |
                          ControlStyles.AllPaintingInWmPaint |
                          ControlStyles.OptimizedDoubleBuffer, true);
            this.BackColor = Color.Transparent;
            this.Size = new Size(20, 20);
            this.Cursor = Cursors.SizeNWSE;
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            if (e.Button == MouseButtons.Left)
            {
                isResizing = true;
                lastMousePos = PointToScreen(e.Location);
            }
            base.OnMouseDown(e);
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            if (isResizing)
            {
                Point currentMousePos = PointToScreen(e.Location);
                int deltaX = currentMousePos.X - lastMousePos.X;
                int deltaY = currentMousePos.Y - lastMousePos.Y;

                ResizeDragged?.Invoke(this, new Size(deltaX, deltaY));
                lastMousePos = currentMousePos;
            }
            base.OnMouseMove(e);
        }

        protected override void OnMouseUp(MouseEventArgs e)
        {
            isResizing = false;
            base.OnMouseUp(e);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            e.Graphics.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
            using (Pen pen = new Pen(Color.FromArgb(150, 255, 255, 255), 2))
            {
                // Draw the ⌟ symbol
                // Vertical line
                e.Graphics.DrawLine(pen, Width - 5, Height - 12, Width - 5, Height - 5);
                // Horizontal line
                e.Graphics.DrawLine(pen, Width - 12, Height - 5, Width - 5, Height - 5);
            }
        }
    }

    // --------------------------------------------------
    // Chat window
    // --------------------------------------------------
    public partial class ChatForm : Form
    {
        // This is required to hide the overlay from the alt-tab menu
        protected override CreateParams CreateParams
        {
            get
            {
                CreateParams cp = base.CreateParams;
                // 0x00000080 is WS_EX_TOOLWINDOW
                cp.ExStyle |= 0x00000080;
                // WS_EX_COMPOSITED: tells Windows to composite (double-buffer) all child windows together
                // This is the biggest single fix to reduce flickering
                cp.ExStyle |= 0x02000000;
                return cp;
            }
        }

        Process robloxProcess;
        Panel mainContainer; // New container for the window
        TextBox chatBox;
        ChatInputBox inputBox;
        RoundButton toggleBtn;
        bool isWindowHidden = false;
        bool overlayTopMostActive;

        System.Windows.Forms.Timer fadeTimer;

        IntPtr winEventHook = IntPtr.Zero;
        NativeMethods.WinEventDelegate winEventDelegate;

        private Point defaultOffset = new Point(10, 40); // Original offset
        private Point currentOffset = new Point(10, 40); // Tracks user customization
        private bool isUserMovingWindow = false;

        float chatOnOpacity = 1.0f;
        float chatOffOpacity = 0.7f;
        float targetOpacity = 0.7f;
        const float fadeStep = 0.05f;

        bool isChatting;
        string rawInputText = "";

        // Channel IDs and WebSocket
        private string channelId = "global";
        private RobloxChatLauncher.Services.RobloxAreaService _robloxService;
        private ClientWebSocket wsClient;
        private CancellationTokenSource wsCts = new CancellationTokenSource(); // Make sure it's not null when the form starts or it will raise an exception at runtime
        private const string BASE_URL = "RobloxChatLauncherDemo.onrender.com";

        // Sets a rounded region for the given control as
        // Windows Forms does not natively support rounded corners.
        private void SetRoundedRegion(Control control, int radius)
        {
            System.Drawing.Drawing2D.GraphicsPath path = new System.Drawing.Drawing2D.GraphicsPath();
            path.StartFigure();
            path.AddArc(new Rectangle(0, 0, radius, radius), 180, 90);
            path.AddArc(new Rectangle(control.Width - radius, 0, radius, radius), 270, 90);
            path.AddArc(new Rectangle(control.Width - radius, control.Height - radius, radius, radius), 0, 90);
            path.AddArc(new Rectangle(0, control.Height - radius, radius, radius), 90, 90);
            path.CloseFigure();
            control.Region = new Region(path);
        }

        void OnRobloxLocationChanged(
            IntPtr hWinEventHook,
            uint eventType,
            IntPtr hwnd,
            int idObject,
            int idChild,
            uint dwEventThread,
            uint dwmsEventTime)
        {
            if (hwnd != robloxProcess.MainWindowHandle || isUserMovingWindow)
                return;

            if (NativeMethods.IsIconic(hwnd))
            {
                BeginInvoke((MethodInvoker)(() =>
                    WindowState = FormWindowState.Minimized));
                return;
            }

            NativeMethods.GetWindowRect(hwnd, out NativeMethods.RECT rect);

            BeginInvoke((MethodInvoker)(() =>
            {
                if (WindowState == FormWindowState.Minimized)
                    WindowState = FormWindowState.Normal;

                // Use the dynamic offset instead of +10, +40
                Location = new Point(rect.Left + currentOffset.X, rect.Top + currentOffset.Y);
            }));
        }

        public bool IsRobloxForegroundProcess()
        {
            IntPtr fg = NativeMethods.GetForegroundWindow();
            if (fg == IntPtr.Zero)
                return false;

            NativeMethods.GetWindowThreadProcessId(fg, out uint pid);
            return pid == (uint)robloxProcess.Id;
        }

        private static readonly HttpClient client = new HttpClient()
        {
            // If the server doesn't respond in x seconds, throw an exception
            Timeout = TimeSpan.FromSeconds(60) // Set to 60 as Render free-tier may take time to wake up
        };

        public void ToggleVisibility()
        {
            this.Invoke((MethodInvoker)delegate
            {
                isWindowHidden = !isWindowHidden;
                mainContainer.Visible = !isWindowHidden;

                // Update the visual state of the button
                toggleBtn.IsActive = !isWindowHidden;
                toggleBtn.Invalidate();
            });
        }

        public ChatForm(Process proc)
        {
            robloxProcess = proc;
            robloxProcess.EnableRaisingEvents = true;
            robloxProcess.Exited += RobloxProcess_Exited;

            this.ShowInTaskbar = false; // Hide the GUI process from taskbar

            // Form Transparency/Styling
            this.FormBorderStyle = FormBorderStyle.None;
            this.BackColor = Color.Magenta;
            this.TransparencyKey = Color.Magenta; // Makes form background invisible
            this.Width = 500;
            this.Height = 400;
            this.TopMost = true;
            this.DoubleBuffered = true; // Reduce flicker

            // Circular Toggle Button (Parented to Form, not Container)
            toggleBtn = new RoundButton
            {
                Location = new Point(115, 2),
                Size = new Size(45, 45),
                Cursor = Cursors.Hand
            };

            // Button click toggling visibility is buggy so prefer
            // Using the Ctrl+Shift+C hotkey instead.
            /*
            toggleBtn.Clicked += (s, e) =>
            {
                isWindowHidden = !isWindowHidden;
                mainContainer.Visible = !isWindowHidden;
            };
            */

            toggleBtn.Dragged += (s, delta) =>
            {
                isUserMovingWindow = true;
                // Update the offset based on drag
                currentOffset.X += delta.X;
                currentOffset.Y += delta.Y;

                // Immediate visual update
                this.Location = new Point(this.Location.X + delta.X, this.Location.Y + delta.Y);
            };

            toggleBtn.DragEnded += (s, e) =>
            {
                isUserMovingWindow = false;

                // 1. Get where Roblox is right now
                NativeMethods.GetWindowRect(robloxProcess.MainWindowHandle, out NativeMethods.RECT rect);

                // 2. Calculate our current offset relative to Roblox's top-left
                int relativeX = this.Location.X - rect.Left;
                int relativeY = this.Location.Y - rect.Top;

                // 3. Snap check: If relative position is close to default, snap to it
                int snapDistance = 20; // Adjust as needed
                if (Math.Abs(relativeX - defaultOffset.X) < snapDistance &&
                    Math.Abs(relativeY - defaultOffset.Y) < snapDistance)
                {
                    currentOffset = defaultOffset;
                }
                else
                {
                    // Otherwise, save this new position as the permanent offset
                    currentOffset = new Point(relativeX, relativeY);
                }
                // Persist position
                Properties.Settings1.Default.WindowOffset = currentOffset;

                this.Location = new Point(rect.Left + currentOffset.X, rect.Top + currentOffset.Y);
            };

            this.Controls.Add(toggleBtn);

            // Load these to settings instead of hardcoding them, so we can persist user customizations to the chat container size
            /*
            // 1. Update Main Container styling
            mainContainer = new SmoothPanel // Use SmoothPanel instead of Panel to reduce flicker
            {
                Location = new Point(7, 54),
                Size = new Size(472, 297), // THIS IS THE REAL SIZE OF THE CHAT WINDOW
                BackColor = Color.FromArgb(35, 45, 55), // Semi-transparent Dark Blue-Gray
                // Each number is: Left, Top, Right, Bottom padding respectively
                Padding = new Padding(10, 10, 30, 10) // Give text breathing room
            };
            */

            // Load persisted values
            currentOffset = Properties.Settings1.Default.WindowOffset;
            this.Size = Properties.Settings1.Default.WindowSize;

            // Ensure the mainContainer uses the saved size
            mainContainer = new SmoothPanel
            {
                Location = new Point(7, 54),
                Size = Properties.Settings1.Default.ChatContainerSize,
                BackColor = Color.FromArgb(35, 45, 55),
                Padding = new Padding(10, 10, 30, 10)
            };

            // Apply rounded corners after the control is created
            mainContainer.HandleCreated += (s, e) => SetRoundedRegion(mainContainer, 20);

            // 2. Update Chat History (The top part)
            chatBox = new SmoothTextBox // Use SmoothTextBox instead of TextBox to reduce flicker
            {
                Multiline = true,
                ReadOnly = true,
                Dock = DockStyle.Fill,
                BorderStyle = BorderStyle.None,
                BackColor = Color.FromArgb(35, 45, 55), // Match container
                ForeColor = Color.White,
                Font = new Font("Segoe UI", 10, FontStyle.Regular),
                TabStop = false,
                Margin = new Padding(0), // Set margin to zero
            };

            // 3. Update Input Bar (The bottom part)
            inputBox = new ChatInputBox
            {
                Dock = DockStyle.Bottom,
                Height = 45,
                BackColor = Color.FromArgb(25, 25, 25), // Darker than history
                ForeColor = Color.White,
                Enabled = false,
                // Margin is ignored by Dock, but Padding in the parent will now squeeze this
            };

            // Ensure the input box also has slightly rounded corners
            inputBox.HandleCreated += (s, e) => SetRoundedRegion(inputBox, 10);

            // Resize Grip
            ResizeGrip grip = new ResizeGrip();
            // Anchor it to the bottom right
            grip.Location = new Point(mainContainer.Width - grip.Width, mainContainer.Height - grip.Height);
            grip.Anchor = AnchorStyles.Bottom | AnchorStyles.Right;

            grip.ResizeDragged += (s, delta) =>
            {
                int newWidth = this.Width + delta.Width;
                int newHeight = this.Height + delta.Height;

                if (newWidth > 200 && newHeight > 150)
                {
                    // 1. Stop the layout engine temporarily
                    this.SuspendLayout();
                    mainContainer.SuspendLayout();

                    this.Size = new Size(newWidth, newHeight);
                    mainContainer.Size = new Size(mainContainer.Width + delta.Width, mainContainer.Height + delta.Height);

                    // 2. Refresh the rounded corners
                    SetRoundedRegion(mainContainer, 20);
                    SetRoundedRegion(inputBox, 10);

                    // 3. Resume and force a clean redraw
                    mainContainer.ResumeLayout();
                    this.ResumeLayout(true);
                    this.Update(); // Force instant redraw
                }
                // Update settings (memory only)
                Properties.Settings1.Default.WindowSize = this.Size;
                Properties.Settings1.Default.ChatContainerSize = mainContainer.Size;
                // We will save to disk when the form closes
            };

            mainContainer.Controls.Add(chatBox);
            mainContainer.Controls.Add(inputBox);
            mainContainer.Controls.Add(grip);
            grip.BringToFront(); // Ensure it is above the chatBox

            // Hide real Win32 caret defensively
            chatBox.GotFocus += (s, e) => NativeMethods.HideCaret(chatBox.Handle);
            chatBox.MouseDown += (s, e) => NativeMethods.HideCaret(chatBox.Handle);
            inputBox.GotFocus += (s, e) => ActiveControl = null;
            inputBox.MouseDown += (s, e) => ActiveControl = null;

            fadeTimer = new System.Windows.Forms.Timer { Interval = 50 };
            fadeTimer.Tick += UpdateOpacity;
            fadeTimer.Start();

            winEventDelegate = OnRobloxLocationChanged;

            winEventHook = NativeMethods.SetWinEventHook(
                NativeMethods.EVENT_OBJECT_LOCATIONCHANGE,
                NativeMethods.EVENT_OBJECT_LOCATIONCHANGE,
                IntPtr.Zero,
                winEventDelegate,
                (uint)robloxProcess.Id,
                0,
                NativeMethods.WINEVENT_OUTOFCONTEXT);

            // ----- Reset Button in the bottom right corner -----
            Label resetBtn = new Label
            {
                Text = "1:1",
                Size = new Size(30, 20),
                ForeColor = Color.DarkGray,
                BackColor = Color.Transparent, // Let it blend with the panel
                TextAlign = ContentAlignment.MiddleCenter,
                Cursor = Cursors.Hand,
                Font = new Font("Segoe UI", 7, FontStyle.Bold),
                Anchor = AnchorStyles.Bottom | AnchorStyles.Right
            };

            // Position it in the alley created by the padding
            // Right edge, and just above the Resize Grip
            resetBtn.Location = new Point(mainContainer.Width - 25, mainContainer.Height - 45);

            resetBtn.Click += (s, e) =>
            {
                this.Size = new Size(500, 400);
                mainContainer.Size = new Size(472, 297);
                SetRoundedRegion(mainContainer, 20);
                SetRoundedRegion(inputBox, 10);
            };

            mainContainer.Controls.Add(resetBtn);
            resetBtn.BringToFront(); // Ensure it stays above the chatBox

            this.Controls.Add(mainContainer);

            // Ensure it is layered correctly
            mainContainer.BringToFront();

            // Manually trigger the first position sync
            NativeMethods.GetWindowRect(robloxProcess.MainWindowHandle, out NativeMethods.RECT initialRect);
            this.Location = new Point(initialRect.Left + currentOffset.X, initialRect.Top + currentOffset.Y);

            // --- Roblox Log Monitor for JobID changes ---
            // Initialize the Roblox Log Monitor
            _robloxService = new RobloxChatLauncher.Services.RobloxAreaService();

            // Subscribe to ID changes
            _robloxService.OnJobIdChanged += async (s, newJobId) =>
            {
                // Safety check: Don't reconnect if we are already in this channel
                if (this.channelId == newJobId)
                    return;

                this.Invoke((MethodInvoker)delegate {
                    bool wasGlobal = (this.channelId == "global");
                    this.channelId = newJobId;

                    if (wasGlobal)
                    {
                        // chatBox.AppendText($"[Server]: Server instance found!\r\n");
                    }
                    else
                        chatBox.AppendText($"[Server]: Switching server...\r\n");
                });

                await RestartWebSocketAsync(); // Reconnect to the WebSocket
            };

            // Start watching logs, passing the Roblox process for session tracking
            _robloxService.Start(robloxProcess);

            // Initial check: If we can't find a JobID yet, wait for the log monitor to catch it
            channelId = "global";
            // chatBox.AppendText("[Server]: Searching for Roblox server instance...\r\n");

            chatBox.AppendText("Made with ❤︎ by Riri.\r\n");
            chatBox.AppendText("Check for updates at github.com/AlinaWan/RobloxChatLauncher\r\n");
            chatBox.AppendText("Chat '/?' or '/help' for a list of chat commands.\r\n");

            // --- End Roblox Log Monitor ---
        }

        private void RobloxProcess_Exited(object sender, EventArgs e)
        {
            // 1. Dispose the service safely
            _robloxService?.Dispose();
            _robloxService = null; // Set to null so OnFormClosed knows it's already done

            // 2. Close the form
            if (!IsDisposed && InvokeRequired)
            {
                BeginInvoke((MethodInvoker)(() => this.Close()));
            }
        }

        void UpdateOpacity(object sender, EventArgs e) // Z-order scoping is also here so we can use the same timer. Please do not make it its own timer
        {
            // Z-order scoping
            // The overlay will always stay above Roblox,
            // but not necessarily above other windows if those windows are above Roblox.
            bool robloxActive = this.IsRobloxForegroundProcess();

            if (robloxActive && !overlayTopMostActive)
            {
                TopMost = true;
                overlayTopMostActive = true;
            }
            else if (!robloxActive && overlayTopMostActive)
            {
                TopMost = false;
                overlayTopMostActive = false;
            }
            // END Z-order scoping

            // Fade logic
            if (Math.Abs(Opacity - targetOpacity) < 0.01f)
                return;
            Opacity += Opacity < targetOpacity ? fadeStep : -fadeStep;
        }

        public void StartChatMode()
        {
            isChatting = true;
            // Don't clear the input bar
            // rawInputText = "";
            targetOpacity = chatOnOpacity;
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

        public void CancelChatMode()
        {
            isChatting = false;
            targetOpacity = chatOffOpacity;
            SyncInput();
        }

        void SyncInput()
        {
            inputBox.RawText = rawInputText;
            inputBox.IsChatting = isChatting;
            inputBox.Invalidate();
        }

        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            // Final save of all settings
            // Windows will handle saving this for us
            Properties.Settings1.Default.WindowOffset = currentOffset;
            Properties.Settings1.Default.WindowSize = this.Size;
            Properties.Settings1.Default.ChatContainerSize = mainContainer.Size;
            Properties.Settings1.Default.Save();

            base.OnFormClosing(e);
        }
    }

    // --------------------------------------------------
    // Smooth Panel and TextBox (for reducing flicker)
    // --------------------------------------------------
    public class SmoothPanel : Panel
    {
        public SmoothPanel()
        {
            this.DoubleBuffered = true;
            this.SetStyle(ControlStyles.AllPaintingInWmPaint | // Don't add ControlStyles.UserPaint here or it will render garbage pixels when resizing the window
                          ControlStyles.OptimizedDoubleBuffer, true);
        }
    }
    public class SmoothTextBox : TextBox
    {
        public SmoothTextBox()
        {
            this.DoubleBuffered = true;
            // This stops the background from "flashing" dark before the text draws
            this.SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer, true);
        }
    }
}
