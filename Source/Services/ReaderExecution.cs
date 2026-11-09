using System;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows.Forms;

namespace MacroDeck
{
    // The desktop boundary is injectable so failure cases never need real clicks in tests.
    internal interface IReaderDesktop
    {
        void CheckWindow(Point? target);
        Bitmap Capture(Rectangle region);
        void Move(Point target);
        Point CursorPosition { get; }
        void Button(bool down);
    }

    internal sealed class ReaderExecution : IDisposable
    {
        private readonly CancellationTokenSource cancellation;
        private readonly IReaderDesktop desktop;
        private readonly CancellationTokenRegistration registration;
        private bool buttonPressed;
        public CancellationToken Token { get { return cancellation.Token; } }
        public ReaderExecution(CancellationTokenSource cancellation, IReaderDesktop desktop)
        {
            this.cancellation = cancellation; this.desktop = desktop;
            registration = cancellation.Token.Register(Release);
        }

        private void Release()
        {
            lock(cancellation) {
                if(!buttonPressed)return;
                buttonPressed=false;
                desktop.Button(false);
            }
        }
        public void Dispose() { Release(); registration.Dispose(); }

        public static void Cancel(CancellationTokenSource cancellation)
        {
            // Serialize cancellation with input submission: after Cancel returns no new
            // move/button-down can be submitted by this execution. Button-up is cleanup.
            lock (cancellation) { try { cancellation.Cancel(); } catch(ObjectDisposedException) { } }
        }
        public void Check() { Token.ThrowIfCancellationRequested(); desktop.CheckWindow(null); }
        public Bitmap Capture(Rectangle region) { Check(); return desktop.Capture(region); }
        public void Wait(int milliseconds)
        {
            int remaining = Math.Max(0, milliseconds);
            do {
                Check();
                int slice = Math.Min(remaining, 25);
                if (slice > 0 && Token.WaitHandle.WaitOne(slice)) Token.ThrowIfCancellationRequested();
                remaining -= slice;
            } while (remaining > 0);
            Check();
        }
        public void Act(VisualRecognitionMatch match, Bitmap observed, bool click, Action checkDeadline = null, Func<bool> confirmTarget = null)
        {
            lock (cancellation) {
                Check(); if(checkDeadline!=null)checkDeadline(); desktop.CheckWindow(match.Center); desktop.Move(match.Center);
            }
            Wait(30);
            // Hovering or a moving menu may change the target after recognition. Fail
            // closed instead of clicking the old coordinates, including in test mode.
            using (Bitmap fresh = Capture(match.Bounds))
                if (!VisualRecognitionService.SameTargetPixels(observed, fresh) && (confirmTarget == null || !confirmTarget()))
                    throw new InvalidOperationException("The target changed before the click. Try again after the menu stops moving.");
            lock (cancellation) {
                Check(); if(checkDeadline!=null)checkDeadline(); desktop.CheckWindow(match.Center);
                if (desktop.CursorPosition != match.Center)
                    throw new InvalidOperationException("The cursor moved before the click. Run the workflow again.");
                if (!click) return;
                buttonPressed=true;
                try { desktop.Button(true); }
                catch { Release(); throw; }
            }
            try { Wait(35); }
            finally { Release(); }
        }
    }

    internal sealed class WindowsReaderDesktop : IReaderDesktop
    {
        [StructLayout(LayoutKind.Sequential)] private struct Rect { public int Left, Top, Right, Bottom; }
        [DllImport("user32.dll")] private static extern IntPtr GetForegroundWindow();
        [DllImport("user32.dll")] private static extern IntPtr GetAncestor(IntPtr window, uint flags);
        [DllImport("user32.dll")] private static extern IntPtr WindowFromPoint(Point point);
        [DllImport("user32.dll")] private static extern bool GetWindowRect(IntPtr window, out Rect rect);
        [DllImport("user32.dll")] private static extern bool SetCursorPos(int x, int y);
        [DllImport("user32.dll")] private static extern bool GetCursorPos(out Point point);
        [DllImport("user32.dll")] private static extern short GetAsyncKeyState(int key);
        [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(IntPtr window, out uint process);
        [DllImport("user32.dll")] private static extern void mouse_event(uint flags, uint x, uint y, uint data, UIntPtr extra);
        [DllImport("user32.dll")] private static extern uint GetDpiForWindow(IntPtr window);
        private readonly IntPtr window;
        private readonly Rect bounds;
        private readonly string monitors;
        private readonly uint dpi;
        public WindowsReaderDesktop()
        {
            window = GetForegroundWindow();
            uint process; GetWindowThreadProcessId(window, out process);
            if (window == IntPtr.Zero || process == (uint)System.Diagnostics.Process.GetCurrentProcess().Id || !GetWindowRect(window, out bounds))
                throw new InvalidOperationException("The active window changed. Run again with the game in the foreground.");
            monitors = MonitorLayout(); dpi = WindowDpi(window);
        }
        private static uint WindowDpi(IntPtr window) { try { return GetDpiForWindow(window); } catch (EntryPointNotFoundException) { return 0; } }
        private static string MonitorLayout()
        {
            System.Text.StringBuilder text = new System.Text.StringBuilder();
            foreach (Screen screen in Screen.AllScreens) text.Append(screen.DeviceName).Append(screen.Bounds.ToString());
            return text.ToString();
        }
        public void CheckWindow(Point? target)
        {
            if ((GetAsyncKeyState((int)Keys.Escape) & 0x8000) != 0) throw new OperationCanceledException();
            Rect current;
            if (GetForegroundWindow() != window || !GetWindowRect(window, out current) || !current.Equals(bounds) ||
                monitors != MonitorLayout() || dpi != WindowDpi(window) ||
                (target.HasValue && GetAncestor(WindowFromPoint(target.Value), 2) != window))
                throw new InvalidOperationException("The window or display changed. Run again with the game in the foreground.");
        }
        public Bitmap Capture(Rectangle region) { return VisualRecognitionService.Capture(region); }
        public void Move(Point target) { if (!SetCursorPos(target.X,target.Y)) throw new InvalidOperationException("Windows could not move the cursor to the recognized target."); }
        public Point CursorPosition { get { Point p; if (!GetCursorPos(out p)) throw new InvalidOperationException("Windows could not read the cursor position."); return p; } }
        public void Button(bool down) { mouse_event(down ? 0x0002u : 0x0004u,0,0,0,UIntPtr.Zero); }
    }
}
