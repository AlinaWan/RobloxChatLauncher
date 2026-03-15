using System.Drawing;
using System.Windows.Forms;

using RobloxChatLauncher.Localization;

namespace RobloxChatLauncher.Utils
{
    /// <summary>
    /// Provides static methods for appending formatted chat and system messages to a RichTextBox control.
    /// Handles coloring of sender names and system messages to enhance readability and maintain visual consistency
    /// with Roblox's default chat colors and automatically handles `\r\n` line breaks.
    /// </summary>
    public static class RichChatBox
    {
        // Plain text
        public static void AppendText(RichTextBox box, string message)
        {
            box.SelectionStart = box.TextLength;
            box.SelectionLength = 0;

            box.SelectionColor = box.ForeColor;
            box.AppendText($"{message}\r\n");

            box.SelectionStart = box.TextLength;
            box.ScrollToCaret();
            NativeMethods.HideCaret(box.Handle);
        }

        // Chat message with sender name
        public static void AppendChatMessage(RichTextBox box, string sender, string message)
        {
            Color nameColor = NameColorUtil.GetNameColor(sender);

            box.SelectionStart = box.TextLength;
            box.SelectionLength = 0;

            // name
            box.SelectionColor = nameColor;
            box.AppendText($"[{sender}]: ");

            // message
            box.SelectionColor = box.ForeColor;
            box.AppendText($"{message}\r\n");

            box.SelectionColor = box.ForeColor;

            box.SelectionStart = box.TextLength;
            box.ScrollToCaret();
            NativeMethods.HideCaret(box.Handle);
        }

        // System message
        public static void AppendSystemMessage(RichTextBox box, string message)
        {
            box.SelectionStart = box.TextLength;
            box.SelectionLength = 0;

            box.SelectionColor = Color.Gray;
            box.AppendText($"[{Strings.System}]: ");

            box.SelectionColor = box.ForeColor;
            box.AppendText($"{message}\r\n");

            box.SelectionStart = box.TextLength;
            box.ScrollToCaret();
            NativeMethods.HideCaret(box.Handle);
        }
    }

}
