using System;
using System.Runtime.InteropServices;

namespace MacroDeck
{
    internal sealed class NativeHooks : IDisposable
    {
        private const int WhKeyboardLl = 13;
        private const int WhMouseLl = 14;
        private const int WmKeyDown = 0x0100;
        private const int WmKeyUp = 0x0101;
        private const int WmSysKeyDown = 0x0104;
        private const int WmSysKeyUp = 0x0105;
        private const int WmMouseMove = 0x0200;
        private const int WmLButtonDown = 0x0201;
        private const int WmLButtonUp = 0x0202;
        private const int WmRButtonDown = 0x0204;
        private const int WmRButtonUp = 0x0205;
        private const int WmMButtonDown = 0x0207;
        private const int WmMButtonUp = 0x0208;
        private const int WmMouseWheel = 0x020A;
        private const int WmXButtonDown = 0x020B;
        private const int WmXButtonUp = 0x020C;
        private const uint LlkhfInjected = 0x10;
        private const uint LlmhfInjected = 0x01;
        private readonly MainForm owner;
        private readonly HookProc keyboardProc;
        private readonly HookProc mouseProc;
        private IntPtr keyboardHook;
        private IntPtr mouseHook;
        private readonly bool[] keyDown = new bool[256];
        private readonly bool[] swallowed = new bool[256];
        private readonly bool[] ignoredKeyUp = new bool[256];
        private readonly bool[] ignoredMouseUp = new bool[5];
        private int lastMouseX = Int32.MinValue;
        private int lastMouseY = Int32.MinValue;
        private long lastMouseMs = -1000;

        [StructLayout(LayoutKind.Sequential)] private struct KbdLl { public uint VkCode; public uint ScanCode; public uint Flags; public uint Time; public IntPtr ExtraInfo; }
        [StructLayout(LayoutKind.Sequential)] private struct Point { public int X; public int Y; }
        [StructLayout(LayoutKind.Sequential)] private struct MouseLl { public Point Pt; public uint MouseData; public uint Flags; public uint Time; public IntPtr ExtraInfo; }
        private delegate IntPtr HookProc(int code, IntPtr wParam, IntPtr lParam);

        [DllImport("user32.dll", SetLastError = true)] private static extern IntPtr SetWindowsHookEx(int idHook, HookProc callback, IntPtr module, uint threadId);
        [DllImport("user32.dll", SetLastError = true)] private static extern bool UnhookWindowsHookEx(IntPtr hook);
        [DllImport("user32.dll")] private static extern IntPtr CallNextHookEx(IntPtr hook, int code, IntPtr wParam, IntPtr lParam);
        [DllImport("kernel32.dll", CharSet = CharSet.Auto)] private static extern IntPtr GetModuleHandle(string moduleName);

        public NativeHooks(MainForm form)
        {
            owner = form;
            keyboardProc = KeyboardCallback;
            mouseProc = MouseCallback;
            IntPtr module = GetModuleHandle(null);
            keyboardHook = SetWindowsHookEx(WhKeyboardLl, keyboardProc, module, 0);
            mouseHook = SetWindowsHookEx(WhMouseLl, mouseProc, module, 0);
            if (keyboardHook == IntPtr.Zero || mouseHook == IntPtr.Zero)
            {
                int error = Marshal.GetLastWin32Error();
                Dispose();
                throw new System.ComponentModel.Win32Exception(error, "Não foi possível iniciar a captura global de teclado e mouse.");
            }
        }

        public void ResetMouseTracking() { lastMouseX = Int32.MinValue; lastMouseY = Int32.MinValue; lastMouseMs = -1000; }

        private IntPtr KeyboardCallback(int code, IntPtr wParam, IntPtr lParam)
        {
            if (code < 0) return CallNextHookEx(keyboardHook, code, wParam, lParam);
            int message = wParam.ToInt32();
            KbdLl data = (KbdLl)Marshal.PtrToStructure(lParam, typeof(KbdLl));
            int vk = (int)data.VkCode;
            if (vk < 0 || vk >= keyDown.Length) return CallNextHookEx(keyboardHook, code, wParam, lParam);
            bool isDown = message == WmKeyDown || message == WmSysKeyDown;
            bool isUp = message == WmKeyUp || message == WmSysKeyUp;
            if ((data.Flags & LlkhfInjected) == 0)
            {
                if (isDown)
                {
                    bool repeatedKeyDown = keyDown[vk];
                    keyDown[vk] = true;
                    if (swallowed[vk]) return new IntPtr(1);
                    if (!repeatedKeyDown && owner.TryCaptureHotkey(vk, CurrentCtrl(), CurrentAlt(), CurrentShift(), CurrentWin()))
                    {
                        swallowed[vk] = true;
                        return new IntPtr(1);
                    }
                    if (!repeatedKeyDown && owner.TryHotkey(vk, CurrentCtrl(), CurrentAlt(), CurrentShift(), CurrentWin()))
                    {
                        swallowed[vk] = true;
                        return new IntPtr(1);
                    }
                    if (owner.IsMacroDeckForeground) ignoredKeyUp[vk] = true;
                    else if (owner.IsRecording) owner.RecordKey("keyDown", vk);
                }
                else if (isUp)
                {
                    keyDown[vk] = false;
                    if (swallowed[vk]) { swallowed[vk] = false; return new IntPtr(1); }
                    if (ignoredKeyUp[vk]) { ignoredKeyUp[vk] = false; return CallNextHookEx(keyboardHook, code, wParam, lParam); }
                    if (owner.IsRecording && !owner.IsMacroDeckForeground) owner.RecordKey("keyUp", vk);
                }
            }
            else if (isUp) keyDown[vk] = false;
            return CallNextHookEx(keyboardHook, code, wParam, lParam);
        }

        private bool CurrentCtrl() { return IsDown(0x11, 0xA2, 0xA3); }
        private bool CurrentAlt() { return IsDown(0x12, 0xA4, 0xA5); }
        private bool CurrentShift() { return IsDown(0x10, 0xA0, 0xA1); }
        private bool CurrentWin() { return IsDown(0x5B, 0x5C); }
        private bool IsDown(params int[] codes) { for (int i = 0; i < codes.Length; i++) if (keyDown[codes[i]]) return true; return false; }

        private IntPtr MouseCallback(int code, IntPtr wParam, IntPtr lParam)
        {
            if (code < 0) return CallNextHookEx(mouseHook, code, wParam, lParam);
            MouseLl data = (MouseLl)Marshal.PtrToStructure(lParam, typeof(MouseLl));
            if ((data.Flags & LlmhfInjected) != 0) return CallNextHookEx(mouseHook, code, wParam, lParam);
            int message = wParam.ToInt32();
            int buttonIndex = MouseButtonIndex(message, data.MouseData);
            if (message != WmMouseMove && owner.IsMacroDeckForeground)
            {
                if (buttonIndex >= 0 && IsMouseDown(message)) ignoredMouseUp[buttonIndex] = true;
                if (buttonIndex >= 0 && IsMouseUp(message)) ignoredMouseUp[buttonIndex] = false;
                return CallNextHookEx(mouseHook, code, wParam, lParam);
            }
            if (buttonIndex >= 0 && IsMouseUp(message) && ignoredMouseUp[buttonIndex])
            {
                ignoredMouseUp[buttonIndex] = false;
                return CallNextHookEx(mouseHook, code, wParam, lParam);
            }
            long now = owner.RecordElapsedMs;
            if (message == WmMouseMove)
            {
                int dx = data.Pt.X - lastMouseX, dy = data.Pt.Y - lastMouseY;
                if (lastMouseX == Int32.MinValue || (now - lastMouseMs >= 15 && (dx != 0 || dy != 0)))
                {
                    if (!owner.IsRecording || !owner.IsMacroDeckForeground) owner.RecordMouseMove(data.Pt.X, data.Pt.Y);
                    lastMouseX = data.Pt.X; lastMouseY = data.Pt.Y; lastMouseMs = now;
                }
            }
            else if (message == WmLButtonDown) owner.RecordMouseButton("mouseDown", 0, data.Pt.X, data.Pt.Y);
            else if (message == WmLButtonUp) owner.RecordMouseButton("mouseUp", 0, data.Pt.X, data.Pt.Y);
            else if (message == WmRButtonDown) owner.RecordMouseButton("mouseDown", 1, data.Pt.X, data.Pt.Y);
            else if (message == WmRButtonUp) owner.RecordMouseButton("mouseUp", 1, data.Pt.X, data.Pt.Y);
            else if (message == WmMButtonDown) owner.RecordMouseButton("mouseDown", 2, data.Pt.X, data.Pt.Y);
            else if (message == WmMButtonUp) owner.RecordMouseButton("mouseUp", 2, data.Pt.X, data.Pt.Y);
            else if (message == WmXButtonDown) owner.RecordMouseButton("mouseDown", ((int)(data.MouseData >> 16) == 1 ? 3 : 4), data.Pt.X, data.Pt.Y);
            else if (message == WmXButtonUp) owner.RecordMouseButton("mouseUp", ((int)(data.MouseData >> 16) == 1 ? 3 : 4), data.Pt.X, data.Pt.Y);
            else if (message == WmMouseWheel) owner.RecordWheel(data.Pt.X, data.Pt.Y, (short)(data.MouseData >> 16));
            return CallNextHookEx(mouseHook, code, wParam, lParam);
        }

        private static bool IsMouseDown(int message) { return message == WmLButtonDown || message == WmRButtonDown || message == WmMButtonDown || message == WmXButtonDown; }
        private static bool IsMouseUp(int message) { return message == WmLButtonUp || message == WmRButtonUp || message == WmMButtonUp || message == WmXButtonUp; }
        private static int MouseButtonIndex(int message, uint mouseData)
        {
            if (message == WmLButtonDown || message == WmLButtonUp) return 0;
            if (message == WmRButtonDown || message == WmRButtonUp) return 1;
            if (message == WmMButtonDown || message == WmMButtonUp) return 2;
            if (message == WmXButtonDown || message == WmXButtonUp) return ((int)(mouseData >> 16) == 1) ? 3 : 4;
            return -1;
        }

        public void Dispose()
        {
            if (keyboardHook != IntPtr.Zero) { UnhookWindowsHookEx(keyboardHook); keyboardHook = IntPtr.Zero; }
            if (mouseHook != IntPtr.Zero) { UnhookWindowsHookEx(mouseHook); mouseHook = IntPtr.Zero; }
        }
    }
}
