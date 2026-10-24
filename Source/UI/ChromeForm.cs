using System;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;
using System.Runtime.InteropServices;

namespace MacroDeck
{
    internal class ChromeForm : Form
    {
        private const int WmNcHitTest = 0x0084;
        private const int WmNcLButtonDown = 0x00A1;
        private const int HtCaption = 2;
        private const int HtLeft = 10;
        private const int HtRight = 11;
        private const int HtTop = 12;
        private const int HtTopLeft = 13;
        private const int HtTopRight = 14;
        private const int HtBottom = 15;
        private const int HtBottomLeft = 16;
        private const int HtBottomRight = 17;
        private const int ResizeBorder = 7;
        private const int WsSysMenu = 0x00080000;
        private const int WsMinimizeBox = 0x00020000;
        private const int WsMaximizeBox = 0x00010000;
        private const int FadeIn = 1;
        private const int FadeOutClose = 3;
        private const int FadeOutMinimize = 4;
        private const int FadeOutWindowState = 5;

        private readonly bool canResize;
        private readonly bool animateOpen;
        private readonly RoundedButton minimizeButton;
        private readonly RoundedButton maximizeButton;
        private readonly RoundedButton closeButton;
        private PictureBox applicationMark;
        private bool fadeInPending;
        private bool fadeInScheduled;
        private bool allowClose;
        private int animationMode;
        private float animationFromOpacity;
        private float animationToOpacity;
        private double animationDurationMs = 170;
        private DialogResult animatedDialogResult;
        private FormWindowState pendingWindowState;
        private FormWindowState lastWindowState = FormWindowState.Normal;
        private bool restoreFadePending;
        private bool stateFadePending;
        private readonly Timer animationTimer;
        private readonly Stopwatch animationClock = new Stopwatch();

        [DllImport("user32.dll")] private static extern bool ReleaseCapture();
        [DllImport("user32.dll", CharSet = CharSet.Auto)] private static extern IntPtr SendMessage(IntPtr hwnd, int message, IntPtr wParam, IntPtr lParam);

        protected Panel ChromeBar { get; private set; }
        protected Panel ContentPanel { get; private set; }
        protected Label ChromeTitle { get; private set; }

        protected void ApplyChromeLanguage(string language)
        {
            if (closeButton != null) closeButton.AccessibleName = Localization.Translate(language, "Fechar janela");
            if (maximizeButton != null) maximizeButton.AccessibleName = Localization.Translate(language, "Maximizar ou restaurar janela");
            if (minimizeButton != null) minimizeButton.AccessibleName = Localization.Translate(language, "Minimizar janela");
        }

        protected ChromeForm(bool resizable)
        {
            canResize = resizable;
            animateOpen = true;
            FormBorderStyle = FormBorderStyle.None;
            ShowInTaskbar = resizable;
            MinimizeBox = resizable;
            MaximizeBox = resizable;
            StartPosition = FormStartPosition.CenterScreen;
            AutoScaleMode = AutoScaleMode.None;
            AutoScaleDimensions = new SizeF(96f, 96f);
            AutoScaleMode = AutoScaleMode.Dpi;
            Font = FontManager.Create(9f, FontStyle.Regular);
            DoubleBuffered = true;
            try
            {
                Icon appIcon = System.Drawing.Icon.ExtractAssociatedIcon(Application.ExecutablePath);
                if (appIcon != null)
                {
                    Icon = appIcon;
                    ShowIcon = true;
                    applicationMark = new PictureBox
                    {
                        Image = appIcon.ToBitmap(),
                        SizeMode = PictureBoxSizeMode.Zoom,
                        Location = new Point(10, 9),
                        Size = new Size(20, 20),
                        BackColor = Color.Transparent
                    };
                }
            }
            catch { }
            animationTimer = new Timer { Interval = 15 };
            animationTimer.Tick += AnimateWindow;

            ChromeBar = new Panel { Dock = DockStyle.Top, Height = 38, BackColor = Color.FromArgb(19, 22, 28) };
            ContentPanel = new Panel { Dock = DockStyle.Fill, BackColor = Color.FromArgb(19, 22, 28) };
            ChromeTitle = new Label { Text = "MacroDeck", AutoSize = true, Location = new Point(applicationMark == null ? 14 : 36, 10), Font = FontManager.Create(8.5f, FontStyle.Bold), ForeColor = Color.White };
            if (applicationMark != null) ChromeBar.Controls.Add(applicationMark);
            ChromeBar.Controls.Add(ChromeTitle);

            closeButton = MakeChromeButton("", 44);
            closeButton.Icon = "close";
            closeButton.AccessibleName = "Fechar janela";
            closeButton.Click += delegate { Close(); };
            ChromeBar.Controls.Add(closeButton);

            if (canResize)
            {
                maximizeButton = MakeChromeButton("", 38);
                maximizeButton.Icon = "maximize";
                maximizeButton.AccessibleName = "Maximizar ou restaurar janela";
                maximizeButton.Click += delegate { ToggleMaximized(); };
                minimizeButton = MakeChromeButton("", 38);
                minimizeButton.Icon = "minimize";
                minimizeButton.AccessibleName = "Minimizar janela";
                minimizeButton.Click += delegate { BeginMinimize(); };
                ChromeBar.Controls.Add(maximizeButton);
                ChromeBar.Controls.Add(minimizeButton);
            }

            ChromeBar.MouseDown += DragWindow;
            ChromeTitle.MouseDown += DragWindow;
            ChromeBar.DoubleClick += delegate { if (canResize) ToggleMaximized(); };
            ChromeTitle.DoubleClick += delegate { if (canResize) ToggleMaximized(); };
            ChromeBar.Resize += PositionChromeButtons;
            Controls.Add(ContentPanel);
            Controls.Add(ChromeBar);
            PositionChromeButtons(null, EventArgs.Empty);
        }

        protected override CreateParams CreateParams
        {
            get
            {
                CreateParams cp = base.CreateParams;
                if (canResize)
                {
                    cp.Style |= WsSysMenu | WsMinimizeBox | WsMaximizeBox;
                }
                return cp;
            }
        }

        protected override void SetVisibleCore(bool value)
        {
            if (value && animateOpen && !IsDisposed)
            {
                allowClose = false;
                if (animationTimer != null && animationTimer.Enabled) animationTimer.Stop();
                animationMode = 0;
                Opacity = 0;
                fadeInPending = true;
            }
            else if (!value) fadeInPending = false;
            base.SetVisibleCore(value);
        }

        protected override void OnShown(EventArgs e)
        {
            base.OnShown(e);
            if (fadeInPending)
            {
                fadeInPending = false;
                QueueFadeIn();
            }
        }

        protected override void OnVisibleChanged(EventArgs e)
        {
            base.OnVisibleChanged(e);
            if (Visible && fadeInPending)
            {
                fadeInPending = false;
                QueueFadeIn();
            }
        }

        protected override void OnActivated(EventArgs e)
        {
            base.OnActivated(e);
            if (ChromeBar != null && closeButton != null) PositionChromeButtons(null, EventArgs.Empty);
        }

        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            if (!allowClose && e.CloseReason == CloseReason.UserClosing)
            {
                e.Cancel = true;
                BeginCloseAnimation();
                return;
            }
            base.OnFormClosing(e);
        }

        protected override void OnTextChanged(EventArgs e)
        {
            base.OnTextChanged(e);
            if (ChromeTitle != null) ChromeTitle.Text = Text;
        }

        protected override void OnSizeChanged(EventArgs e)
        {
            base.OnSizeChanged(e);
            if (Width < 2 || Height < 2) return;
            if (canResize && WindowState == FormWindowState.Maximized)
            {
                Region previous = Region;
                Region = null;
                if (previous != null) previous.Dispose();
            }
            else
            {
                using (GraphicsPath path = RoundedControls.Path(new Rectangle(0, 0, Width, Height), 8))
                {
                    Region previous = Region;
                    Region = new Region(path);
                    if (previous != null) previous.Dispose();
                }
            }
            if (ChromeBar != null && closeButton != null) PositionChromeButtons(null, EventArgs.Empty);
        }

        protected override void OnResize(EventArgs e)
        {
            FormWindowState previousState = lastWindowState;
            base.OnResize(e);
            lastWindowState = WindowState;
            if (previousState == FormWindowState.Minimized && WindowState != FormWindowState.Minimized && restoreFadePending)
            {
                restoreFadePending = false;
                QueueFadeIn();
            }
        }

        protected void ApplyChromeTheme(Color background, Color foreground, Color surface)
        {
            ChromeBar.BackColor = background;
            ContentPanel.BackColor = background;
            ChromeTitle.ForeColor = foreground;
            foreach (Control control in ChromeBar.Controls)
            {
                RoundedButton button = control as RoundedButton;
                if (button != null)
                {
                    button.BackColor = surface;
                    button.ForeColor = foreground;
                    button.Invalidate();
                }
            }
            Invalidate();
        }

        private RoundedButton MakeChromeButton(string text, int width)
        {
            return new RoundedButton {
                Text = text, Size = new Size(width, 26), CornerRadius = 7,
                Font = FontManager.Create(9f, FontStyle.Bold), BackColor = Color.FromArgb(36, 40, 48),
                ForeColor = Color.White, Anchor = AnchorStyles.Top | AnchorStyles.Right
            };
        }

        private void PositionChromeButtons(object sender, EventArgs e)
        {
            int right = ChromeBar.ClientSize.Width - 7;
            closeButton.SetBounds(right - closeButton.Width, 6, closeButton.Width, 26);
            if (canResize)
            {
                maximizeButton.SetBounds(closeButton.Left - 3 - maximizeButton.Width, 6, maximizeButton.Width, 26);
                minimizeButton.SetBounds(maximizeButton.Left - 3 - minimizeButton.Width, 6, minimizeButton.Width, 26);
                maximizeButton.Icon = WindowState == FormWindowState.Maximized ? "restore" : "maximize";
            }
        }

        private void DragWindow(object sender, MouseEventArgs e)
        {
            if (e.Button != MouseButtons.Left || canResize && e.Clicks > 1) return;
            ReleaseCapture();
            SendMessage(Handle, WmNcLButtonDown, new IntPtr(HtCaption), IntPtr.Zero);
        }

        private void ToggleMaximized()
        {
            if (!canResize || animationMode != 0) return;
            pendingWindowState = WindowState == FormWindowState.Maximized
                ? FormWindowState.Normal
                : FormWindowState.Maximized;
            StartOpacityAnimation(FadeOutWindowState, Opacity, 0, 105);
        }

        private void BeginMinimize()
        {
            if (!canResize || WindowState == FormWindowState.Minimized || animationMode != 0) return;
            StartOpacityAnimation(FadeOutMinimize, Opacity, 0, 115);
        }

        private void BeginCloseAnimation()
        {
            if (animationMode == FadeOutClose) return;
            StartOpacityAnimation(FadeOutClose, Opacity, 0, 150);
        }

        private void BeginFadeIn()
        {
            StartOpacityAnimation(FadeIn, Opacity, 1, 180);
        }

        protected void CloseWithAnimation(DialogResult result)
        {
            animatedDialogResult = result;
            BeginCloseAnimation();
        }

        private void QueueFadeIn()
        {
            if (fadeInScheduled || IsDisposed || !IsHandleCreated) return;
            fadeInScheduled = true;
            try
            {
                BeginInvoke((MethodInvoker)delegate
                {
                    fadeInScheduled = false;
                    if (IsDisposed || !Visible) return;
                    PerformLayout();
                    Invalidate(true);
                    Update();
                    BeginFadeIn();
                });
            }
            catch (InvalidOperationException) { fadeInScheduled = false; }
        }

        private void StartOpacityAnimation(int mode, double from, double to, double durationMs)
        {
            StopAnimation();
            animationMode = mode;
            animationFromOpacity = (float)Math.Max(0, Math.Min(1, from));
            animationToOpacity = (float)Math.Max(0, Math.Min(1, to));
            animationDurationMs = Math.Max(1, durationMs);
            animationClock.Restart();
            animationTimer.Start();
        }

        private void StopAnimation()
        {
            if (animationTimer.Enabled) animationTimer.Stop();
            animationClock.Stop();
            animationMode = 0;
        }

        private void AnimateWindow(object sender, EventArgs e)
        {
            float progress = Math.Min(1f, (float)(animationClock.Elapsed.TotalMilliseconds / animationDurationMs));
            float eased = progress * progress * (3f - 2f * progress);
            if (animationMode == FadeIn || animationMode == FadeOutClose || animationMode == FadeOutMinimize || animationMode == FadeOutWindowState)
            {
                Opacity = animationFromOpacity + (animationToOpacity - animationFromOpacity) * eased;
            }
            if (progress < 1f) return;
            animationTimer.Stop();
            animationClock.Stop();
            int completedMode = animationMode; animationMode = 0;
            if (completedMode == FadeIn) Opacity = 1;
            else if (completedMode == FadeOutClose)
            {
                if (animatedDialogResult != DialogResult.None)
                {
                    DialogResult = animatedDialogResult;
                    animatedDialogResult = DialogResult.None;
                }
                allowClose = true;
                Close();
            }
            else if (completedMode == FadeOutMinimize)
            {
                restoreFadePending = true;
                WindowState = FormWindowState.Minimized;
            }
            else if (completedMode == FadeOutWindowState)
            {
                stateFadePending = true;
                WindowState = pendingWindowState;
                try
                {
                    BeginInvoke((MethodInvoker)delegate
                    {
                        if (!stateFadePending || IsDisposed || WindowState == FormWindowState.Minimized) return;
                        stateFadePending = false;
                        QueueFadeIn();
                    });
                }
                catch (InvalidOperationException) { stateFadePending = false; }
            }
        }

        protected override void WndProc(ref Message message)
        {
            base.WndProc(ref message);
            if (!canResize || WindowState == FormWindowState.Maximized || message.Msg != WmNcHitTest || message.Result.ToInt32() != 1) return;

            int packed = message.LParam.ToInt32();
            Point clientPoint = PointToClient(new Point((short)(packed & 0xffff), (short)((packed >> 16) & 0xffff)));
            bool left = clientPoint.X < ResizeBorder;
            bool right = clientPoint.X >= ClientSize.Width - ResizeBorder;
            bool top = clientPoint.Y < ResizeBorder;
            bool bottom = clientPoint.Y >= ClientSize.Height - ResizeBorder;
            if (top && left) message.Result = new IntPtr(HtTopLeft);
            else if (top && right) message.Result = new IntPtr(HtTopRight);
            else if (bottom && left) message.Result = new IntPtr(HtBottomLeft);
            else if (bottom && right) message.Result = new IntPtr(HtBottomRight);
            else if (left) message.Result = new IntPtr(HtLeft);
            else if (right) message.Result = new IntPtr(HtRight);
            else if (top) message.Result = new IntPtr(HtTop);
            else if (bottom) message.Result = new IntPtr(HtBottom);
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing && animationTimer != null) { animationTimer.Stop(); animationTimer.Dispose(); }
            base.Dispose(disposing);
        }
    }

    internal static class UiAnimationClock
    {
        private static int activeAnimations;
        private static bool highResolutionTimer;

        [DllImport("winmm.dll", EntryPoint = "timeBeginPeriod")] private static extern uint BeginTimerPeriod(uint period);
        [DllImport("winmm.dll", EntryPoint = "timeEndPeriod")] private static extern uint EndTimerPeriod(uint period);
        [DllImport("dwmapi.dll")] private static extern int DwmFlush();

        public static void Begin()
        {
            if (System.Threading.Interlocked.Increment(ref activeAnimations) != 1) return;
            try { highResolutionTimer = BeginTimerPeriod(1) == 0; }
            catch { highResolutionTimer = false; }
        }

        public static void End()
        {
            int remaining = System.Threading.Interlocked.Decrement(ref activeAnimations);
            if (remaining > 0) return;
            System.Threading.Interlocked.Exchange(ref activeAnimations, 0);
            if (!highResolutionTimer) return;
            highResolutionTimer = false;
            try { EndTimerPeriod(1); }
            catch { }
        }

        public static void Flush()
        {
            try { DwmFlush(); }
            catch { }
        }
    }
}
