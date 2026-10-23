using System;
using System.Text;
using System.Windows.Forms;

namespace MacroDeck
{
    internal sealed class Hotkey
    {
        public int Key;
        public bool Ctrl;
        public bool Alt;
        public bool Shift;
        public bool Win;
        public string Text;

        public static Hotkey Parse(string value)
        {
            if (String.IsNullOrWhiteSpace(value)) return null;
            string[] parts = value.Split('+');
            Hotkey h = new Hotkey();
            for (int i = 0; i < parts.Length; i++)
            {
                string part = parts[i].Trim();
                if (part.Equals("Ctrl", StringComparison.OrdinalIgnoreCase) || part.Equals("Control", StringComparison.OrdinalIgnoreCase)) h.Ctrl = true;
                else if (part.Equals("Alt", StringComparison.OrdinalIgnoreCase)) h.Alt = true;
                else if (part.Equals("Shift", StringComparison.OrdinalIgnoreCase)) h.Shift = true;
                else if (part.Equals("Win", StringComparison.OrdinalIgnoreCase) || part.Equals("Windows", StringComparison.OrdinalIgnoreCase)) h.Win = true;
                else
                {
                    Keys k;
                    if (part.Length == 1 && Char.IsDigit(part[0])) k = (Keys)((int)Keys.D0 + (part[0] - '0'));
                    else if (!Enum.TryParse<Keys>(part, true, out k)) return null;
                    h.Key = (int)k;
                }
            }
            if (h.Key <= 0 || h.Key > 255) return null;
            h.Text = Format(h.Key, h.Ctrl, h.Alt, h.Shift, h.Win);
            return h;
        }

        public static string Format(int key, bool ctrl, bool alt, bool shift, bool win)
        {
            StringBuilder s = new StringBuilder();
            if (ctrl) s.Append("Ctrl+");
            if (alt) s.Append("Alt+");
            if (shift) s.Append("Shift+");
            if (win) s.Append("Win+");
            string keyName = ((Keys)key).ToString();
            if (keyName.StartsWith("D", StringComparison.Ordinal) && keyName.Length == 2 && Char.IsDigit(keyName[1])) keyName = keyName.Substring(1);
            s.Append(keyName);
            return s.ToString();
        }
    }
}
