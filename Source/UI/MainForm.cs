using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Windows.Forms;
using System.Web.Script.Serialization;

namespace MacroDeck
{
    internal sealed class MainForm : ChromeForm
    {
        private const int MaxEvents = 250000;
        private const int PlaybackHotkeyId = 0x4D44;
        private bool nativePlaybackRegistered;
        [DllImport("user32.dll", SetLastError=true)] private static extern bool RegisterHotKey(IntPtr window,int id,uint modifiers,uint key);
        [DllImport("user32.dll")] private static extern bool UnregisterHotKey(IntPtr window,int id);

        public bool IsNativePlaybackHotkey(int key,bool ctrl,bool alt,bool shift,bool win)
        {
            return nativePlaybackRegistered && playHotkey!=null && playHotkey.Key==key && playHotkey.Ctrl==ctrl && playHotkey.Alt==alt && playHotkey.Shift==shift && playHotkey.Win==win;
        }

        private void RefreshNativePlaybackHotkey()
        {
            if(!IsHandleCreated)return;
            if(nativePlaybackRegistered)UnregisterHotKey(Handle,PlaybackHotkeyId);
            nativePlaybackRegistered=false;
            if(playHotkey==null || capturePlay || captureRecord || Disposing || IsDisposed)return;
            uint modifiers=0x4000u|(playHotkey.Alt?1u:0)|(playHotkey.Ctrl?2u:0)|(playHotkey.Shift?4u:0)|(playHotkey.Win?8u:0);
            nativePlaybackRegistered=RegisterHotKey(Handle,PlaybackHotkeyId,modifiers,(uint)playHotkey.Key);
        }

        protected override void OnHandleDestroyed(EventArgs e)
        {
            if(nativePlaybackRegistered)UnregisterHotKey(Handle,PlaybackHotkeyId);
            nativePlaybackRegistered=false;
            base.OnHandleDestroyed(e);
        }
        private const uint InputMouse = 0;
        private const uint MouseFlagWheel = 0x0800;
        private const uint CreateWaitableTimerHighResolution = 0x00000002;
        private const uint TimerModifyStateAndSynchronize = 0x00100002;
        private const uint WaitObject0 = 0;
        private const uint WaitFailed = 0xFFFFFFFF;
        private const uint InfiniteWait = 0xFFFFFFFF;
        private const long TimerSpinThresholdUs = 500;
        private readonly string configPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "MacroDeck", "settings.json");
        private readonly JavaScriptSerializer json = new JavaScriptSerializer { MaxJsonLength = Int32.MaxValue };
        private readonly object eventsLock = new object();
        private readonly List<MacroEvent> recordedEvents = new List<MacroEvent>();
        private static readonly uint[] MouseDownFlags = { 0x0002, 0x0008, 0x0020, 0x0080, 0x0080 };
        private static readonly uint[] MouseUpFlags = { 0x0004, 0x0010, 0x0040, 0x0100, 0x0100 };
        private static readonly uint[] MouseData = { 0, 0, 0, 1, 2 };
        private static readonly int CurrentProcessId = Process.GetCurrentProcess().Id;

        [StructLayout(LayoutKind.Sequential)]
        private struct NativePoint
        {
            public int X;
            public int Y;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct MouseInput
        {
            public int X;
            public int Y;
            public uint Data;
            public uint Flags;
            public uint Time;
            public IntPtr ExtraInfo;
        }

        [StructLayout(LayoutKind.Explicit)]
        private struct InputUnion
        {
            [FieldOffset(0)] public MouseInput Mouse;
            [FieldOffset(0)] public KeyboardInput Keyboard;
            [FieldOffset(0)] public HardwareInput Hardware;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct KeyboardInput
        {
            public ushort VirtualKey;
            public ushort ScanCode;
            public uint Flags;
            public uint Time;
            public IntPtr ExtraInfo;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct HardwareInput
        {
            public uint Message;
            public ushort ParameterLow;
            public ushort ParameterHigh;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct NativeInput
        {
            public uint Type;
            public InputUnion Data;
        }

        private AppSettings settings;
        private string uiLanguage = "pt-BR";
        private bool legacyMacroDefaultCycleInfinite;
        private int legacyMacroDefaultIntervalMinutes;
        private int legacyMacroDefaultIntervalSeconds;
        private NativeHooks hooks;
        private List<MacroEvent> pendingEvents;
        private int? recordingStartMouseX;
        private int? recordingStartMouseY;
        private MacroFile activeMacro;
        private string activeMacroPath = "";
        private readonly Stopwatch recordClock = new Stopwatch();
        private volatile bool recording;
        private volatile bool playing;
        private CancellationTokenSource playbackCancel;
        private int playbackGeneration;
        private bool closeAfterPlayback;
        private bool recordLimitQueued;
        private bool captureRecord;
        private bool capturePlay;
        private bool refreshingMacros;
        private bool applyingMacroSettings;
        private bool pendingPreviewActive;
        private bool activatingMacro;
        private bool macroSettingsDirty;
        private bool folderPickerInProgress;
        private int activePageIndex;
        private int pageTransitionTarget;
        private int macroLoadGeneration;
        private string currentMonitorDevice = "";

        private Hotkey recordHotkey;
        private Hotkey playHotkey;
        private ThemePalette palette;
        private OptionsForm optionsDialog;
        private AboutForm aboutDialog;
        private TrayIconController trayIcon;
        private bool hiddenToTray;
        private FormWindowState trayRestoreState = FormWindowState.Normal;

        private Label titleLabel, subtitleLabel, statusLabel, eventsLabel;
        private Label recordKeyLabel, playKeyLabel, recordKeyValue, playKeyValue, keyStatusLabel, folderLabel;
        private Label autoClickerTitle, autoClickerHint;
        private RoundedButton recordButton, playButton, themeButton, folderButton;
        private RoundedButton setRecordKeyButton, setPlayKeyButton, refreshButton, deleteSelectedButton, deleteAllButton, saveButton, useMacroButton;
        private RoundedButton macroNavButton, clickerNavButton, visualNavButton, menuButton;
        private ModernTextBox nameBox, folderBox;
        private MacroSelector macroCombo;
        private AnimatedToggle infiniteCheck;
        private AnimatedToggle variationCheck, smoothReturnCheck;
        private StepperInput minutesInput, secondsInput;
        private Panel headerPanel, autoClickerPage;
        private BufferedPanel pageHost;
        private ModernScrollViewer macroPage;
        private VisualRecognitionPage visualRecognitionPage;
        private ModernMenuPopup appMenu;
        private ModernToolTip toolTips;
        private System.Windows.Forms.Timer themeTransitionTimer;
        private readonly Stopwatch themeTransitionClock = new Stopwatch();
        private ThemeTransitionSurface themeTransitionSurface;
        private System.Windows.Forms.Timer pageTransitionTimer;
        private PageTransitionSurface transitionSurface;
        private readonly Stopwatch pageTransitionClock = new Stopwatch();

        [DllImport("user32.dll")] private static extern IntPtr GetForegroundWindow();
        [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(IntPtr hwnd, out uint processId);
        [DllImport("user32.dll", SetLastError = true)] private static extern bool SetCursorPos(int x, int y);
        [DllImport("user32.dll", SetLastError = true)] private static extern bool GetCursorPos(out NativePoint point);
        [DllImport("user32.dll")] private static extern void keybd_event(byte vk, byte scan, uint flags, UIntPtr extra);
        [DllImport("user32.dll", SetLastError = true)] private static extern uint SendInput(uint inputCount, NativeInput[] inputs, int inputSize);
        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true, EntryPoint = "CreateWaitableTimerExW")]
        private static extern IntPtr CreateWaitableTimerEx(IntPtr attributes, string name, uint flags, uint desiredAccess);
        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern IntPtr CreateWaitableTimer(IntPtr attributes, bool manualReset, string name);
        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool SetWaitableTimer(IntPtr timer, ref long dueTime, int period, IntPtr completionRoutine, IntPtr argument, bool resume);
        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern uint WaitForMultipleObjects(uint count, IntPtr[] handles, bool waitAll, uint milliseconds);
        [DllImport("kernel32.dll")] private static extern bool CloseHandle(IntPtr handle);
        [DllImport("winmm.dll")] private static extern uint timeBeginPeriod(uint period);
        [DllImport("winmm.dll")] private static extern uint timeEndPeriod(uint period);

        public long RecordElapsedMs { get { return recordClock.ElapsedMilliseconds; } }
        public long RecordElapsedMicroseconds
        {
            get
            {
                long elapsedTicks = recordClock.ElapsedTicks;
                return elapsedTicks * 1000000L / Stopwatch.Frequency;
            }
        }
        public bool IsRecording { get { return recording; } }
        public bool IsMacroDeckForeground
        {
            get
            {
                IntPtr foreground = GetForegroundWindow();
                if (foreground == IntPtr.Zero) return false;
                uint processId; GetWindowThreadProcessId(foreground, out processId);
                return processId == (uint)CurrentProcessId;
            }
        }

        public MainForm() : base(true)
        {
            settings = LoadSettings();
            legacyMacroDefaultCycleInfinite = settings.CycleInfinite;
            legacyMacroDefaultIntervalMinutes = settings.IntervalMinutes;
            legacyMacroDefaultIntervalSeconds = settings.IntervalSeconds;
            uiLanguage = settings.Language;
            ApplyChromeLanguage(uiLanguage);
            FontManager.SetPreferredFamily(settings.FontFamily);
            palette = ThemePalette.From(settings);
            Text = "MacroDeck";
            Name = "MacroDeck";
            currentMonitorDevice = Screen.PrimaryScreen.DeviceName;
            MinimumSize = new Size(820, 640);
            Size = new Size(1080, 850);
            BackColor = palette.Background;
            ForeColor = palette.Text;
            Font = FontManager.Create(9f, FontStyle.Regular);
            DoubleBuffered = true;
            BuildUi();
            ApplyFontTree(this);

            try { hooks = new NativeHooks(this); }
            catch (Exception ex)
            {
                MessageBox.Show(this, Tr(ex.Message) + "\r\n\r\n" + Tr("Execute este programa em uma sessão interativa do Windows."), Tr("Captura indisponível"), MessageBoxButtons.OK, MessageBoxIcon.Error);
            }

            recordHotkey = Hotkey.Parse(settings.RecordHotkey);
            playHotkey = Hotkey.Parse(settings.PlayHotkey);
            UpdateHotkeyLabels();
            ApplyTheme();
            FormClosing += OnClosing;
            UpdateRuntimeUi("Pronto", "", false);
            trayIcon = new TrayIconController(Icon, Tr, RestoreFromTray, Close);
        }

        private AppSettings LoadSettings()
        {
            AppSettings s = AppSettings.Defaults();
            try
            {
                if (!File.Exists(configPath)) return s;
                AppSettings loaded = json.Deserialize<AppSettings>(File.ReadAllText(configPath, Encoding.UTF8));
                if (loaded == null) return s;
                if (!String.IsNullOrWhiteSpace(loaded.Folder)) s.Folder = loaded.Folder;
                if (Hotkey.Parse(loaded.RecordHotkey) != null) s.RecordHotkey = loaded.RecordHotkey;
                if (Hotkey.Parse(loaded.PlayHotkey) != null) s.PlayHotkey = loaded.PlayHotkey;
                s.DarkTheme = loaded.DarkTheme;
                s.FontFamily = loaded.FontFamily ?? "";
                if (String.Equals(loaded.Language, "en-US", StringComparison.OrdinalIgnoreCase)) s.Language = "en-US";
                else if (String.Equals(loaded.Language, "pt-BR", StringComparison.OrdinalIgnoreCase)) s.Language = "pt-BR";
                s.BackgroundColor = loaded.BackgroundColor ?? "";
                s.CardColor = loaded.CardColor ?? "";
                s.TextColor = loaded.TextColor ?? "";
                s.AccentColor = loaded.AccentColor ?? "";
                s.DarkBackgroundColor = loaded.DarkBackgroundColor ?? "";
                s.DarkCardColor = loaded.DarkCardColor ?? "";
                s.DarkTextColor = loaded.DarkTextColor ?? "";
                s.DarkAccentColor = loaded.DarkAccentColor ?? "";
                s.LightBackgroundColor = loaded.LightBackgroundColor ?? "";
                s.LightCardColor = loaded.LightCardColor ?? "";
                s.LightTextColor = loaded.LightTextColor ?? "";
                s.LightAccentColor = loaded.LightAccentColor ?? "";
                s.CycleInfinite = loaded.CycleInfinite || loaded.Repeat;
                int minutes = Math.Max(0, Math.Min(loaded.IntervalMinutes, 9999));
                int seconds = Math.Max(0, Math.Min(loaded.IntervalSeconds, 59));
                if (minutes == 0 && seconds == 0 && loaded.Interval > 0)
                {
                    decimal legacyTotalSeconds = loaded.Interval * (loaded.IntervalUnit == "minutos" ? 60m : 1m);
                    int roundedSeconds = (int)Math.Round(legacyTotalSeconds, MidpointRounding.AwayFromZero);
                    minutes = roundedSeconds / 60;
                    seconds = roundedSeconds % 60;
                }
                s.IntervalMinutes = minutes;
                s.IntervalSeconds = seconds;
                s.Repeat = s.CycleInfinite;
                s.Interval = minutes * 60 + seconds;
                s.IntervalUnit = "segundos";
                s.SelectedFile = loaded.SelectedFile ?? "";
                s.ReaderMonitorDevice = loaded.ReaderMonitorDevice ?? "";
                s.VaryPlayback = loaded.VaryPlayback;
                s.SmoothCursorReturn = loaded.SmoothCursorReturn;
                s.AlwaysRunAsAdministrator = loaded.AlwaysRunAsAdministrator;
            }
            catch { }
            return s;
        }

        private void SaveSettings()
        {
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(configPath));
                WriteMacroAtomically(configPath, json.Serialize(settings));
            }
            catch { }
        }

        private void BuildUi()
        {
            SuspendLayout();
            BufferedTableLayoutPanel root = new BufferedTableLayoutPanel {
                Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 2,
                Padding = new Padding(22, 14, 22, 16), Tag = "surface"
            };
            root.SuspendLayout();
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 104));
            root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            ContentPanel.Controls.Add(root);

            headerPanel = new BufferedPanel { Dock = DockStyle.Fill, Tag = "surface" };
            titleLabel = new Label { Text = "MacroDeck", AutoSize = true, Location = new Point(0, 0), Font = FontManager.Create(22f, FontStyle.Bold), ForeColor = palette.Text };
            subtitleLabel = new Label { Text = Tr("Grave, organize e reproduza ações."), AutoSize = true, Location = new Point(2, 42), Font = FontManager.Create(8.5f, FontStyle.Regular), ForeColor = palette.Muted, Tag = "muted" };
            macroNavButton = MakeButton("Macro", 0, 25, 88, 38, true);
            macroNavButton.Click += delegate { SetActivePage(0); };
            visualNavButton = MakeButton(Tr("Leitor"), 0, 25, 88, 38, false);
            visualNavButton.Click += delegate { SetActivePage(2); };
            clickerNavButton = MakeButton("Auto-Clicker", 0, 25, 128, 38, false);
            clickerNavButton.Click += delegate { SetActivePage(1); };
            themeButton = MakeButton(Tr("Claro"), 0, 25, 94, 38, false);
            themeButton.Icon = "sun";
            themeButton.Click += delegate { BeginThemeTransition(); };
            menuButton = MakeButton("Menu", 0, 25, 100, 38, false);
            menuButton.Icon = "chevron";
            menuButton.Click += delegate { ShowAppMenu(); };
            headerPanel.Controls.Add(titleLabel); headerPanel.Controls.Add(subtitleLabel);
            headerPanel.Controls.Add(macroNavButton); headerPanel.Controls.Add(visualNavButton); headerPanel.Controls.Add(clickerNavButton);
            headerPanel.Controls.Add(themeButton); headerPanel.Controls.Add(menuButton);
            headerPanel.Resize += PositionHeaderActions;
            root.Controls.Add(headerPanel, 0, 0);

            pageHost = new BufferedPanel { Dock = DockStyle.Fill, Tag = "surface" };
            pageHost.SuspendLayout();
            root.Controls.Add(pageHost, 0, 1);
            BuildMacroPage();
            BuildAutoClickerPage();
            BuildVisualRecognitionPage();
            pageHost.ResumeLayout(true);

            CreateAppMenu();
            toolTips = new ModernToolTip();
            toolTips.SetToolTip(deleteSelectedButton, Tr("Apaga somente o macro selecionado. O aplicativo pedirá confirmação."), this);
            toolTips.SetToolTip(deleteAllButton, Tr("Apaga todos os macros válidos desta pasta. O aplicativo pedirá confirmação."), this);
            toolTips.SetToolTip(saveButton, Tr("Salva a gravação atual como um novo macro."), this);
            toolTips.SetToolTip(refreshButton, Tr("Atualiza a lista de macros salvos."), this);
            PositionHeaderActions(null, EventArgs.Empty);
            ApplyActivePage(activePageIndex);
            root.ResumeLayout(true);
            ResumeLayout(true);
        }

        private void PositionHeaderActions(object sender, EventArgs e)
        {
            if (headerPanel == null || macroNavButton == null) return;
            int right = headerPanel.ClientSize.Width;
            menuButton.Left = right - menuButton.Width;
            themeButton.Left = menuButton.Left - 8 - themeButton.Width;
            clickerNavButton.Left = themeButton.Left - 8 - clickerNavButton.Width;
            visualNavButton.Left = clickerNavButton.Left - 6 - visualNavButton.Width;
            macroNavButton.Left = visualNavButton.Left - 6 - macroNavButton.Width;
            int top = macroNavButton.Left < subtitleLabel.Right + 12 ? subtitleLabel.Bottom + 6 : Math.Max(0, (headerPanel.ClientSize.Height - menuButton.Height) / 2 - 8);
            foreach (RoundedButton navigation in new RoundedButton[] { macroNavButton, visualNavButton, clickerNavButton, themeButton, menuButton }) navigation.Top = top;
        }

        private void BuildMacroPage()
        {
            macroPage = new ModernScrollViewer { Dock = DockStyle.Fill, Tag = "surface" };
            pageHost.Controls.Add(macroPage);
            BufferedTableLayoutPanel grid = new BufferedTableLayoutPanel {
                Dock = DockStyle.Top, ColumnCount = 2, RowCount = 4, AutoSize = true,
                GrowStyle = TableLayoutPanelGrowStyle.AddRows, Tag = "surface", Padding = new Padding(0, 0, 0, 5)
            };
            grid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
            grid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
            grid.RowStyles.Add(new RowStyle(SizeType.Absolute, 158));
            grid.RowStyles.Add(new RowStyle(SizeType.Absolute, 258));
            grid.RowStyles.Add(new RowStyle(SizeType.Absolute, 174));
            grid.RowStyles.Add(new RowStyle(SizeType.Absolute, 132));
            grid.SuspendLayout();
            macroPage.Content = grid;

            RoundedPanel actions = CreateCard();
            actions.Dock = DockStyle.Fill;
            AddSectionTitle(actions, Tr("GRAVAÇÃO E REPRODUÇÃO"), 19, 13);
            recordButton = MakeButton(Tr("Gravar"), 19, 43, 220, 54, true);
            recordButton.Icon = "record";
            recordButton.Font = FontManager.Create(10f, FontStyle.Bold);
            recordButton.Click += delegate { ToggleRecording(); };
            playButton = MakeButton(Tr("Reproduzir"), 250, 43, 220, 54, true);
            playButton.Icon = "play";
            playButton.Font = FontManager.Create(10f, FontStyle.Bold);
            playButton.Click += delegate { TogglePlayback(); };
            statusLabel = new Label { Text = Tr("Pronto"), AutoSize = false, Location = new Point(20, 107), Size = new Size(500, 22), Font = FontManager.Create(8.5f, FontStyle.Bold), ForeColor = palette.Muted };
            actions.Controls.Add(recordButton); actions.Controls.Add(playButton); actions.Controls.Add(statusLabel);
            actions.Resize += delegate
            {
                int half = Math.Max(170, (actions.ClientSize.Width - 58) / 2);
                recordButton.SetBounds(19, 43, half, 54);
                playButton.SetBounds(recordButton.Right + 16, 43, half, 54);
                statusLabel.Width = Math.Max(200, actions.ClientSize.Width - 38);
            };
            grid.Controls.Add(actions, 0, 0);
            grid.SetColumnSpan(actions, 2);

            RoundedPanel macroCard = CreateCard();
            macroCard.Dock = DockStyle.Fill;
            AddSectionTitle(macroCard, Tr("BIBLIOTECA DE MACROS"), 19, 14);
            macroCard.Controls.Add(MakeCaption(Tr("Nome da nova gravação"), 20, 47));
            nameBox = new ModernTextBox { Text = Tr("Nova gravação"), Location = new Point(19, 68), Width = 365, Height = 30, Font = FontManager.Create(9.5f, FontStyle.Regular) };
            macroCard.Controls.Add(nameBox);
            macroCard.Controls.Add(MakeCaption(Tr("Macros salvos"), 20, 105));
            macroCombo = new MacroSelector { Location = new Point(19, 127), Width = 270, Height = 36, Font = FontManager.Create(8.5f, FontStyle.Regular) };
            macroCombo.Language = uiLanguage;
            macroCombo.ApplyTheme(palette);
            macroCombo.SelectedIndexChanged += delegate
            {
                if (refreshingMacros) return;
                UpdateSelectedMacro();
                UpdateMacroActions();
            };
            refreshButton = MakeButton("", 0, 128, 34, 34, false);
            refreshButton.Icon = "refresh";
            refreshButton.AccessibleName = Tr("Atualizar macros");
            refreshButton.Click += delegate { LoadMacros(); };
            deleteSelectedButton = MakeButton("", 0, 128, 34, 34, false);
            deleteSelectedButton.Icon = "trash";
            deleteSelectedButton.AccessibleName = Tr("Apagar macro selecionado");
            deleteSelectedButton.Click += delegate { DeleteSelectedMacro(); };
            deleteAllButton = MakeButton("", 0, 128, 34, 34, false);
            deleteAllButton.Icon = "trash-all";
            deleteAllButton.AccessibleName = Tr("Apagar todos os macros");
            deleteAllButton.Click += delegate { DeleteAllMacros(); };
            macroCard.Controls.Add(macroCombo); macroCard.Controls.Add(refreshButton);
            macroCard.Controls.Add(deleteSelectedButton); macroCard.Controls.Add(deleteAllButton);
            macroCard.Resize += delegate
            {
                int available = Math.Max(170, macroCard.ClientSize.Width - 166);
                nameBox.Width = Math.Max(180, macroCard.ClientSize.Width - 40);
                macroCombo.Width = available;
                refreshButton.Left = macroCombo.Right + 7;
                deleteSelectedButton.Left = refreshButton.Right + 6;
                deleteAllButton.Left = deleteSelectedButton.Right + 6;
                LayoutMacroActionButtons(macroCard);
            };
            eventsLabel = new Label { Text = Tr("Nenhum macro salvo"), AutoSize = false, Location = new Point(20, 166), Size = new Size(410, 22), Font = FontManager.Create(8.2f, FontStyle.Regular), Tag = "muted" };
            macroCard.Controls.Add(eventsLabel);
            saveButton = MakeButton(Tr("Salvar gravação"), 19, 197, 148, 36, true);
            saveButton.Enabled = false;
            saveButton.Click += delegate { SaveRecording(); };
            macroCard.Controls.Add(saveButton);
            useMacroButton = MakeButton(Tr("Usar Macro Selecionado"), 175, 197, 190, 36, false);
            useMacroButton.Enabled = false;
            useMacroButton.AccessibleName = Tr("Usar Macro Selecionado");
            useMacroButton.Click += delegate { UseSelectedMacro(); };
            saveButton.FontChanged += delegate { LayoutMacroActionButtons(macroCard); };
            useMacroButton.FontChanged += delegate { LayoutMacroActionButtons(macroCard); };
            macroCard.Controls.Add(useMacroButton);
            LayoutMacroActionButtons(macroCard);
            grid.Controls.Add(macroCard, 0, 1);

            RoundedPanel folderCard = CreateCard();
            folderCard.Dock = DockStyle.Fill;
            AddSectionTitle(folderCard, Tr("PASTA DE MACROS"), 19, 14);
            folderLabel = MakeCaption(Tr("Escolha onde organizar seus macros."), 20, 50);
            folderCard.Controls.Add(folderLabel);
            folderBox = new ModernTextBox { Text = settings.Folder, Location = new Point(19, 80), Width = 347, Height = 32, ReadOnly = true, Font = FontManager.Create(9f, FontStyle.Regular) };
            folderCard.Controls.Add(folderBox);
            folderCard.Resize += delegate { folderBox.Width = Math.Max(180, folderCard.ClientSize.Width - 40); };
            folderButton = MakeButton(Tr("Escolher pasta"), 19, 124, 144, 38, false);
            folderButton.Click += ChooseFolder;
            folderCard.Controls.Add(folderButton);
            Label saveHint = MakeCaption(Tr("Gravações novas só são salvas ao clicar em Salvar gravação."), 20, 180);
            saveHint.Tag = "muted";
            folderCard.Controls.Add(saveHint);
            grid.Controls.Add(folderCard, 1, 1);

            RoundedPanel keyCard = CreateCard();
            keyCard.Dock = DockStyle.Fill;
            AddSectionTitle(keyCard, Tr("ATALHOS GLOBAIS"), 19, 14);
            recordKeyLabel = MakeCaption(Tr("Gravar/Parar"), 20, 57);
            playKeyLabel = MakeCaption(Tr("Reproduzir/Parar"), 20, 108);
            recordKeyLabel.Font = FontManager.Create(8.5f, FontStyle.Bold);
            playKeyLabel.Font = FontManager.Create(8.5f, FontStyle.Bold);
            recordKeyValue = MakeCaption("", 160, 57); recordKeyValue.Font = FontManager.Create(8.5f, FontStyle.Bold);
            playKeyValue = MakeCaption("", 160, 108); playKeyValue.Font = FontManager.Create(8.5f, FontStyle.Bold);
            keyStatusLabel = MakeCaption("", 20, 138); keyStatusLabel.Font = FontManager.Create(7.5f, FontStyle.Regular); keyStatusLabel.Tag = "muted";
            keyCard.Controls.Add(recordKeyLabel); keyCard.Controls.Add(playKeyLabel);
            keyCard.Controls.Add(recordKeyValue); keyCard.Controls.Add(playKeyValue); keyCard.Controls.Add(keyStatusLabel);
            setRecordKeyButton = MakeButton(Tr("Definir"), 0, 49, 100, 36, false);
            setRecordKeyButton.Click += delegate { BeginCapture(true); };
            setPlayKeyButton = MakeButton(Tr("Definir"), 0, 100, 100, 36, false);
            setPlayKeyButton.Click += delegate { BeginCapture(false); };
            keyCard.Controls.Add(setRecordKeyButton); keyCard.Controls.Add(setPlayKeyButton);
            keyCard.Resize += delegate
            {
                setRecordKeyButton.Left = keyCard.ClientSize.Width - setRecordKeyButton.Width - 20;
                setPlayKeyButton.Left = setRecordKeyButton.Left;
                recordKeyValue.Left = Math.Min(190, Math.Max(130, setRecordKeyButton.Left - recordKeyValue.Width - 22));
                playKeyValue.Left = recordKeyValue.Left;
            };
            grid.Controls.Add(keyCard, 0, 2);

            RoundedPanel intervalCard = CreateCard();
            intervalCard.Dock = DockStyle.Fill;
            AddSectionTitle(intervalCard, Tr("REPETIÇÃO"), 19, 14);
            infiniteCheck = new AnimatedToggle { Text = Tr("Ciclo Infinito"), Location = new Point(20, 49), Width = 150, Height = 30, Font = FontManager.Create(8.5f, FontStyle.Bold), BackColor = palette.Card };
            infiniteCheck.SetInitialChecked(settings.CycleInfinite);
            infiniteCheck.ApplyTheme(palette.Accent, palette.Text);
            infiniteCheck.CheckedChanged += delegate
            {
                settings.CycleInfinite = infiniteCheck.Checked;
                settings.Repeat = infiniteCheck.Checked;
                if (!applyingMacroSettings)
                {
                    macroSettingsDirty = true;
                    SaveSettings();
                    UpdateMacroActions();
                }
            };
            intervalCard.Controls.Add(infiniteCheck);
            intervalCard.Resize += delegate
            {
                infiniteCheck.Width = Math.Max(150, intervalCard.ClientSize.Width - infiniteCheck.Left - 20);
            };
            intervalCard.Controls.Add(MakeCaption(Tr("Pausa após cada ciclo"), 20, 86));
            minutesInput = new StepperInput { Location = new Point(20, 111), Width = 104, Minimum = 0, Maximum = 9999, Suffix = "min", Value = Math.Max(0, Math.Min(settings.IntervalMinutes, 9999)) };
            secondsInput = new StepperInput { Location = new Point(138, 111), Width = 104, Minimum = 0, Maximum = 59, Suffix = "s", Value = Math.Max(0, Math.Min(settings.IntervalSeconds, 59)) };
            minutesInput.ApplyLanguage(uiLanguage); secondsInput.ApplyLanguage(uiLanguage);
            intervalCard.Controls.Add(minutesInput); intervalCard.Controls.Add(secondsInput);
            minutesInput.ValueChanged += delegate { if (!applyingMacroSettings) SaveIntervalSettings(); };
            secondsInput.ValueChanged += delegate { if (!applyingMacroSettings) SaveIntervalSettings(); };
            grid.Controls.Add(intervalCard, 1, 2);
            RoundedPanel playbackOptions = CreateCard();
            playbackOptions.Dock = DockStyle.Fill;
            AddSectionTitle(playbackOptions, Tr("OPÇÕES DE REPRODUÇÃO"), 19, 14);
            variationCheck = new AnimatedToggle { Text = Tr("Variação leve"), Location = new Point(20, 44), Size = new Size(300, 30), Font = FontManager.Create(8.5f, FontStyle.Bold) };
            smoothReturnCheck = new AnimatedToggle { Text = Tr("Retorno suave ao início"), Location = new Point(400, 44), Size = new Size(300, 30), Font = FontManager.Create(8.5f, FontStyle.Bold) };
            variationCheck.SetInitialChecked(settings.VaryPlayback);
            smoothReturnCheck.SetInitialChecked(settings.SmoothCursorReturn);
            Label variationHint = MakeCaption(Tr("Até 1 px; pequenos ajustes nos tempos."), 20, 81);
            Label smoothHint = MakeCaption(Tr("Antes e depois de cada ciclo concluído."), 400, 81);
            variationHint.Tag = smoothHint.Tag = "muted";
            variationHint.AutoSize = smoothHint.AutoSize = false;
            variationHint.Height = smoothHint.Height = 26;
            EventHandler optionsChanged = delegate {
                if (applyingMacroSettings) return;
                settings.VaryPlayback = variationCheck.Checked;
                settings.SmoothCursorReturn = smoothReturnCheck.Checked;
                macroSettingsDirty = true;
                SaveSettings(); UpdateMacroActions();
            };
            variationCheck.CheckedChanged += optionsChanged;
            smoothReturnCheck.CheckedChanged += optionsChanged;
            playbackOptions.Controls.Add(variationCheck); playbackOptions.Controls.Add(smoothReturnCheck);
            playbackOptions.Controls.Add(variationHint); playbackOptions.Controls.Add(smoothHint);
            playbackOptions.Resize += delegate {
                int margin = (int)Math.Round(20 * DeviceDpi / 96.0);
                int half = playbackOptions.ClientSize.Width / 2;
                variationCheck.Width = variationHint.Width = Math.Max(1, half - margin * 2);
                smoothReturnCheck.Left = smoothHint.Left = half + margin / 2;
                smoothReturnCheck.Width = smoothHint.Width = Math.Max(1, playbackOptions.ClientSize.Width - smoothReturnCheck.Left - margin);
            };
            grid.Controls.Add(playbackOptions, 0, 3);
            grid.SetColumnSpan(playbackOptions, 2);
            grid.ResumeLayout(true);
        }

        private void BuildAutoClickerPage()
        {
            autoClickerPage = new BufferedPanel { Dock = DockStyle.Fill, Visible = false, Tag = "surface" };
            pageHost.Controls.Add(autoClickerPage);
            autoClickerTitle = new Label { Text = "Auto-Clicker", AutoSize = true, Font = FontManager.Create(22f, FontStyle.Bold), ForeColor = palette.Text };
            autoClickerHint = new Label { Text = Tr("Em desenvolvimento"), AutoSize = true, Font = FontManager.Create(9f, FontStyle.Regular), ForeColor = palette.Muted, Tag = "muted" };
            autoClickerPage.Controls.Add(autoClickerTitle); autoClickerPage.Controls.Add(autoClickerHint);
            autoClickerPage.Resize += delegate
            {
                autoClickerTitle.Left = (autoClickerPage.ClientSize.Width - autoClickerTitle.Width) / 2;
                autoClickerTitle.Top = Math.Max(70, (autoClickerPage.ClientSize.Height - autoClickerTitle.Height) / 2 - 16);
                autoClickerHint.Left = (autoClickerPage.ClientSize.Width - autoClickerHint.Width) / 2;
                autoClickerHint.Top = autoClickerTitle.Bottom + 12;
            };
        }

        private void BuildVisualRecognitionPage()
        {
            visualRecognitionPage = new VisualRecognitionPage(settings.Folder, uiLanguage, Tr, palette);
            visualRecognitionPage.DefaultMonitorDevice = settings.ReaderMonitorDevice;
            visualRecognitionPage.DefaultMonitorChanged = delegate(string device) { settings.ReaderMonitorDevice = device; SaveSettings(); };
            visualRecognitionPage.CanStart = delegate { return !recording && !playing; };
            pageHost.Controls.Add(visualRecognitionPage);
        }

        private Control GetPageControl(int index)
        {
            if (index == 1) return autoClickerPage;
            if (index == 2) return visualRecognitionPage;
            return macroPage;
        }

        private RoundedButton GetPageButton(int index)
        {
            if (index == 1) return clickerNavButton;
            if (index == 2) return visualNavButton;
            return macroNavButton;
        }

        private void SetActivePage(int pageIndex)
        {
            if (pageTransitionTimer != null && pageTransitionTimer.Enabled) FinishPageTransition();
            if (pageIndex < 0 || pageIndex > 2 || pageIndex == activePageIndex) return;
            pageTransitionTarget = pageIndex;
            Rectangle area = pageHost.RectangleToScreen(pageHost.ClientRectangle);
            if (!Visible || WindowState == FormWindowState.Minimized || area.Width < 2 || area.Height < 2)
            {
                ApplyActivePage(pageIndex);
                return;
            }
            Bitmap frame = new Bitmap(area.Width, area.Height);
            try
            {
                // Capture the pixels actually on screen, including native text/list
                // controls. Only this outgoing frame is faded by the window compositor.
                using(Graphics graphics = Graphics.FromImage(frame))
                    graphics.CopyFromScreen(area.Location, Point.Empty, area.Size);
                transitionSurface = new PageTransitionSurface(frame) { Bounds = area };
                frame = null;
                transitionSurface.Show(this);
                transitionSurface.Update();
                ApplyActivePage(pageIndex);
                GetPageControl(pageIndex).Update();
                if (pageTransitionTimer == null)
                {
                    pageTransitionTimer = new System.Windows.Forms.Timer { Interval = 16 };
                    pageTransitionTimer.Tick += AnimatePageTransition;
                }
                pageTransitionClock.Restart();
                UiAnimationClock.Begin();
                pageTransitionTimer.Start();
            }
            catch
            {
                if(frame != null)frame.Dispose();
                FinishPageTransition();
            }
        }
        private void ApplyActivePage(int pageIndex)
        {
            activePageIndex = Math.Max(0, Math.Min(2, pageIndex));
            for (int index = 0; index < 3; index++)
            {
                Control page = GetPageControl(index);
                page.Dock = DockStyle.Fill;
                page.Location = Point.Empty;
                page.Visible = index == activePageIndex;
            }
            SetSelectedPageButton(activePageIndex);
            ApplyControlTheme(headerPanel);
        }

        private void SetSelectedPageButton(int pageIndex)
        {
            for (int index = 0; index < 3; index++)
                GetPageButton(index).Tag = index == pageIndex ? "selected" : "secondary";
        }

        private void FinishPageTransition()
        {
            if (pageTransitionTimer != null && pageTransitionTimer.Enabled)
            {
                pageTransitionTimer.Stop();
                UiAnimationClock.End();
            }
            pageTransitionClock.Stop();
            ApplyActivePage(pageTransitionTarget);
            if (transitionSurface != null)
            {
                transitionSurface.Hide();
                transitionSurface.Dispose();
                transitionSurface = null;
            }
        }

        private void AnimatePageTransition(object sender, EventArgs e)
        {
            float progress = Math.Min(1f, (float)(pageTransitionClock.Elapsed.TotalMilliseconds / 210.0));
            float eased = progress * progress * (3f - 2f * progress);
            if (transitionSurface != null) transitionSurface.Progress = eased;
            if (progress < 1f) return;
            FinishPageTransition();
        }

        private void ShowAppMenu()
        {
            if (appMenu == null || appMenu.IsDisposed) CreateAppMenu();
            appMenu.ApplyTheme(palette);
            appMenu.ShowAt(this, menuButton.RectangleToScreen(menuButton.ClientRectangle));
        }

        private void CreateAppMenu()
        {
            appMenu = new ModernMenuPopup(palette, uiLanguage);
            appMenu.ItemChosen += delegate(int item) { if (item == 0) ShowOptions(); else ShowAbout(); };
        }

        protected override void OnResize(EventArgs e)
        {
            if (!hiddenToTray && WindowState != FormWindowState.Minimized) trayRestoreState = WindowState;
            if(transitionSurface != null)FinishPageTransition();
            base.OnResize(e);
            if (themeTransitionTimer != null && themeTransitionTimer.Enabled &&
                (WindowState == FormWindowState.Minimized || themeTransitionSurface == null || themeTransitionSurface.Size != ClientSize))
                FinishThemeTransition();
            if (WindowState == FormWindowState.Minimized && appMenu != null && !appMenu.IsDisposed) appMenu.Dismiss();
            if (WindowState == FormWindowState.Minimized) MinimizeToTray();
        }

        private void MinimizeToTray()
        {
            if (trayIcon == null || hiddenToTray || IsDisposed || Disposing) return;
            trayIcon.Show();
            hiddenToTray = true;
            // Hiding removes the taskbar button without recreating the window handle,
            // so the registered shortcuts remain available in the background.
            Hide();
        }

        private void RestoreFromTray()
        {
            if (IsDisposed || Disposing || closeAfterPlayback) return;
            if (visualRecognitionPage != null && visualRecognitionPage.IsSelecting) return;
            if (hiddenToTray || WindowState == FormWindowState.Minimized)
            {
                // Show the native window before restoring its state. Restoring a
                // hidden minimized Form first can leave it minimized after Show().
                hiddenToTray = true;
                if (!Visible) Show();
                WindowState = trayRestoreState;
                hiddenToTray = false;
                FitToWorkingArea();
            }
            if (!Visible) Show();
            Activate();
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing && trayIcon != null)
            {
                trayIcon.Dispose();
                trayIcon = null;
            }
            base.Dispose(disposing);
        }

        private void ApplyMenuTheme()
        {
            if (appMenu != null && !appMenu.IsDisposed) appMenu.ApplyTheme(palette);
        }

        private void ShowOptions()
        {
            if (optionsDialog == null || optionsDialog.IsDisposed) optionsDialog = new OptionsForm(settings, palette, uiLanguage);
            optionsDialog.RefreshPalette(palette);
            ApplyFontTree(optionsDialog);
            optionsDialog.DialogResult = DialogResult.None;
            if (optionsDialog.ShowDialog(this) == DialogResult.OK)
            {
                string previousLanguage = settings.Language;
                if (settings.DarkTheme)
                {
                    settings.DarkBackgroundColor = optionsDialog.BackgroundHex ?? "";
                    settings.DarkCardColor = optionsDialog.CardHex ?? "";
                    settings.DarkTextColor = optionsDialog.TextHex ?? "";
                    settings.DarkAccentColor = optionsDialog.AccentHex ?? "";
                }
                else
                {
                    settings.LightBackgroundColor = optionsDialog.BackgroundHex ?? "";
                    settings.LightCardColor = optionsDialog.CardHex ?? "";
                    settings.LightTextColor = optionsDialog.TextHex ?? "";
                    settings.LightAccentColor = optionsDialog.AccentHex ?? "";
                }
                settings.FontFamily = optionsDialog.SelectedFontFamily;
                settings.Language = optionsDialog.SelectedLanguage;
                settings.AlwaysRunAsAdministrator = optionsDialog.AlwaysRunAsAdministrator;
                FontManager.SetPreferredFamily(settings.FontFamily);
                palette = ThemePalette.From(settings);
                SaveSettings();
                if (!String.Equals(previousLanguage, settings.Language, StringComparison.OrdinalIgnoreCase))
                    RebuildUiForLanguage();
                else
                {
                    ApplyFontTree(this);
                    if (toolTips != null) toolTips.ApplyFont(FontManager.Create(8.5f, FontStyle.Regular));
                    ApplyTheme();
                }
            }
        }

        private void RebuildUiForLanguage()
        {
            CommitPlaybackTiming();
            string recordingName = nameBox.Text;
            if (pageTransitionTimer != null && pageTransitionTimer.Enabled) FinishPageTransition();
            int showPage = activePageIndex;
            VisualWorkflow visualDraft = visualRecognitionPage == null ? null : visualRecognitionPage.ExportDraft();
            if (pageTransitionTimer != null)
            {
                if (pageTransitionTimer.Enabled) UiAnimationClock.End();
                pageTransitionTimer.Stop();
                pageTransitionTimer.Dispose();
                pageTransitionTimer = null;
            }
            pageTransitionClock.Stop();

            if (appMenu != null) { appMenu.Dispose(); appMenu = null; }
            if (toolTips != null) { toolTips.Dispose(); toolTips = null; }
            if (optionsDialog != null) { optionsDialog.Dispose(); optionsDialog = null; }
            if (aboutDialog != null) { aboutDialog.Dispose(); aboutDialog = null; }

            while (ContentPanel.Controls.Count > 0)
            {
                Control oldControl = ContentPanel.Controls[0];
                ContentPanel.Controls.RemoveAt(0);
                oldControl.Dispose();
            }

            uiLanguage = settings.Language;
            ApplyChromeLanguage(uiLanguage);
            if (trayIcon != null) trayIcon.RefreshLanguage();
            activePageIndex = showPage;
            BuildUi();
            nameBox.Text = recordingName;
            visualRecognitionPage.RestoreDraft(visualDraft);
            ApplyActivePage(showPage);
            ApplyFontTree(this);
            ApplyTheme();
            UpdateHotkeyLabels();

            if (!recording && pendingEvents != null && pendingEvents.Count > 0)
                eventsLabel.Text = Tr("Gravação não salva · pronta para testar ou salvar");
            else
                eventsLabel.Text = "";

            UpdateRuntimeUi(recording ? "Gravando" : (playing ? "Reproduzindo" : "Pronto"), "", recording || playing);
            LoadMacros();
        }

        private void ShowAbout()
        {
            if (aboutDialog == null || aboutDialog.IsDisposed) aboutDialog = new AboutForm(palette, uiLanguage);
            aboutDialog.RefreshPalette(palette);
            ApplyFontTree(aboutDialog);
            aboutDialog.DialogResult = DialogResult.None;
            aboutDialog.ShowDialog(this);
        }

        private void SaveIntervalSettings()
        {
            if (minutesInput == null || secondsInput == null) return;
            settings.IntervalMinutes = (int)minutesInput.Value;
            settings.IntervalSeconds = (int)secondsInput.Value;
            settings.Interval = settings.IntervalMinutes * 60 + settings.IntervalSeconds;
            settings.IntervalUnit = "segundos";
            macroSettingsDirty = true;
            SaveSettings();
            UpdateMacroActions();
        }

        private string Tr(string portugueseText)
        {
            return Localization.Translate(uiLanguage, portugueseText);
        }

        private RoundedPanel CreateCard()
        {
            return new RoundedPanel { BackColor = palette.Card, BorderColor = palette.Border, Margin = new Padding(0, 8, 12, 8), CornerRadius = 15, Tag = "card" };
        }

        private void LayoutMacroActionButtons(RoundedPanel card)
        {
            if (card == null || saveButton == null || useMacroButton == null) return;
            const int gap = 8;
            const int rightPadding = 20;
            int available = Math.Max(1, card.ClientSize.Width - saveButton.Left - rightPadding);
            int preferredSaveWidth = TextRenderer.MeasureText(saveButton.Text, saveButton.Font,
                new Size(1000, saveButton.Height), TextFormatFlags.NoPadding | TextFormatFlags.SingleLine).Width + 16;
            int preferredUseWidth = TextRenderer.MeasureText(useMacroButton.Text, useMacroButton.Font,
                new Size(1000, useMacroButton.Height), TextFormatFlags.NoPadding | TextFormatFlags.SingleLine).Width + 16;
            int saveWidth = Math.Max(148, preferredSaveWidth);
            int useWidth = Math.Min(preferredUseWidth, available - saveWidth - gap);
            if (useWidth < preferredUseWidth)
            {
                saveWidth = Math.Max(110, saveWidth - (preferredUseWidth - Math.Max(0, useWidth)));
                useWidth = Math.Min(preferredUseWidth, available - saveWidth - gap);
            }
            saveButton.Width = Math.Max(1, saveWidth);
            useMacroButton.SetBounds(saveButton.Right + gap, saveButton.Top, Math.Max(1, useWidth), saveButton.Height);
        }

        private void AddSectionTitle(Control parent, string text, int x, int y)
        {
            parent.Controls.Add(new Label { Text = text, AutoSize = true, Location = new Point(x, y), Font = FontManager.Create(8f, FontStyle.Bold), ForeColor = palette.Muted, Tag = "muted" });
        }

        private Label MakeCaption(string text, int x, int y)
        {
            return new Label { Text = text, AutoSize = true, Location = new Point(x, y), Font = FontManager.Create(8.5f, FontStyle.Regular), ForeColor = palette.Text };
        }

        private RoundedButton MakeButton(string text, int x, int y, int w, int h, bool primary)
        {
            return new RoundedButton { Text = text, Location = new Point(x, y), Size = new Size(w, h), Font = FontManager.Create(8.5f, FontStyle.Bold), Tag = primary ? "primary" : "secondary", BackColor = primary ? palette.Accent : palette.Input, ForeColor = palette.Text };
        }

        private void ApplyTheme()
        {
            palette = ThemePalette.From(settings);
            BackColor = palette.Background;
            ForeColor = palette.Text;
            ApplyChromeTheme(palette.Background, palette.Text, palette.Card);
            foreach (Control c in ContentPanel.Controls) ApplyControlTheme(c);
            themeButton.Text = Tr(settings.DarkTheme ? "Claro" : "Escuro");
            themeButton.Icon = settings.DarkTheme ? "sun" : "moon";
            SetSelectedPageButton(activePageIndex);
            ApplyControlTheme(headerPanel);
            if (toolTips != null) toolTips.ApplyTheme(palette.Card, palette.Text, palette.Border);
            if (macroPage != null) macroPage.ApplyTheme(palette.Background, palette.Background, palette.Border);
            recordButton.BackColor = recording ? Color.FromArgb(193, 64, 73) : palette.Accent;
            playButton.BackColor = playing ? Color.FromArgb(193, 64, 73) : palette.Accent;
            statusLabel.ForeColor = recording ? Color.FromArgb(246, 115, 122) : (playing ? palette.Accent : palette.Muted);
            if (autoClickerTitle != null) autoClickerTitle.ForeColor = palette.Text;
            if (autoClickerHint != null) autoClickerHint.ForeColor = palette.Muted;
            ApplyMenuTheme();
            Invalidate(true);
            ContentPanel.Invalidate(true);
        }

        private void BeginThemeTransition()
        {
            if (themeTransitionTimer == null)
            {
                themeTransitionTimer = new System.Windows.Forms.Timer { Interval = 15 };
                themeTransitionTimer.Tick += AnimateThemeTransition;
            }
            if (themeTransitionTimer.Enabled) return;
            if (!Visible || WindowState == FormWindowState.Minimized)
            {
                SwitchTheme();
                return;
            }
            Bitmap previousFrame = CaptureThemeFrame();
            SwitchTheme();
            Bitmap currentFrame = CaptureThemeFrame();
            if (previousFrame == null || currentFrame == null)
            {
                if (previousFrame != null) previousFrame.Dispose();
                if (currentFrame != null) currentFrame.Dispose();
                return;
            }

            themeTransitionSurface = new ThemeTransitionSurface(previousFrame, currentFrame) { Bounds = ClientRectangle };
            Controls.Add(themeTransitionSurface);
            themeTransitionSurface.BringToFront();
            themeTransitionClock.Restart();
            UiAnimationClock.Begin();
            themeTransitionTimer.Start();
        }

        private Bitmap CaptureThemeFrame()
        {
            if (ClientSize.Width < 1 || ClientSize.Height < 1) return null;
            Bitmap frame = new Bitmap(ClientSize.Width, ClientSize.Height, PixelFormat.Format32bppPArgb);
            try
            {
                DrawToBitmap(frame, new Rectangle(Point.Empty, ClientSize));
                return frame;
            }
            catch
            {
                frame.Dispose();
                return null;
            }
        }

        private void SwitchTheme()
        {
            settings.DarkTheme = !settings.DarkTheme;
            palette = ThemePalette.From(settings);
            SaveSettings();
            ApplyTheme();
        }

        private void AnimateThemeTransition(object sender, EventArgs e)
        {
            float progress = Math.Min(1f, (float)(themeTransitionClock.Elapsed.TotalMilliseconds / 240.0));
            float eased = progress * progress * (3f - 2f * progress);
            if (themeTransitionSurface != null) themeTransitionSurface.Progress = eased;
            if (progress < 1f) return;
            FinishThemeTransition();
        }

        private void FinishThemeTransition()
        {
            if (themeTransitionTimer != null && themeTransitionTimer.Enabled)
            {
                themeTransitionTimer.Stop();
                UiAnimationClock.End();
            }
            themeTransitionClock.Stop();
            if (themeTransitionSurface != null)
            {
                Controls.Remove(themeTransitionSurface);
                themeTransitionSurface.Dispose();
                themeTransitionSurface = null;
            }
        }

        private void ApplyControlTheme(Control parent)
        {
            foreach (Control c in parent.Controls)
            {
                string tag = c.Tag == null ? "" : c.Tag as string;
                if (tag == "surface") c.BackColor = palette.Background;
                else if (tag == "card") c.BackColor = palette.Card;
                RoundedPanel card = c as RoundedPanel;
                if (card != null) card.BorderColor = palette.Border;

                Label label = c as Label;
                if (label != null) label.ForeColor = tag == "muted" ? palette.Muted : palette.Text;
                if (c is TextBox) { c.BackColor = palette.Input; c.ForeColor = palette.Text; }
                ModernTextBox textBox = c as ModernTextBox;
                if (textBox != null) textBox.ApplyTheme(palette.Input, palette.Border, palette.Text);
                StepperInput stepper = c as StepperInput;
                if (stepper != null) { stepper.ApplyTheme(palette.Input, palette.Border, palette.Text, RoundedControls.Blend(palette.Card, palette.Text, 0.13f)); continue; }
                AnimatedToggle toggle = c as AnimatedToggle;
                if (toggle != null) { toggle.BackColor = palette.Card; toggle.ApplyTheme(palette.Accent, palette.Text); }
                MacroSelector selector = c as MacroSelector;
                if (selector != null) selector.ApplyTheme(palette);
                RoundedButton button = c as RoundedButton;
                if (button != null)
                {
                    bool primary = tag == "primary" || tag == "selected";
                    bool active = tag == "recording" || tag == "playing";
                    button.BackColor = active ? Color.FromArgb(193, 64, 73) : (primary ? palette.Accent : palette.Input);
                    button.ForeColor = primary || active ? Color.White : palette.Text;
                }
                ComboBox combo = c as ComboBox;
                if (combo != null) { combo.BackColor = palette.Input; combo.ForeColor = palette.Text; }
                VisualRecognitionPage recognitionPage = c as VisualRecognitionPage;
                if (recognitionPage != null) recognitionPage.ApplyTheme(palette);
                ApplyControlTheme(c);
            }
        }

        protected override void OnShown(EventArgs e)
        {
            base.OnShown(e);
            if (trayIcon != null) trayIcon.Show();
            RefreshNativePlaybackHotkey();
            FitToWorkingArea();
            BeginInvoke((MethodInvoker)delegate
            {
                LoadMacros();
                System.Threading.ThreadPool.QueueUserWorkItem(delegate
                {
                    System.Threading.Thread.Sleep(900);
                    FontManager.PreloadInstalledFamilies();
                });
            });
        }

        private static void ApplyFontTree(Control control)
        {
            if (control == null || control.IsDisposed) return;
            try
            {
                float size = control.Font == null ? 9f : control.Font.SizeInPoints;
                FontStyle style = control.Font == null ? FontStyle.Regular : control.Font.Style;
                control.Font = FontManager.Create(size, style);
            }
            catch { }
            foreach (Control child in control.Controls) ApplyFontTree(child);
        }

        protected override void OnDpiChanged(DpiChangedEventArgs e)
        {
            base.OnDpiChanged(e);
            Screen screen = Screen.FromControl(this);
            currentMonitorDevice = screen.DeviceName;
            FitToWorkingArea();
        }

        protected override void OnLocationChanged(EventArgs e)
        {
            if(transitionSurface != null)FinishPageTransition();
            base.OnLocationChanged(e);
            if (!IsHandleCreated || IsDisposed) return;
            Screen screen = Screen.FromControl(this);
            if (String.Equals(screen.DeviceName, currentMonitorDevice, StringComparison.OrdinalIgnoreCase)) return;
            currentMonitorDevice = screen.DeviceName;
            FitToWorkingArea();
        }

        protected override void WndProc(ref Message message)
        {
            if(message.Msg==0x0312 && message.WParam.ToInt32()==PlaybackHotkeyId) {
                if(!capturePlay && !captureRecord && playHotkey!=null)
                    TryHotkey(playHotkey.Key,playHotkey.Ctrl,playHotkey.Alt,playHotkey.Shift,playHotkey.Win);
                return;
            }
            base.WndProc(ref message);
            if (message.Msg == 0x007E && IsHandleCreated && !IsDisposed)
            {
                PostToUi((MethodInvoker)delegate
                {
                    if (IsDisposed || WindowState == FormWindowState.Minimized) return;
                    currentMonitorDevice = Screen.FromControl(this).DeviceName;
                    FitToWorkingArea();
                });
            }
        }

        private static Size ScaleSize(Size size, float scale)
        {
            return new Size(Math.Max(1, (int)Math.Round(size.Width * scale)), Math.Max(1, (int)Math.Round(size.Height * scale)));
        }

        private void FitToWorkingArea()
        {
            if (WindowState != FormWindowState.Normal) return;
            Rectangle area = Screen.FromControl(this).WorkingArea;
            float dpiScale = Math.Max(96, DeviceDpi) / 96f;
            Size designMinimum = ScaleSize(new Size(820, 640), dpiScale);
            int availableWidth = Math.Max(1, area.Width - 16);
            int availableHeight = Math.Max(1, area.Height - 16);
            int minWidth = Math.Min(designMinimum.Width, availableWidth);
            int minHeight = Math.Min(designMinimum.Height, availableHeight);
            MinimumSize = new Size(minWidth, minHeight);
            int width = Math.Max(minWidth, Math.Min(Width, availableWidth));
            int height = Math.Max(minHeight, Math.Min(Height, availableHeight));
            int left = Math.Max(area.Left, Math.Min(Left, area.Right - width));
            int top = Math.Max(area.Top, Math.Min(Top, area.Bottom - height));
            Bounds = new Rectangle(left, top, width, height);
        }

        private bool PostToUi(MethodInvoker action)
        {
            if (action == null || IsDisposed || Disposing || !IsHandleCreated) return false;
            try { BeginInvoke(action); return true; }
            catch (InvalidOperationException) { return false; }
        }

        private void UpdateHotkeyLabels()
        {
            RefreshNativePlaybackHotkey();
            if (recordKeyLabel != null) recordKeyLabel.Text = Tr("Gravar/Parar");
            if (playKeyLabel != null) playKeyLabel.Text = Tr("Reproduzir/Parar");
            if (recordKeyValue != null) recordKeyValue.Text = recordHotkey == null ? Tr("Não definido") : recordHotkey.Text;
            if (playKeyValue != null) playKeyValue.Text = playHotkey == null ? Tr("Não definido") : playHotkey.Text;
        }

        public bool TryCaptureHotkey(int vk, bool ctrl, bool alt, bool shift, bool win)
        {
            if (!captureRecord && !capturePlay) return false;
            if (IsModifier(vk)) return true;
            if (vk == (int)Keys.Escape)
            {
                captureRecord = capturePlay = false;
                PostToUi((MethodInvoker)delegate
                {
                    setRecordKeyButton.Text = Tr("Definir"); setPlayKeyButton.Text = Tr("Definir");
                    SetKeyStatus("Alteração cancelada");
                    RefreshNativePlaybackHotkey();
                });
                return true;
            }

            string combination = Hotkey.Format(vk, ctrl, alt, shift, win);
            bool duplicate = (captureRecord && playHotkey != null && Hotkey.Parse(combination).Text == playHotkey.Text) ||
                (capturePlay && recordHotkey != null && Hotkey.Parse(combination).Text == recordHotkey.Text);
            if (duplicate)
            {
                captureRecord = capturePlay = false;
                PostToUi((MethodInvoker)delegate
                {
                    setRecordKeyButton.Text = Tr("Definir"); setPlayKeyButton.Text = Tr("Definir");
                    SetKeyStatus("Escolha dois atalhos diferentes");
                    RefreshNativePlaybackHotkey();
                });
                return true;
            }

            bool setRecord = captureRecord;
            captureRecord = capturePlay = false;
            PostToUi((MethodInvoker)delegate
            {
                if (setRecord) { settings.RecordHotkey = combination; recordHotkey = Hotkey.Parse(combination); }
                else { settings.PlayHotkey = combination; playHotkey = Hotkey.Parse(combination); }
                setRecordKeyButton.Text = Tr("Definir"); setPlayKeyButton.Text = Tr("Definir");
                SaveSettings(); UpdateHotkeyLabels();
                SetKeyStatus("Atalho atualizado");
            });
            return true;
        }

        private bool IsModifier(int vk)
        {
            return vk == 0x10 || vk == 0x11 || vk == 0x12 || vk == 0x5B || vk == 0x5C ||
                vk == 0xA0 || vk == 0xA1 || vk == 0xA2 || vk == 0xA3 || vk == 0xA4 || vk == 0xA5;
        }

        private void BeginCapture(bool record)
        {
            captureRecord = record; capturePlay = !record;
            RefreshNativePlaybackHotkey();
            setRecordKeyButton.Text = record ? Tr("Pressione...") : Tr("Definir");
            setPlayKeyButton.Text = record ? Tr("Definir") : Tr("Pressione...");
            SetKeyStatus("Pressione uma tecla ou Esc para cancelar");
        }

        private void SetKeyStatus(string text)
        {
            if (keyStatusLabel == null || IsDisposed) return;
            keyStatusLabel.Text = Tr(text);
            keyStatusLabel.ForeColor = palette.Muted;
        }

        public bool TryHotkey(int vk, bool ctrl, bool alt, bool shift, bool win)
        {
            bool playbackKey = playHotkey != null && playHotkey.Key == vk && playHotkey.Ctrl == ctrl && playHotkey.Alt == alt && playHotkey.Shift == shift && playHotkey.Win == win;
            if (playing && playbackKey)
            {
                PostToUi((MethodInvoker)delegate { StopPlayback(); });
                return true;
            }
            if (visualRecognitionPage != null && visualRecognitionPage.IsSelecting) return false;
            if (visualRecognitionPage != null && visualRecognitionPage.IsRunning)
            {
                if (vk == (int)Keys.Escape || (playHotkey != null && playHotkey.Key == vk && playHotkey.Ctrl == ctrl && playHotkey.Alt == alt && playHotkey.Shift == shift && playHotkey.Win == win))
                {
                    visualRecognitionPage.StopExecution();
                    return true;
                }
                return false;
            }
            if (recordHotkey != null && recordHotkey.Key == vk && recordHotkey.Ctrl == ctrl && recordHotkey.Alt == alt && recordHotkey.Shift == shift && recordHotkey.Win == win)
            {
                if(!recording && (pageTransitionTimer != null && pageTransitionTimer.Enabled ? pageTransitionTarget : activePageIndex) != 0)return false;
                PostToUi((MethodInvoker)delegate { ToggleRecording(); });
                return true;
            }
            if (playHotkey != null && playHotkey.Key == vk && playHotkey.Ctrl == ctrl && playHotkey.Alt == alt && playHotkey.Shift == shift && playHotkey.Win == win)
            {
                int page = pageTransitionTimer != null && pageTransitionTimer.Enabled ? pageTransitionTarget : activePageIndex;
                PostToUi((MethodInvoker)delegate {
                    if(page==2)visualRecognitionPage.ToggleExecution();
                    else if(page==0)TogglePlayback();
                });
                return true;
            }
            return false;
        }

        private void ToggleRecording()
        {
            if (visualRecognitionPage != null && visualRecognitionPage.IsRunning) return;
            if (recording) { StopRecording(); return; }
            if (playing) { UpdateRuntimeUi("Pare a reprodução antes de gravar", "", true); return; }
            pendingEvents = null;
            recordingStartMouseX = null;
            recordingStartMouseY = null;
            pendingPreviewActive = false;
            saveButton.Enabled = false;
            lock (eventsLock) recordedEvents.Clear();
            recordLimitQueued = false;
            try
            {
                if (hooks != null) hooks.BeginMouseCapture();
            }
            catch (Exception ex)
            {
                UpdateRuntimeUi("Não foi possível iniciar a gravação", "", false);
                MessageBox.Show(this, Tr("Não foi possível iniciar a captura do mouse.") + "\r\n\r\n" + ex.Message, Tr("Erro ao gravar"), MessageBoxButtons.OK, MessageBoxIcon.Error);
                return;
            }
            recordClock.Reset(); recordClock.Start();
            recording = true;
            if (!IsMacroDeckForeground) CaptureRecordingStartPositionFromCursor();
            eventsLabel.Text = "";
            UiSounds.RecordingStart();
            UpdateRuntimeUi("Gravando", "", true);
        }

        private void StopRecording()
        {
            if (!recording) return;
            recording = false;
            if (hooks != null) hooks.EndMouseCapture();
            UiSounds.RecordingStop();
            recordClock.Stop();
            List<MacroEvent> copy;
            lock (eventsLock)
            {
                copy = new List<MacroEvent>(recordedEvents);
                BalanceInputs(copy, RecordElapsedMicroseconds);
            }
            pendingEvents = copy.Count == 0 ? null : copy;
            pendingPreviewActive = pendingEvents != null;
            if (pendingEvents == null)
            {
                eventsLabel.Text = "";
                saveButton.Enabled = false;
                UpdateRuntimeUi("Gravação vazia", "", false);
                return;
            }
            eventsLabel.Text = Tr("Gravação não salva · pronta para testar ou salvar");
            saveButton.Enabled = true;
            UpdateRuntimeUi("Gravação pronta para testar ou salvar", "", false);
        }

        private bool SaveRecording()
        {
            CommitPlaybackTiming();
            if (pendingEvents == null || pendingEvents.Count == 0) return SaveActiveMacroSettings();
            try
            {
                Directory.CreateDirectory(settings.Folder);
                string baseName = SafeFileName(String.IsNullOrWhiteSpace(nameBox.Text) ? Tr("Nova gravação") : nameBox.Text);
                string name = baseName;
                string path = Path.Combine(settings.Folder, name + ".json");
                if (File.Exists(path))
                {
                    DialogResult replace = MessageBox.Show(this,
                        Tr("Já existe um macro com esse nome. Deseja substituir os dados e as opções salvas?"),
                        Tr("Substituir macro?"), MessageBoxButtons.YesNo, MessageBoxIcon.Warning);
                    if (replace != DialogResult.Yes) return false;
                }
                MacroFile macro = new MacroFile {
                    Version = 1,
                    Name = name,
                    CreatedAt = DateTime.Now,
                    Events = new List<MacroEvent>(pendingEvents),
                    CycleInfinite = infiniteCheck.Checked,
                    IntervalMinutes = (int)minutesInput.Value,
                    IntervalSeconds = (int)secondsInput.Value,
                    VaryPlayback = variationCheck.Checked,
                    SmoothCursorReturn = smoothReturnCheck.Checked,
                    StartMouseX = recordingStartMouseX,
                    StartMouseY = recordingStartMouseY
                };
                WriteMacroAtomically(path, json.Serialize(macro));
                pendingEvents = null;
                pendingPreviewActive = false;
                macroSettingsDirty = false;
                activeMacro = macro;
                activeMacroPath = path;
                settings.SelectedFile = path;
                SaveSettings();
                saveButton.Enabled = false;
                LoadMacros(path);
                eventsLabel.Text = Tr("Gravação salva");
                UpdateRuntimeUi("Macro salvo", "", false);
                return true;
            }
            catch (Exception ex)
            {
                UpdateRuntimeUi("Erro ao salvar", "", false);
                MessageBox.Show(this, Tr("Não foi possível salvar a gravação.") + "\r\n\r\n" + ex.Message, Tr("Erro ao salvar gravação"), MessageBoxButtons.OK, MessageBoxIcon.Error);
                return false;
            }
        }

        private bool SaveActiveMacroSettings()
        {
            CommitPlaybackTiming();
            MacroChoice selected = macroCombo.SelectedItem as MacroChoice;
            if (!macroSettingsDirty || activeMacro == null || selected == null ||
                !String.Equals(selected.Path, activeMacroPath, StringComparison.OrdinalIgnoreCase)) return false;

            try
            {
                if (!File.Exists(activeMacroPath))
                {
                    LoadMacros();
                    return false;
                }

                string targetPath = GetMacroSettingsTargetPath();
                string targetName = Path.GetFileNameWithoutExtension(targetPath);
                bool sameFile = PathsEqual(activeMacroPath, targetPath);
                if (!sameFile && File.Exists(targetPath))
                {
                    DialogResult replace = MessageBox.Show(this,
                        Tr("Já existe um macro com esse nome. Deseja substituir os dados e as opções salvas?"),
                        Tr("Substituir macro?"), MessageBoxButtons.YesNo, MessageBoxIcon.Warning);
                    if (replace != DialogResult.Yes) return false;
                }

                MacroFile macroToSave = CreateMacroSettingsSnapshot(targetName, sameFile);
                WriteMacroAtomically(targetPath, json.Serialize(macroToSave));

                activeMacro = macroToSave;
                activeMacroPath = targetPath;
                settings.SelectedFile = targetPath;
                macroSettingsDirty = false;
                SaveSettings();
                UpdateRuntimeUi("Macro salvo", "", false);
                statusLabel.Text = Tr("Alterações salvas") + ": " + targetName;
                if (sameFile)
                {
                    selected.Name = targetName;
                    selected.CycleInfinite = macroToSave.CycleInfinite;
                    selected.IntervalMinutes = macroToSave.IntervalMinutes;
                    selected.IntervalSeconds = macroToSave.IntervalSeconds;
                    selected.VaryPlayback = macroToSave.VaryPlayback;
                    selected.SmoothCursorReturn = macroToSave.SmoothCursorReturn;
                    UpdateSelectedMacro();
                }
                else
                {
                    nameBox.Text = targetName;
                    LoadMacros(targetPath);
                }
                return true;
            }
            catch (Exception ex)
            {
                UpdateRuntimeUi("Erro ao salvar", "", false);
                MessageBox.Show(this, Tr("Não foi possível salvar as alterações do macro.") + "\r\n\r\n" + ex.Message,
                    Tr("Erro ao salvar gravação"), MessageBoxButtons.OK, MessageBoxIcon.Error);
                return false;
            }
        }

        private MacroFile CreateMacroSettingsSnapshot(string name, bool sameFile)
        {
            // Do not change the loaded macro until replacing its file succeeds.
            return new MacroFile {
                Version = activeMacro.Version, Name = name,
                CreatedAt = sameFile ? activeMacro.CreatedAt : DateTime.Now,
                Events = new List<MacroEvent>(activeMacro.Events ?? new List<MacroEvent>()),
                StartMouseX = activeMacro.StartMouseX, StartMouseY = activeMacro.StartMouseY,
                CycleInfinite = infiniteCheck.Checked,
                IntervalMinutes = minutesInput.Value, IntervalSeconds = secondsInput.Value,
                VaryPlayback = variationCheck.Checked, SmoothCursorReturn = smoothReturnCheck.Checked
            };
        }

        private void CommitPlaybackTiming()
        {
            minutesInput.CommitValue();
            secondsInput.CommitValue();
        }

        private string GetMacroSettingsTargetPath()
        {
            string currentName = Path.GetFileNameWithoutExtension(activeMacroPath);
            string targetName = String.IsNullOrWhiteSpace(nameBox.Text) ? currentName : SafeFileName(nameBox.Text);
            return Path.Combine(settings.Folder, targetName + ".json");
        }

        private static bool PathsEqual(string first, string second)
        {
            if (String.IsNullOrWhiteSpace(first) || String.IsNullOrWhiteSpace(second)) return false;
            return String.Equals(Path.GetFullPath(first), Path.GetFullPath(second), StringComparison.OrdinalIgnoreCase);
        }

        private static void WriteMacroAtomically(string path, string contents)
        {
            string temporaryPath = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
            try
            {
                byte[] bytes = new UTF8Encoding(false).GetBytes(contents);
                using (FileStream stream = new FileStream(temporaryPath, FileMode.CreateNew, FileAccess.Write, FileShare.None))
                {
                    stream.Write(bytes, 0, bytes.Length);
                    stream.Flush(true);
                }
                if (File.Exists(path)) File.Replace(temporaryPath, path, null);
                else File.Move(temporaryPath, path);
            }
            finally
            {
                if (File.Exists(temporaryPath)) File.Delete(temporaryPath);
            }
        }

        private static void BalanceInputs(List<MacroEvent> events, long stopTimeUs)
        {
            HashSet<int> heldKeys = new HashSet<int>();
            Dictionary<int, MacroEvent> heldButtons = new Dictionary<int, MacroEvent>();
            for (int i = 0; i < events.Count; i++)
            {
                MacroEvent item = events[i]; if (item == null) continue;
                if (item.Kind == "keyDown") heldKeys.Add(item.Code);
                else if (item.Kind == "keyUp") heldKeys.Remove(item.Code);
                else if (item.Kind == "mouseDown") heldButtons[item.Code] = item;
                else if (item.Kind == "mouseUp") heldButtons.Remove(item.Code);
            }
            foreach (int key in heldKeys) events.Add(new MacroEvent { Kind = "keyUp", Code = key, TimeMs = stopTimeUs / 1000L, TimeUs = stopTimeUs });
            foreach (KeyValuePair<int, MacroEvent> entry in heldButtons)
                events.Add(new MacroEvent { Kind = "mouseUp", Code = entry.Key, X = entry.Value.X, Y = entry.Value.Y, TimeMs = stopTimeUs / 1000L, TimeUs = stopTimeUs });
        }

        public void RecordKey(string kind, int code)
        {
            if (!recording) return;
            CaptureRecordingStartPositionFromCursor();
            AddEvent(new MacroEvent { Kind = kind, Code = code });
        }

        public void RecordMouseMove(int x, int y)
        {
            if (!recording) return;
            CaptureRecordingStartPosition(x, y);
            AddEvent(new MacroEvent { Kind = "move", X = x, Y = y });
        }

        public void RecordMouseButton(string kind, int button, int x, int y)
        {
            if (!recording) return;
            CaptureRecordingStartPosition(x, y);
            AddEvent(new MacroEvent { Kind = kind, Code = button, X = x, Y = y });
        }

        public void RecordWheel(int x, int y, int delta)
        {
            if (!recording) return;
            CaptureRecordingStartPosition(x, y);
            AddEvent(new MacroEvent { Kind = "wheel", X = x, Y = y, Wheel = delta });
        }

        private void CaptureRecordingStartPositionFromCursor()
        {
            NativePoint point;
            if (GetCursorPos(out point)) CaptureRecordingStartPosition(point.X, point.Y);
        }

        private void CaptureRecordingStartPosition(int x, int y)
        {
            lock (eventsLock)
            {
                if (recordingStartMouseX.HasValue || recordingStartMouseY.HasValue) return;
                recordingStartMouseX = x;
                recordingStartMouseY = y;
            }
        }

        private void AddEvent(MacroEvent e)
        {
            e.TimeUs = RecordElapsedMicroseconds;
            e.TimeMs = e.TimeUs.Value / 1000L;
            lock (eventsLock)
            {
                if (recordedEvents.Count < MaxEvents) recordedEvents.Add(e);
                else if (!recordLimitQueued)
                {
                    recordLimitQueued = true;
                    PostToUi((MethodInvoker)delegate { StopRecording(); UpdateRuntimeUi("Limite de ações atingido", "", false); });
                }
            }
        }

        private void TogglePlayback()
        {
            if (visualRecognitionPage != null && visualRecognitionPage.IsRunning) { visualRecognitionPage.StopExecution(); return; }
            if (recording) { UpdateRuntimeUi("Pare a gravação antes de reproduzir", "", true); return; }
            if (playing) { StopPlayback(); return; }
            if (pendingEvents != null && pendingEvents.Count > 0 && pendingPreviewActive)
            {
                MacroFile preview = new MacroFile {
                    Version = 1,
                    Name = String.IsNullOrWhiteSpace(nameBox.Text) ? Tr("Prévia") : nameBox.Text.Trim(),
                    CreatedAt = DateTime.Now,
                    Events = new List<MacroEvent>(pendingEvents),
                    StartMouseX = recordingStartMouseX,
                    StartMouseY = recordingStartMouseY
                };
                StartPlayback(preview);
                return;
            }
            MacroFile macro = activeMacro;
            MacroChoice choice = macroCombo.SelectedItem as MacroChoice;
            if (macro == null && choice != null) macro = ReadMacro(choice.Path);
            if (macro == null || macro.Events == null || macro.Events.Count == 0)
            {
                MessageBox.Show(this, Tr("Este arquivo não contém ações válidas para reproduzir."), Tr("Macro inválido"), MessageBoxButtons.OK, MessageBoxIcon.Information);
                LoadMacros();
                return;
            }
            StartPlayback(macro);
        }

        private void StartPlayback(MacroFile macro)
        {
            if (playing || recording || activatingMacro) return;
            CommitPlaybackTiming();
            playing = true;
            playbackGeneration++;
            int generation = playbackGeneration;
            CancellationTokenSource source = new CancellationTokenSource();
            playbackCancel = source;
            MacroPlaybackInput input = new MacroPlaybackInput(source.Token, PlayEvent, ReleasePlaybackInput);
            double intervalSeconds = (double)minutesInput.Value * 60.0 + (double)secondsInput.Value;
            bool repeat = infiniteCheck.Checked;
            bool varyPlayback = variationCheck.Checked;
            bool smoothReturn = smoothReturnCheck.Checked;
            Rectangle[] screens = Array.ConvertAll(Screen.AllScreens, delegate(Screen screen) { return screen.Bounds; });
            NativePoint macroMouseAnchor = new NativePoint();
            bool hasMouseAnchor = TryGetFirstMousePosition(macro, out macroMouseAnchor);
            bool resetCursorToMacroStart = !IsMacroDeckForeground && hasMouseAnchor;
            UiSounds.PlaybackStart();
            UpdateRuntimeUi("Reproduzindo", "", true);

            Thread playbackThread = new Thread(delegate()
            {
                string error = null;
                IntPtr playbackTimer = IntPtr.Zero;
                bool timerIsHighResolution = false;
                bool timerResolutionRequested = false;
                try
                {
                    playbackTimer = CreatePlaybackTimer(out timerIsHighResolution);
                    if (playbackTimer == IntPtr.Zero || !timerIsHighResolution)
                        timerResolutionRequested = timeBeginPeriod(1) == 0;

                    WaitHandle cancellationWaitHandle = source.Token.WaitHandle;
                    IntPtr cancelHandle = cancellationWaitHandle.SafeWaitHandle.DangerousGetHandle();
                    IntPtr[] waitHandles = playbackTimer == IntPtr.Zero
                        ? new IntPtr[] { cancelHandle }
                        : new IntPtr[] { playbackTimer, cancelHandle };
                    Random random = new Random(Guid.NewGuid().GetHashCode());

                    do
                    {
                        PlaybackVariation variation = new PlaybackVariation(varyPlayback, random, screens);
                        Point anchor = variation.Position(new Point(macroMouseAnchor.X, macroMouseAnchor.Y));
                        if (smoothReturn && hasMouseAnchor)
                        {
                            if (!MoveCursorSmoothly(anchor, screens, random, input, source.Token, playbackTimer, waitHandles)) break;
                        }
                        else if (resetCursorToMacroStart)
                            input.Send(new MacroEvent { Kind = "move", X = anchor.X, Y = anchor.Y });
                        long cycleStartTimestamp = Stopwatch.GetTimestamp();
                        for (int i = 0; i < macro.Events.Count; i++)
                        {
                            if (source.IsCancellationRequested) break;
                            MacroEvent item = macro.Events[i];
                            if (item == null || item.Kind == "limit") continue;
                            long eventTime = variation.Time(GetEventTimeMicroseconds(item));
                            if (!WaitForPlaybackTime(cycleStartTimestamp, eventTime, source.Token, playbackTimer, waitHandles)) break;
                            input.Send(variation.Event(item));
                        }
                        if (smoothReturn && hasMouseAnchor && !source.IsCancellationRequested)
                        {
                            input.ReleaseHeld();
                            if (!MoveCursorSmoothly(anchor, screens, random, input, source.Token, playbackTimer, waitHandles)) break;
                        }
                        if (!repeat || source.IsCancellationRequested) break;
                        if (intervalSeconds > 0)
                        {
                            long intervalTicks = (long)(intervalSeconds * Stopwatch.Frequency);
                            long intervalDeadline = Stopwatch.GetTimestamp() + intervalTicks;
                            if (!WaitUntilTimestamp(intervalDeadline, source.Token, playbackTimer, waitHandles)) break;
                        }
                    } while (!source.IsCancellationRequested);
                }
                catch (OperationCanceledException) { }
                catch (Exception ex) { error = ex.Message; }
                finally
                {
                    try { input.ReleaseHeld(); }
                    catch (Exception ex) { if (error == null) error = ex.Message; }
                    if (timerResolutionRequested) timeEndPeriod(1);
                    if (playbackTimer != IntPtr.Zero) CloseHandle(playbackTimer);
                    if (IsDisposed || !IsHandleCreated) source.Dispose();
                    else
                    {
                        if (!PostToUi((MethodInvoker)delegate
                        {
                            if (generation == playbackGeneration)
                            {
                                playing = false;
                                playbackCancel = null;
                                UpdateRuntimeUi(error != null ? "Erro na reprodução" : (source.IsCancellationRequested ? "Reprodução parada" : "Macro finalizado"), "", false);
                            }
                            source.Dispose();
                            if (closeAfterPlayback && !playing && !IsDisposed) Close();
                        })) source.Dispose();
                    }
                }
            });
            playbackThread.IsBackground = true;
            playbackThread.Name = "MacroDeck playback";
            try { playbackThread.Priority = ThreadPriority.AboveNormal; }
            catch { }
            playbackThread.Start();
        }

        private static bool MoveCursorSmoothly(Point target, Rectangle[] screens, Random random, MacroPlaybackInput input,
            CancellationToken cancellation, IntPtr timer, IntPtr[] waitHandles)
        {
            cancellation.ThrowIfCancellationRequested();
            NativePoint current;
            if (!GetCursorPos(out current)) throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error());
            long started = Stopwatch.GetTimestamp();
            return PlaybackMotion.Move(new Point(current.X, current.Y), target, screens, random,
                delegate(long timeUs) { return WaitForPlaybackTime(started, timeUs, cancellation, timer, waitHandles); },
                delegate(Point point) { input.Send(new MacroEvent { Kind = "move", X = point.X, Y = point.Y }); }, cancellation);
        }

        private static IntPtr CreatePlaybackTimer(out bool highResolution)
        {
            highResolution = false;
            try
            {
                IntPtr timer = CreateWaitableTimerEx(IntPtr.Zero, null, CreateWaitableTimerHighResolution, TimerModifyStateAndSynchronize);
                if (timer != IntPtr.Zero)
                {
                    highResolution = true;
                    return timer;
                }

                timer = CreateWaitableTimerEx(IntPtr.Zero, null, 0, TimerModifyStateAndSynchronize);
                if (timer != IntPtr.Zero) return timer;
            }
            catch (EntryPointNotFoundException) { }

            return CreateWaitableTimer(IntPtr.Zero, false, null);
        }

        private static bool TryGetFirstMousePosition(MacroFile macro, out NativePoint position)
        {
            position = new NativePoint();
            if (macro == null || macro.Events == null) return false;
            if (macro.StartMouseX.HasValue && macro.StartMouseY.HasValue)
            {
                position.X = macro.StartMouseX.Value;
                position.Y = macro.StartMouseY.Value;
                return true;
            }
            for (int i = 0; i < macro.Events.Count; i++)
            {
                MacroEvent item = macro.Events[i];
                if (item != null && (item.Kind == "move" || item.Kind == "wheel" || item.Kind == "mouseDown" || item.Kind == "mouseUp"))
                {
                    position.X = item.X;
                    position.Y = item.Y;
                    return true;
                }
            }
            return false;
        }

        private static long GetEventTimeMicroseconds(MacroEvent item)
        {
            if (item.TimeUs.HasValue) return Math.Max(0L, item.TimeUs.Value);
            long milliseconds = Math.Max(0L, item.TimeMs);
            return milliseconds > Int64.MaxValue / 1000L ? Int64.MaxValue : milliseconds * 1000L;
        }

        private static bool WaitForPlaybackTime(long cycleStartTimestamp, long targetMicroseconds, CancellationToken token, IntPtr playbackTimer, IntPtr[] waitHandles)
        {
            double targetOffsetTicks = targetMicroseconds * (double)Stopwatch.Frequency / 1000000.0;
            long offsetTicks = targetOffsetTicks >= Int64.MaxValue ? Int64.MaxValue : (long)Math.Round(targetOffsetTicks);
            long deadline = cycleStartTimestamp > Int64.MaxValue - offsetTicks ? Int64.MaxValue : cycleStartTimestamp + offsetTicks;
            return WaitUntilTimestamp(deadline, token, playbackTimer, waitHandles);
        }

        private static bool WaitUntilTimestamp(long deadline, CancellationToken token, IntPtr playbackTimer, IntPtr[] waitHandles)
        {
            long spinThresholdTicks = Math.Max(1L, Stopwatch.Frequency * TimerSpinThresholdUs / 1000000L);
            while (true)
            {
                if (token.IsCancellationRequested) return false;
                long remainingTicks = deadline - Stopwatch.GetTimestamp();
                if (remainingTicks <= 0) return true;

                if (remainingTicks <= spinThresholdTicks)
                {
                    Thread.SpinWait(64);
                    continue;
                }

                if (playbackTimer == IntPtr.Zero)
                {
                    double remainingMilliseconds = remainingTicks * 1000.0 / Stopwatch.Frequency;
                    int sleepMilliseconds = (int)Math.Min(10.0, Math.Max(1.0, Math.Floor(remainingMilliseconds - 1.0)));
                    Thread.Sleep(sleepMilliseconds);
                    continue;
                }

                long timerWaitTicks = remainingTicks - spinThresholdTicks;
                long dueTime = -Math.Max(1L, (long)Math.Ceiling(timerWaitTicks * 10000000.0 / Stopwatch.Frequency));
                if (!SetWaitableTimer(playbackTimer, ref dueTime, 0, IntPtr.Zero, IntPtr.Zero, false))
                    throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error(), "Não foi possível agendar o próximo passo do macro.");

                uint waitResult = WaitForMultipleObjects((uint)waitHandles.Length, waitHandles, false, InfiniteWait);
                if (waitResult == WaitObject0 + 1) return false;
                if (waitResult == WaitFailed)
                    throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error(), "Não foi possível aguardar o próximo passo do macro.");
                if (waitResult != WaitObject0)
                    throw new InvalidOperationException("O temporizador de reprodução retornou um estado inesperado.");
            }
        }

        private void PlayEvent(MacroEvent e)
        {
            if (e.Kind == "keyDown" || e.Kind == "keyUp")
                keybd_event((byte)e.Code, 0, e.Kind == "keyUp" ? 0x0002u : 0u, UIntPtr.Zero);
            else if (e.Kind == "move")
                SendMousePlaybackInput(e.X, e.Y, 0, 0);
            else if (e.Kind == "wheel")
                SendMousePlaybackInput(e.X, e.Y, MouseFlagWheel, unchecked((uint)e.Wheel));
            else if (e.Kind == "mouseDown" || e.Kind == "mouseUp")
            {
                int button = Math.Max(0, Math.Min(MouseDownFlags.Length - 1, e.Code));
                uint buttonFlags = e.Kind == "mouseDown" ? MouseDownFlags[button] : MouseUpFlags[button];
                SendMousePlaybackInput(e.X, e.Y, buttonFlags, MouseData[button]);
            }
        }

        private void ReleasePlaybackInput(MacroEvent item)
        {
            if (item.Kind == "keyUp")
            {
                keybd_event((byte)item.Code, 0, 0x0002u, UIntPtr.Zero);
                return;
            }
            int button = Math.Max(0, Math.Min(MouseUpFlags.Length - 1, item.Code));
            NativeInput[] inputs = new NativeInput[1];
            inputs[0].Type = InputMouse;
            inputs[0].Data.Mouse = new MouseInput { Flags = MouseUpFlags[button], Data = MouseData[button] };
            // Release at the current cursor position; cleanup must not move it back.
            if (SendInput(1, inputs, Marshal.SizeOf(typeof(NativeInput))) != 1)
                throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error(), "O Windows não aceitou a ação do mouse durante a reprodução.");
        }

        private static void SendMousePlaybackInput(int x, int y, uint actionFlags, uint actionData)
        {
            MoveCursorTo(x, y);
            if (actionFlags == 0) return;

            NativeInput[] inputs = new NativeInput[1];
            inputs[0].Type = InputMouse;
            inputs[0].Data.Mouse = new MouseInput {
                Data = actionData,
                Flags = actionFlags,
                ExtraInfo = IntPtr.Zero
            };

            if (SendInput(1, inputs, Marshal.SizeOf(typeof(NativeInput))) != 1)
            {
                int error = Marshal.GetLastWin32Error();
                throw new System.ComponentModel.Win32Exception(error, "O Windows não aceitou a ação do mouse durante a reprodução.");
            }
        }

        private static void MoveCursorTo(int x, int y)
        {
            if (SetCursorPos(x, y)) return;
            int error = Marshal.GetLastWin32Error();
            throw new System.ComponentModel.Win32Exception(error, "O Windows não conseguiu posicionar o cursor para a reprodução.");
        }

        private void StopPlayback()
        {
            if (!playing) return;
            if (playbackCancel == null || playbackCancel.IsCancellationRequested) return;
            playbackCancel.Cancel();
            UiSounds.PlaybackStop();
            UpdateRuntimeUi("Parando…", "", true);
        }

        private void UpdateRuntimeUi(string state, string detail, bool active)
        {
            if (IsDisposed || statusLabel == null) return;
            if (InvokeRequired)
            {
                PostToUi((MethodInvoker)delegate { UpdateRuntimeUi(state, detail, active); });
                return;
            }
            statusLabel.Text = Tr(state);
            statusLabel.ForeColor = recording ? Color.FromArgb(246, 115, 122) : (playing ? palette.Accent : palette.Muted);
            recordButton.Text = Tr(recording ? "Parar gravação" : "Gravar");
            recordButton.Icon = recording ? "stop" : "record";
            playButton.Text = Tr(playing ? "Parar reprodução" : (pendingEvents != null && pendingEvents.Count > 0 && pendingPreviewActive ? "Testar gravação" : "Reproduzir"));
            playButton.Icon = playing ? "stop" : "play";
            recordButton.Enabled = !playing || recording;
            playButton.Enabled = !recording || playing;
            recordButton.Tag = recording ? "recording" : "primary";
            playButton.Tag = playing ? "playing" : "primary";
            recordButton.BackColor = recording ? Color.FromArgb(193, 64, 73) : palette.Accent;
            playButton.BackColor = playing ? Color.FromArgb(193, 64, 73) : palette.Accent;
            saveButton.Enabled = !recording && pendingEvents != null && pendingEvents.Count > 0;
            UpdateMacroActions();
            recordButton.Invalidate(); playButton.Invalidate();
        }

        private void ChooseFolder(object sender, EventArgs e)
        {
            if (folderPickerInProgress || IsDisposed) return;
            folderPickerInProgress = true;
            folderButton.Enabled = false;
            folderButton.Text = Tr("Abrindo...");
            Cursor = Cursors.Default;
            IntPtr ownerHandle = Handle;
            string currentPath = settings.Folder;
            Thread pickerThread = new Thread(delegate()
            {
                string folder = null;
                Exception failure = null;
                try { folder = new WindowsFolderPicker().PickFolder(ownerHandle, currentPath); }
                catch (Exception ex) { failure = ex; }
                if (IsDisposed || !IsHandleCreated) return;
                PostToUi((MethodInvoker)delegate
                {
                    folderPickerInProgress = false;
                    if (folderButton != null && !folderButton.IsDisposed)
                    {
                        folderButton.Enabled = true;
                        folderButton.Text = Tr("Escolher pasta");
                    }
                    Cursor = Cursors.Default;
                    if (failure != null)
                    {
                        MessageBox.Show(this, Tr("Não foi possível abrir a seleção de pasta do Windows.") + "\r\n\r\n" + failure.Message, Tr("Pasta indisponível"), MessageBoxButtons.OK, MessageBoxIcon.Warning);
                        return;
                    }
                    if (String.IsNullOrWhiteSpace(folder)) return;
                    settings.Folder = folder;
                    if (visualRecognitionPage != null) visualRecognitionPage.SetMacroFolder(folder);
                    folderBox.Text = settings.Folder;
                    SaveSettings();
                    LoadMacros();
                });
            });
            pickerThread.IsBackground = true;
            pickerThread.SetApartmentState(ApartmentState.STA);
            pickerThread.Start();
        }

        private void LoadMacros() { LoadMacros(null); }

        private void LoadMacros(string selectPath)
        {
            if (IsDisposed || macroCombo == null) return;
            string folder = settings.Folder;
            string wanted = selectPath ?? (String.IsNullOrWhiteSpace(activeMacroPath) ? settings.SelectedFile : activeMacroPath);
            bool legacyCycleInfinite = legacyMacroDefaultCycleInfinite;
            int legacyIntervalMinutes = legacyMacroDefaultIntervalMinutes;
            int legacyIntervalSeconds = legacyMacroDefaultIntervalSeconds;
            int generation = Interlocked.Increment(ref macroLoadGeneration);
            ThreadPool.QueueUserWorkItem(delegate
            {
                List<MacroChoice> choices = new List<MacroChoice>();
                MacroFile firstMacro = null;
                MacroFile wantedMacro = null;
                Exception failure = null;
                try
                {
                    Directory.CreateDirectory(folder);
                    string[] files = Directory.GetFiles(folder, "*.json");
                    Array.Sort(files, StringComparer.OrdinalIgnoreCase);
                    Dictionary<string, int> displayNameCounts = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
                    JavaScriptSerializer reader = new JavaScriptSerializer { MaxJsonLength = Int32.MaxValue };
                    for (int i = 0; i < files.Length; i++)
                    {
                        MacroFile macro = ReadMacro(files[i], reader);
                        if (macro == null) continue;
                        bool needsPlaybackSettings = !macro.CycleInfinite.HasValue || !macro.IntervalMinutes.HasValue || !macro.IntervalSeconds.HasValue;
                        if (needsPlaybackSettings)
                        {
                            if (!macro.CycleInfinite.HasValue) macro.CycleInfinite = legacyCycleInfinite;
                            if (!macro.IntervalMinutes.HasValue) macro.IntervalMinutes = legacyIntervalMinutes;
                            if (!macro.IntervalSeconds.HasValue) macro.IntervalSeconds = legacyIntervalSeconds;
                            try { WriteMacroAtomically(files[i], reader.Serialize(macro)); }
                            catch { }
                        }
                        if (firstMacro == null)
                        {
                            firstMacro = macro;
                        }
                        if (String.Equals(files[i], wanted, StringComparison.OrdinalIgnoreCase)) wantedMacro = macro;
                        string name = CleanMacroName(String.IsNullOrWhiteSpace(macro.Name) ? Path.GetFileNameWithoutExtension(files[i]) : macro.Name, Localization.IsEnglish(uiLanguage));
                        int duplicateCount;
                        if (displayNameCounts.TryGetValue(name, out duplicateCount))
                        {
                            duplicateCount++;
                            displayNameCounts[name] = duplicateCount;
                            name += " (" + duplicateCount.ToString() + ")";
                        }
                        else displayNameCounts.Add(name, 1);
                        choices.Add(new MacroChoice {
                            Path = files[i],
                            Name = name,
                            CycleInfinite = macro.CycleInfinite ?? legacyCycleInfinite,
                            IntervalMinutes = macro.IntervalMinutes ?? legacyIntervalMinutes,
                            IntervalSeconds = macro.IntervalSeconds ?? legacyIntervalSeconds,
                            VaryPlayback = macro.VaryPlayback, SmoothCursorReturn = macro.SmoothCursorReturn
                        });
                    }
                }
                catch (Exception ex) { failure = ex; }

                MacroFile macroToActivate = wantedMacro ?? firstMacro;

                if (IsDisposed || !IsHandleCreated) return;
                PostToUi((MethodInvoker)delegate
                {
                    if (IsDisposed || generation != macroLoadGeneration) return;
                    refreshingMacros = true;
                    macroCombo.BeginUpdate();
                    macroCombo.ClearItems();
                    for (int i = 0; i < choices.Count; i++)
                    {
                        macroCombo.Items.Add(choices[i]);
                        if (String.Equals(choices[i].Path, wanted, StringComparison.OrdinalIgnoreCase)) macroCombo.SelectedIndex = i;
                    }
                    if (macroCombo.SelectedIndex < 0 && macroCombo.Items.Count > 0) macroCombo.SelectedIndex = 0;
                    macroCombo.EndUpdate();
                    refreshingMacros = false;
                    if (failure != null)
                    {
                        UpdateRuntimeUi("Pasta indisponível", "", false);
                        MessageBox.Show(this, failure.Message, Tr("Pasta indisponível"), MessageBoxButtons.OK, MessageBoxIcon.Warning);
                        return;
                    }
                    MacroChoice selected = macroCombo.SelectedItem as MacroChoice;
                    if (selected == null)
                    {
                        activeMacro = null;
                        activeMacroPath = "";
                        settings.SelectedFile = "";
                        SaveSettings();
                        if (pendingEvents == null) eventsLabel.Text = "";
                    }
                    else
                    {
                        bool activePathStillAvailable = false;
                        for (int i = 0; i < choices.Count; i++)
                        {
                            if (String.Equals(choices[i].Path, activeMacroPath, StringComparison.OrdinalIgnoreCase))
                            {
                                activePathStillAvailable = true;
                                break;
                            }
                        }
                        bool shouldActivateSelection = selectPath != null || activeMacro == null || !activePathStillAvailable;
                        if (shouldActivateSelection)
                        {
                            activeMacro = macroToActivate;
                            activeMacroPath = selected.Path;
                            settings.SelectedFile = selected.Path;
                            ApplyMacroPlaybackSettings(selected);
                            if (pendingEvents == null) nameBox.Text = Path.GetFileNameWithoutExtension(selected.Path);
                        }
                        else settings.SelectedFile = activeMacroPath;
                        SaveSettings();
                        UpdateSelectedMacro();
                    }
                    UpdateMacroActions();
                });
            });
        }

        private MacroFile ReadMacro(string path)
        {
            return ReadMacro(path, json);
        }

        private static MacroFile ReadMacro(string path, JavaScriptSerializer serializer)
        {
            try
            {
                MacroFile macro = serializer.Deserialize<MacroFile>(File.ReadAllText(path, Encoding.UTF8));
                return macro != null && macro.Events != null ? macro : null;
            }
            catch { return null; }
        }

        private void UseSelectedMacro()
        {
            MacroChoice choice = macroCombo.SelectedItem as MacroChoice;
            if (choice == null || recording || playing || activatingMacro) return;

            activatingMacro = true;
            macroCombo.Enabled = false;
            statusLabel.Text = Tr("Carregando macro...");
            UpdateMacroActions();
            ThreadPool.QueueUserWorkItem(delegate
            {
                MacroFile macro = ReadMacro(choice.Path, new JavaScriptSerializer { MaxJsonLength = Int32.MaxValue });
                if (IsDisposed || !IsHandleCreated) return;
                PostToUi((MethodInvoker)delegate
                {
                    if (IsDisposed) return;
                    activatingMacro = false;
                    macroCombo.Enabled = true;
                    if (macro == null || macro.Events == null || macro.Events.Count == 0)
                    {
                        UpdateRuntimeUi("Pronto", "", false);
                        MessageBox.Show(this, Tr("Este arquivo não contém ações válidas para reproduzir."), Tr("Macro inválido"), MessageBoxButtons.OK, MessageBoxIcon.Information);
                        LoadMacros();
                        return;
                    }

                    activeMacro = macro;
                    activeMacroPath = choice.Path;
                    settings.SelectedFile = choice.Path;
                    pendingPreviewActive = false;
                    ApplyMacroPlaybackSettings(choice);
                    if (pendingEvents == null) nameBox.Text = Path.GetFileNameWithoutExtension(choice.Path);
                    SaveSettings();
                    UpdateMacroActions();
                    UpdateSelectedMacro();
                    UpdateRuntimeUi("Pronto", "", false);
                    statusLabel.Text = Tr("Macro ativado") + ": " + choice.Name;
                });
            });
        }

        private void ApplyMacroPlaybackSettings(MacroChoice choice)
        {
            if (choice == null || !choice.HasPlaybackSettings) return;

            bool cycleInfinite = choice.CycleInfinite.Value;
            int minutes = Math.Max(0, Math.Min(choice.IntervalMinutes.Value, 9999));
            int seconds = Math.Max(0, Math.Min(choice.IntervalSeconds.Value, 59));

            applyingMacroSettings = true;
            try
            {
                infiniteCheck.Checked = cycleInfinite;
                minutesInput.Value = minutes;
                secondsInput.Value = seconds;
                variationCheck.Checked = choice.VaryPlayback ?? false;
                smoothReturnCheck.Checked = choice.SmoothCursorReturn ?? false;
            }
            finally { applyingMacroSettings = false; }

            settings.CycleInfinite = cycleInfinite;
            settings.Repeat = cycleInfinite;
            settings.IntervalMinutes = minutes;
            settings.IntervalSeconds = seconds;
            settings.Interval = minutes * 60 + seconds;
            settings.IntervalUnit = "segundos";
            settings.VaryPlayback = variationCheck.Checked;
            settings.SmoothCursorReturn = smoothReturnCheck.Checked;
            if (activeMacro != null)
            {
                activeMacro.CycleInfinite = cycleInfinite;
                activeMacro.IntervalMinutes = minutes;
                activeMacro.IntervalSeconds = seconds;
                activeMacro.VaryPlayback = variationCheck.Checked;
                activeMacro.SmoothCursorReturn = smoothReturnCheck.Checked;
            }
            macroSettingsDirty = false;
        }

        private void UpdateSelectedMacro()
        {
            if (pendingEvents != null && !recording)
            {
                eventsLabel.Text = Tr("Gravação não salva · pronta para testar ou salvar");
                return;
            }
            MacroChoice choice = macroCombo.SelectedItem as MacroChoice;
            if (choice == null) { eventsLabel.Text = ""; return; }
            eventsLabel.Text = String.Equals(choice.Path, activeMacroPath, StringComparison.OrdinalIgnoreCase)
                ? ""
                : Tr("Macro selecionado. Clique em Usar Macro Selecionado para ativar.");
        }

        private void UpdateMacroActions()
        {
            if (deleteSelectedButton == null) return;
            if (variationCheck != null) variationCheck.Enabled = !recording && !playing && !activatingMacro;
            if (smoothReturnCheck != null) smoothReturnCheck.Enabled = !recording && !playing && !activatingMacro;
            deleteSelectedButton.Enabled = !recording && !playing && !activatingMacro && macroCombo.SelectedItem != null;
            deleteAllButton.Enabled = !recording && !playing && !activatingMacro && macroCombo.Items.Count > 0;
            if (refreshButton != null) refreshButton.Enabled = !activatingMacro;
            if (useMacroButton != null)
                useMacroButton.Enabled = !recording && !playing && !activatingMacro && macroCombo.SelectedItem != null;
            if (saveButton != null)
            {
                bool hasRecording = !recording && !playing && pendingEvents != null && pendingEvents.Count > 0;
                MacroChoice selected = macroCombo.SelectedItem as MacroChoice;
                bool canSaveMacroSettings = !recording && !playing && !activatingMacro && macroSettingsDirty && activeMacro != null &&
                    selected != null && String.Equals(selected.Path, activeMacroPath, StringComparison.OrdinalIgnoreCase);
                string saveLabel = hasRecording || recording ? "Salvar gravação" : (macroSettingsDirty ? "Salvar alterações" : "Salvar gravação");
                if (!hasRecording && !recording && canSaveMacroSettings)
                {
                    string targetPath = GetMacroSettingsTargetPath();
                    if (!PathsEqual(targetPath, activeMacroPath) && !File.Exists(targetPath)) saveLabel = "Salvar como novo macro";
                }
                saveButton.Text = Tr(saveLabel);
                saveButton.Enabled = hasRecording || canSaveMacroSettings;
                LayoutMacroActionButtons(saveButton.Parent as RoundedPanel);
            }
            if (playButton != null)
                playButton.Enabled = !activatingMacro && (playing || (!recording && ((pendingEvents != null && pendingEvents.Count > 0) || macroCombo.SelectedItem != null)));
        }

        private void DeleteSelectedMacro()
        {
            if (recording || playing) return;
            MacroChoice choice = macroCombo.SelectedItem as MacroChoice;
            if (choice == null) return;
            DialogResult confirm = MessageBox.Show(this, Tr("Apagar o macro") + " \"" + choice.Name + "\" " + Tr("permanentemente?"), Tr("Apagar macro"), MessageBoxButtons.YesNo, MessageBoxIcon.Warning);
            if (confirm != DialogResult.Yes) return;
            try
            {
                File.Delete(choice.Path);
                LoadMacros();
                UpdateRuntimeUi("Macro apagado", "", false);
            }
            catch (Exception ex) { MessageBox.Show(this, Tr("Não foi possível apagar o macro.") + "\r\n\r\n" + ex.Message, Tr("Erro ao apagar"), MessageBoxButtons.OK, MessageBoxIcon.Error); }
        }

        private void DeleteAllMacros()
        {
            if (recording || playing) return;
            List<string> paths = new List<string>();
            try
            {
                if (Directory.Exists(settings.Folder))
                {
                    string[] files = Directory.GetFiles(settings.Folder, "*.json");
                    for (int i = 0; i < files.Length; i++) if (ReadMacro(files[i]) != null) paths.Add(files[i]);
                }
            }
            catch (Exception ex) { MessageBox.Show(this, ex.Message, Tr("Erro ao listar macros"), MessageBoxButtons.OK, MessageBoxIcon.Error); return; }
            if (paths.Count == 0) { LoadMacros(); return; }
            DialogResult confirm = MessageBox.Show(this, Tr("Apagar permanentemente os ") + paths.Count.ToString() + Tr(" macros válidos desta pasta?"), Tr("Apagar todos os macros"), MessageBoxButtons.YesNo, MessageBoxIcon.Warning);
            if (confirm != DialogResult.Yes) return;
            List<string> failures = new List<string>();
            for (int i = 0; i < paths.Count; i++)
            {
                try { File.Delete(paths[i]); }
                catch (Exception ex) { failures.Add(Path.GetFileName(paths[i]) + ": " + ex.Message); }
            }
            LoadMacros();
            if (failures.Count == 0) UpdateRuntimeUi("Macros apagados", "", false);
            else MessageBox.Show(this, Tr("Alguns arquivos não puderam ser apagados:") + "\r\n\r\n" + String.Join("\r\n", failures.ToArray()), Tr("Exclusão incompleta"), MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }

        private static string CleanMacroName(string name, bool english)
        {
            if (String.IsNullOrWhiteSpace(name)) return english ? "New recording" : "Nova gravação";
            string value = name.Trim();
            if (Regex.IsMatch(value, @"^Macro \d{4}-\d{2}-\d{2} \d{2}-\d{2}-\d{2}(?:\s*\(\d+\))*$")) return english ? "Recording" : "Gravação";
            string cleaned = Regex.Replace(value, @"(?:\s*\(\d+\)){2,}$", "");
            return String.IsNullOrWhiteSpace(cleaned) ? value : cleaned;
        }

        private static string SafeFileName(string name)
        {
            char[] invalid = Path.GetInvalidFileNameChars();
            StringBuilder builder = new StringBuilder(name.Trim());
            for (int i = 0; i < builder.Length; i++) if (Array.IndexOf(invalid, builder[i]) >= 0) builder[i] = '-';
            string result = builder.ToString().Trim().TrimEnd('.');
            if (result.EndsWith(".json", StringComparison.OrdinalIgnoreCase)) result = result.Substring(0, result.Length - 5).TrimEnd().TrimEnd('.');
            if (String.IsNullOrWhiteSpace(result)) return "Macro";
            if (Regex.IsMatch(result, @"^(CON|PRN|AUX|NUL|COM[1-9]|LPT[1-9])$", RegexOptions.IgnoreCase)) result = "Macro " + result;
            return result.Length > 120 ? result.Substring(0, 120).TrimEnd().TrimEnd('.') : result;
        }

        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            // Request cancellation on the first close attempt, before ChromeForm's
            // fade. Actual disposal still waits for worker cleanup in OnClosing.
            if (playing) StopPlayback();
            if (visualRecognitionPage != null) visualRecognitionPage.StopExecution();
            base.OnFormClosing(e);
        }

        private void OnClosing(object sender, FormClosingEventArgs e)
        {
            if (playing)
            {
                closeAfterPlayback = true;
                e.Cancel = true;
                StopPlayback();
                return;
            }
            if (visualRecognitionPage != null) visualRecognitionPage.StopExecution();
            FinishThemeTransition();
            if (recording) StopRecording();
            pendingEvents = null;
            if (playbackCancel != null) playbackCancel.Cancel();
            SaveSettings();
            if (toolTips != null) toolTips.Dispose();
            if (hooks != null) hooks.Dispose();
            if (pageTransitionTimer != null) { if (pageTransitionTimer.Enabled) UiAnimationClock.End(); pageTransitionTimer.Stop(); pageTransitionTimer.Dispose(); pageTransitionTimer = null; }
            if (themeTransitionTimer != null)
            {
                if (themeTransitionTimer.Enabled) UiAnimationClock.End();
                themeTransitionTimer.Stop();
                themeTransitionTimer.Dispose();
                themeTransitionTimer = null;
            }
            themeTransitionClock.Stop();
            if (optionsDialog != null) { optionsDialog.Dispose(); optionsDialog = null; }
            if (aboutDialog != null) { aboutDialog.Dispose(); aboutDialog = null; }
            if (appMenu != null) { appMenu.Dispose(); appMenu = null; }
            if (transitionSurface != null) { transitionSurface.Dispose(); transitionSurface = null; }
        }

        private sealed class ThemeTransitionSurface : Control
        {
            private readonly Bitmap previousFrame;
            private readonly Bitmap currentFrame;
            private float progress;

            public float Progress
            {
                get { return progress; }
                set { progress = Math.Max(0, Math.Min(1, value)); Invalidate(); }
            }

            public ThemeTransitionSurface(Bitmap oldFrame, Bitmap newFrame)
            {
                previousFrame = oldFrame;
                currentFrame = newFrame;
                SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint |
                    ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw | ControlStyles.Opaque, true);
            }

            protected override void OnPaintBackground(PaintEventArgs e) { }

            protected override void OnPaint(PaintEventArgs e)
            {
                if (previousFrame == null || currentFrame == null || ClientSize.Width < 1 || ClientSize.Height < 1) return;
                e.Graphics.DrawImage(previousFrame, ClientRectangle);
                if (progress <= 0f) return;
                using (ImageAttributes attributes = new ImageAttributes())
                {
                    ColorMatrix matrix = new ColorMatrix();
                    matrix.Matrix33 = progress;
                    attributes.SetColorMatrix(matrix, ColorMatrixFlag.Default, ColorAdjustType.Bitmap);
                    e.Graphics.DrawImage(currentFrame, ClientRectangle, 0, 0, currentFrame.Width, currentFrame.Height,
                        GraphicsUnit.Pixel, attributes);
                }
            }

            protected override void Dispose(bool disposing)
            {
                if (disposing)
                {
                    if (previousFrame != null) previousFrame.Dispose();
                    if (currentFrame != null) currentFrame.Dispose();
                }
                base.Dispose(disposing);
            }
        }

        private sealed class PageTransitionSurface : Form
        {
            private readonly Bitmap frame;
            public float Progress
            {
                set { Opacity = Math.Max(0.01, 1.0 - Math.Max(0, Math.Min(1,value))); }
            }
            public PageTransitionSurface(Bitmap outgoing)
            {
                frame=outgoing;
                FormBorderStyle=FormBorderStyle.None;
                StartPosition=FormStartPosition.Manual;
                ShowInTaskbar=false;
                AutoScaleMode=AutoScaleMode.None;
                DoubleBuffered=true;
                Opacity=0.999;
            }
            protected override bool ShowWithoutActivation { get { return true; } }
            protected override CreateParams CreateParams
            {
                get { CreateParams parameters=base.CreateParams; parameters.ExStyle|=0x08000000|0x00000080|0x00000020; return parameters; }
            }
            protected override void OnPaintBackground(PaintEventArgs e) { }
            protected override void OnPaint(PaintEventArgs e) { e.Graphics.DrawImageUnscaled(frame,0,0); }
            protected override void Dispose(bool disposing)
            {
                if(disposing)frame.Dispose();
                base.Dispose(disposing);
            }
        }
    }
}
