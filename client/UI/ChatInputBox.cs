namespace RobloxChatLauncher.UI
{
    // --------------------------------------------------
    // Custom input box (fake caret, custom paint)
    // --------------------------------------------------
    using System.ComponentModel;
    using System.Drawing;
    using System.Windows.Forms;

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
}