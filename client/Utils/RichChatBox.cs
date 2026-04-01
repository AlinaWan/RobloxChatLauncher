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

        /// <remarks>
        /// This method uses some stupid ugly hacks because GDI+ is a nightmare to work with and usually maps the wrong fonts and WinForms is stupid.
        /// Don't try to fix or simplify this unless you ensure the random chance to map the wrong font issue is actually resolved.
        /// You may think that loading all fonts into a single PrivateFontCollection and then creating Font objects with specific styles (Regular, Bold, Italic) would work, but it doesn't.
        /// You may find that a fix will cause it to be random if Medium or Black actually gets mapped to the correct weight, and sometimes Regular will map to Light because GDI+ is a joke.
        /// </remarks>
        private static void LoadAllFonts()
        {
            string[] fontFiles = {
                "Montserrat-Thin.ttf", "Montserrat-ThinItalic.ttf",
                "Montserrat-ExtraLight.ttf", "Montserrat-ExtraLightItalic.ttf",
                "Montserrat-Light.ttf", "Montserrat-LightItalic.ttf",
                "Montserrat-Regular.ttf", "Montserrat-Italic.ttf",
                "Montserrat-Medium.ttf", "Montserrat-MediumItalic.ttf",
                "Montserrat-SemiBold.ttf", "Montserrat-SemiBoldItalic.ttf",
                "Montserrat-Bold.ttf", "Montserrat-BoldItalic.ttf",
                "Montserrat-ExtraBold.ttf", "Montserrat-ExtraBoldItalic.ttf",
                "Montserrat-Black.ttf", "Montserrat-BlackItalic.ttf"
            };

            var assembly = Assembly.GetExecutingAssembly();

            foreach (var fileName in fontFiles)
            {
                string weight = Path.GetFileNameWithoutExtension(fileName).Replace("Montserrat-", "");

                using Stream? stream = assembly.GetManifestResourceStream(fileName);
                if (stream == null)
                    continue;

                byte[] fontData = new byte[stream.Length];
                stream.Read(fontData, 0, fontData.Length);

                // Define what this SPECIFIC file MUST be named by GDI+ to be considered "correct"
                // Most Montserrat weights include the weight in the family name (e.g. "Montserrat Medium")
                // except for Regular, Bold, Italic, and BoldItalic which GDI+ usually just calls "Montserrat"
                string expectedBase = weight.Replace("Italic", "").Trim();
                string expectedName = (expectedBase == "Regular" || expectedBase == "Bold" || expectedBase == "")
                    ? "Montserrat"
                    : $"Montserrat {expectedBase}";

                bool success = false;
                for (int attempt = 0; attempt < 20 && !success; attempt++)
                {
                    // Allocate fresh memory for every single attempt to defeat GDI+ caching
                    IntPtr data = Marshal.AllocCoTaskMem(fontData.Length);
                    Marshal.Copy(fontData, 0, data, fontData.Length);

                    var solo = new PrivateFontCollection();
                    solo.AddMemoryFont(data, fontData.Length);

                    if (solo.Families.Length > 0)
                    {
                        var family = solo.Families[0];

                        // If it's a specific weight (like Medium) but GDI+ named it generic "Montserrat", 
                        // it means it merged. This is a FAILURE. We loop again.
                        if (family.Name.Equals(expectedName, StringComparison.OrdinalIgnoreCase))
                        {
                            _montserratWeights[weight] = family;

                            // Now that we have our isolated pointer, register it for the RichTextBox
                            _fontCollection.AddMemoryFont(data, fontData.Length);
                            uint cFonts = 0;
                            NativeMethods.AddFontMemResourceEx(data, (uint)fontData.Length, IntPtr.Zero, ref cFonts);

                            Console.WriteLine($"[FontLoad] Key: {weight,-15} | Mapped To: {family.Name,-20} | Status: CORRECT");
                            success = true;
                            // Note: We do NOT free 'data' because the FontFamily and GDI+ need it alive.
                        }
                        else
                        {
                            Console.WriteLine($"[FontLoad] !! INCORRECT !! Key: {weight} expected '{expectedName}' but GDI+ merged it into '{family.Name}'. Retrying...");

                            // Clean up this failed attempt's memory and try again
                            Marshal.FreeCoTaskMem(data);
                            solo.Dispose();
                        }
                    }
                    else
                    {
                        Console.WriteLine($"[FontLoad] !! FAILURE !! GDI+ failed to load {weight} into memory. Retrying...");

                        Marshal.FreeCoTaskMem(data);
                        solo.Dispose();
                    }
                }
            }
        }

        private static Font GetMontserrat(string weight, float size)
        {
            if (_montserratWeights.TryGetValue(weight, out FontFamily? family))
            {
                // Check if this specific family object supports the Bold style.
                // If we loaded "Montserrat-Bold.ttf", IsStyleAvailable(Bold) will be true.
                if (weight.Contains("Bold") && family.IsStyleAvailable(FontStyle.Bold))
                    return new Font(family, size, FontStyle.Bold);

                if (weight.Contains("Italic") && family.IsStyleAvailable(FontStyle.Italic))
                    return new Font(family, size, FontStyle.Italic);

                return new Font(family, size, FontStyle.Regular);
            }

            return new Font(SystemFonts.DefaultFont.FontFamily, size);
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

        // Whisper message with [To/From] prefix and colored sender name
        public static void AppendWhisperMessage(RichTextBox box, string sender, string target, string text, bool isTo)
        {
            Color nameColor = NameColorUtil.GetNameColor(sender);

            string prefix = isTo
                ? string.Format(Strings.WhisperTo, target)
                : string.Format(Strings.WhisperFrom, sender);

            box.SelectionStart = box.TextLength;
            box.SelectionLength = 0;

            box.SelectionFont = GetMontserrat("Medium", DefaultSize);
            box.SelectionColor = box.ForeColor;
            box.AppendText($"[{prefix}] ");

            box.SelectionColor = nameColor;
            box.AppendText(sender);

            box.SelectionColor = box.ForeColor;
            box.AppendText($": {text}\r\n");

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
            AppendWhisperMessage(box, "im_riri", "Guest 39360", "Are we ready to launch?", true);
            AppendWhisperMessage(box, "Guest 39360", "im_riri", "Send it!", false);
            AppendText(box, "");
            AppendText(box, "");
            AppendText(box, "");
        }
#endif
    }
}
