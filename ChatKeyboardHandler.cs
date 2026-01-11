namespace ChatLauncherApp
{
    // --------------------------------------------------
    // Keyboard hook (layout-correct, shift-safe)
    // --------------------------------------------------
    class ChatKeyboardHandler : IDisposable
    {
        IKeyboardMouseEvents hook;
        ChatForm form;
        bool chatMode;

        static bool IsNonTextKey(Keys key) =>
            key == Keys.Escape ||
            key == Keys.Enter ||
            key == Keys.Back ||
            key == Keys.ControlKey ||
            key == Keys.ShiftKey ||
            key == Keys.Menu; // Both alt keys

        public ChatKeyboardHandler(ChatForm chatForm)
        {
            form = chatForm;
            hook = Hook.GlobalEvents();
            hook.KeyDown += OnKeyDown;
        }

        void OnKeyDown(object sender, KeyEventArgs e)
        {
            // 1. Ignore all input if the chat window is minimized
            // This handles cases where the user minimizes Roblox
            if (form.WindowState == FormWindowState.Minimized)
                return;

            // 2. Ignore all input if Roblox is NOT the active (focused) window
            // We get the current foreground window and compare it to Roblox's handle
            // This handles cases where the user alt-tabs away or clicks another window
            IntPtr foregroundWindow = NativeMethods.GetForegroundWindow();
            if (!form.IsRobloxForegroundProcess())
                return;

            if (!chatMode)
            {
                // Toggle UI Visibility: Ctrl + Shift + C
                if (e.Control && e.Shift && e.KeyCode == Keys.C)
                {
                    form.ToggleVisibility();
                    e.Handled = true;
                    return;
                }
                if (e.KeyCode == Keys.OemQuestion) // slash key
                {
                    chatMode = true;
                    form.StartChatMode();
                    e.Handled = true;
                }
                return;
            }

            if (e.KeyCode == Keys.Escape)
            {
                chatMode = false;           // Stop intercepting keys in this app
                form.CancelChatMode();      // Update UI (opacity/caret) but keep text
                                            // DO NOT set e.Handled = true; 
                                            // This allows the Escape key to "pass through" to the game/Windows
                return;
            }

            if (e.KeyCode == Keys.Enter)
            {
                // Use _ = to explicitly fire and forget the task
                _ = form.Send();
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
            // Don't translate control keys into text characters
            if (IsNonTextKey(e.KeyCode))
                return null;

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
}