using System.Drawing;
using System.Drawing.Text;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Threading.Channels;
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
        private const float DefaultSize = 10f;
        private static readonly PrivateFontCollection _fontCollection = new PrivateFontCollection();
        private static readonly Dictionary<string, FontFamily> _montserratWeights =
            new(StringComparer.OrdinalIgnoreCase);

        static RichChatBox()
        {
            LoadAllFonts();
        }

        private static void LoadAllFonts()
        {
            string[] fontFiles = {
                "Montserrat-Black.ttf", "Montserrat-BlackItalic.ttf",
                "Montserrat-Bold.ttf", "Montserrat-BoldItalic.ttf",
                "Montserrat-ExtraBold.ttf", "Montserrat-ExtraBoldItalic.ttf",
                "Montserrat-ExtraLight.ttf", "Montserrat-ExtraLightItalic.ttf",
                "Montserrat-Italic.ttf", "Montserrat-Light.ttf",
                "Montserrat-LightItalic.ttf", "Montserrat-Medium.ttf",
                "Montserrat-MediumItalic.ttf", "Montserrat-Regular.ttf",
                "Montserrat-SemiBold.ttf", "Montserrat-SemiBoldItalic.ttf",
                "Montserrat-Thin.ttf", "Montserrat-ThinItalic.ttf"
            };

            var assembly = Assembly.GetExecutingAssembly();

            foreach (var fileName in fontFiles)
            {
                using Stream? stream = assembly.GetManifestResourceStream(fileName);
                if (stream == null)
                    continue;

                byte[] fontData = new byte[stream.Length];
                stream.Read(fontData, 0, fontData.Length);

                IntPtr data = Marshal.AllocCoTaskMem(fontData.Length);
                Marshal.Copy(fontData, 0, data, fontData.Length);

                _fontCollection.AddMemoryFont(data, fontData.Length);
                uint cFonts = 0;
                NativeMethods.AddFontMemResourceEx(data, (uint)fontData.Length, IntPtr.Zero, ref cFonts);

                Marshal.FreeCoTaskMem(data);

                // Map weight name
                string weight = Path.GetFileNameWithoutExtension(fileName).Replace("Montserrat-", "");
                _montserratWeights[weight] = _fontCollection.Families.Last();
            }
        }

        private static Font GetMontserrat(string weight, float size)
        {
            if (_montserratWeights.TryGetValue(weight, out FontFamily? family))
            {
                return new Font(family, size);
            }

            return new Font(_montserratWeights["Regular"], size);
        }

        // Plain text
        public static void AppendText(RichTextBox box, string message)
        {
            box.SelectionStart = box.TextLength;
            box.SelectionLength = 0;

            box.SelectionFont = GetMontserrat("Medium", DefaultSize);
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
            box.SelectionFont = GetMontserrat("Medium", DefaultSize);
            box.SelectionColor = nameColor;
            box.AppendText($"{sender}: ");

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

            box.SelectionFont = GetMontserrat("Medium", DefaultSize);
            box.SelectionColor = Color.Gray;
            box.AppendText($"{Strings.System}: ");

            box.SelectionColor = box.ForeColor;
            box.AppendText($"{message}\r\n");

            box.SelectionStart = box.TextLength;
            box.ScrollToCaret();
            NativeMethods.HideCaret(box.Handle);
        }

        // Main overload for broadcast messages
        public static void AppendBroadcastMessage(RichTextBox box, string sender, string message, Color? overrideColor)
        {
            // Use overrideColor if provided, otherwise use NameColorUtil
            Color nameColor = overrideColor ?? NameColorUtil.GetNameColor(sender);

            box.SelectionStart = box.TextLength;
            box.SelectionLength = 0;

            box.SelectionFont = GetMontserrat("Bold", DefaultSize);
            box.SelectionColor = nameColor;
            box.AppendText($"{sender}: ");

            box.SelectionFont = GetMontserrat("Medium", DefaultSize);
            box.SelectionColor = box.ForeColor;
            box.AppendText($"{message}\r\n");

            box.SelectionStart = box.TextLength;
            box.ScrollToCaret();
            NativeMethods.HideCaret(box.Handle);
        }

        // Hex String Overload
        public static void AppendBroadcastMessage(RichTextBox box, string sender, string message, string? hexColor)
        {
            Color? parsedColor = null;
            if (!string.IsNullOrEmpty(hexColor))
            {
                try
                {
                    parsedColor = ColorTranslator.FromHtml(hexColor);
                }
                catch { /* Invalid hex */ }
            }
            AppendBroadcastMessage(box, sender, message, parsedColor);
        }

        // Default Overload
        public static void AppendBroadcastMessage(RichTextBox box, string sender, string message)
        {
            AppendBroadcastMessage(box, sender, message, (Color?)null);
        }

#if DEBUG
        public static void ShowcasePreview(RichTextBox box)
        {
            AppendSystemMessage(box, string.Format(Strings.ConnectingToServer, "66f27f28-2ca4-4c35-93ed-8002346d4edb"));
            AppendSystemMessage(box, Strings.ConnectedSuccessfully);
            AppendChatMessage(box, "im_riri", "Hello over WS!");
            AppendChatMessage(box, "Guest 41670", "Hello World!");
            AppendChatMessage(box, "Guest 39360", "What's up?");
            AppendSystemMessage(box, "Hello over HTTP! (Only you can see this message.)");
            AppendSystemMessage(box, Strings.MessageRejectedModeration);
            AppendChatMessage(box, "im_riri", "Are we ready to launch?");
            AppendChatMessage(box, "Guest 39360", "Send it!");
            AppendText(box, "");
            AppendText(box, "");
            AppendText(box, "");
            AppendText(box, "");
        }
#endif
    }
}
