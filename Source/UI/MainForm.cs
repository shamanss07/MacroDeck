using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
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
        private readonly string configPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "MacroDeck", "settings.json");
        private readonly JavaScriptSerializer json = new JavaScriptSerializer { MaxJsonLength = Int32.MaxValue };
        private readonly object eventsLock = new object();
        private readonly List<MacroEvent> recordedEvents = new List<MacroEvent>();
        private static readonly uint[] MouseDownFlags = { 0x0002, 0x0008, 0x0020, 0x0080, 0x0080 };
        private static readonly uint[] MouseUpFlags = { 0x0004, 0x0010, 0x0040, 0x0100, 0x0100 };
        private static readonly uint[] MouseData = { 0, 0, 0, 1, 2 };
        private static readonly int CurrentProcessId = Process.GetCurrentProcess().Id;

        private AppSettings settings;
        private string uiLanguage = "pt-BR";
        private NativeHooks hooks;
        private List<MacroEvent> pendingEvents;
        private readonly Stopwatch recordClock = new Stopwatch();
        private volatile bool recording;
        private volatile bool playing;
        private CancellationTokenSource playbackCancel;
        private int playbackGeneration;
        private bool recordLimitQueued;
        private bool captureRecord;
        private bool capturePlay;
        private bool refreshingMacros;
        private bool folderPickerInProgress;
        private bool activeClickerPage;
        private bool pageTransitionTarget;
        private int macroLoadGeneration;
        private string currentMonitorDevice = "";

        private Hotkey recordHotkey;
        private Hotkey playHotkey;
        private ThemePalette palette;
        private OptionsForm optionsDialog;
        private AboutForm aboutDialog;

        private Label titleLabel, subtitleLabel, statusLabel, eventsLabel;
        private Label recordKeyLabel, playKeyLabel, recordKeyValue, playKeyValue, keyStatusLabel, folderLabel;
        private Label autoClickerTitle, autoClickerHint;
        private RoundedButton recordButton, playButton, themeButton, folderButton;
        private RoundedButton setRecordKeyButton, setPlayKeyButton, refreshButton, deleteSelectedButton, deleteAllButton, saveButton;
        private RoundedButton macroNavButton, clickerNavButton, menuButton;
        private ModernTextBox nameBox, folderBox;
        private MacroSelector macroCombo;
        private AnimatedToggle infiniteCheck;
        private StepperInput minutesInput, secondsInput;
        private Panel headerPanel, autoClickerPage;
        private BufferedPanel pageHost;
        private ModernScrollViewer macroPage;
        private ModernMenuPopup appMenu;
        private ModernToolTip toolTips;
        private System.Windows.Forms.Timer pageTransitionTimer;
        private readonly Stopwatch pageTransitionClock = new Stopwatch();

        [DllImport("user32.dll")] private static extern IntPtr GetForegroundWindow();
        [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(IntPtr hwnd, out uint processId);
        [DllImport("user32.dll")] private static extern bool SetCursorPos(int x, int y);
        [DllImport("user32.dll")] private static extern void keybd_event(byte vk, byte scan, uint flags, UIntPtr extra);
        [DllImport("user32.dll")] private static extern void mouse_event(uint flags, uint dx, uint dy, uint data, UIntPtr extra);

        public long RecordElapsedMs { get { return recordClock.ElapsedMilliseconds; } }
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
            }
            catch { }
            return s;
        }

        private void SaveSettings()
        {
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(configPath));
                File.WriteAllText(configPath, json.Serialize(settings), Encoding.UTF8);
            }
            catch { }
        }

        private void BuildUi()
        {
            SuspendLayout();
            TableLayoutPanel root = new TableLayoutPanel {
                Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 2,
                Padding = new Padding(22, 14, 22, 16), Tag = "surface"
            };
            root.SuspendLayout();
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 104));
            root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            ContentPanel.Controls.Add(root);

            headerPanel = new Panel { Dock = DockStyle.Fill, Tag = "surface" };
            titleLabel = new Label { Text = "MacroDeck", AutoSize = true, Location = new Point(0, 0), Font = FontManager.Create(22f, FontStyle.Bold), ForeColor = palette.Text };
            subtitleLabel = new Label { Text = Tr("Grave, organize e reproduza ações."), AutoSize = true, Location = new Point(2, 42), Font = FontManager.Create(8.5f, FontStyle.Regular), ForeColor = palette.Muted, Tag = "muted" };
            macroNavButton = MakeButton("Macro", 0, 25, 88, 38, true);
            macroNavButton.Click += delegate { SetActivePage(false); };
            clickerNavButton = MakeButton("Auto-Clicker", 0, 25, 128, 38, false);
            clickerNavButton.Click += delegate { SetActivePage(true); };
            themeButton = MakeButton(Tr("Claro"), 0, 25, 94, 38, false);
            themeButton.Icon = "sun";
            themeButton.Click += delegate
            {
                settings.DarkTheme = !settings.DarkTheme;
                palette = ThemePalette.From(settings);
                SaveSettings();
                ApplyTheme();
            };
            menuButton = MakeButton("Menu", 0, 25, 100, 38, false);
            menuButton.Icon = "chevron";
            menuButton.Click += delegate { ShowAppMenu(); };
            headerPanel.Controls.Add(titleLabel); headerPanel.Controls.Add(subtitleLabel);
            headerPanel.Controls.Add(macroNavButton); headerPanel.Controls.Add(clickerNavButton);
            headerPanel.Controls.Add(themeButton); headerPanel.Controls.Add(menuButton);
            headerPanel.Resize += PositionHeaderActions;
            root.Controls.Add(headerPanel, 0, 0);

            pageHost = new BufferedPanel { Dock = DockStyle.Fill, Tag = "surface" };
            pageHost.SuspendLayout();
            root.Controls.Add(pageHost, 0, 1);
            BuildMacroPage();
            BuildAutoClickerPage();
            pageHost.ResumeLayout(true);

            CreateAppMenu();
            toolTips = new ModernToolTip();
            toolTips.SetToolTip(deleteSelectedButton, Tr("Apaga somente o macro selecionado. O aplicativo pedirá confirmação."), this);
            toolTips.SetToolTip(deleteAllButton, Tr("Apaga todos os macros válidos desta pasta. O aplicativo pedirá confirmação."), this);
            toolTips.SetToolTip(saveButton, Tr("Salva a gravação atual como um novo macro."), this);
            toolTips.SetToolTip(refreshButton, Tr("Atualiza a lista de macros salvos."), this);
            PositionHeaderActions(null, EventArgs.Empty);
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
            macroNavButton.Left = clickerNavButton.Left - 6 - macroNavButton.Width;
        }

        private void BuildMacroPage()
        {
            macroPage = new ModernScrollViewer { Dock = DockStyle.Fill, Tag = "surface" };
            pageHost.Controls.Add(macroPage);
            TableLayoutPanel grid = new TableLayoutPanel {
                Dock = DockStyle.Top, ColumnCount = 2, RowCount = 3, AutoSize = true,
                GrowStyle = TableLayoutPanelGrowStyle.AddRows, Tag = "surface", Padding = new Padding(0, 0, 0, 5)
            };
            grid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
            grid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
            grid.RowStyles.Add(new RowStyle(SizeType.Absolute, 158));
            grid.RowStyles.Add(new RowStyle(SizeType.Absolute, 258));
            grid.RowStyles.Add(new RowStyle(SizeType.Absolute, 174));
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
                SaveSelectedMacro();
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
            };
            eventsLabel = new Label { Text = Tr("Nenhum macro salvo"), AutoSize = false, Location = new Point(20, 166), Size = new Size(410, 22), Font = FontManager.Create(8.2f, FontStyle.Regular), Tag = "muted" };
            macroCard.Controls.Add(eventsLabel);
            saveButton = MakeButton(Tr("Salvar gravação"), 19, 197, 148, 36, true);
            saveButton.Enabled = false;
            saveButton.Click += delegate { SaveRecording(); };
            macroCard.Controls.Add(saveButton);
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
            infiniteCheck.CheckedChanged += delegate { settings.CycleInfinite = infiniteCheck.Checked; settings.Repeat = infiniteCheck.Checked; SaveSettings(); };
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
            minutesInput.ValueChanged += delegate { SaveIntervalSettings(); };
            secondsInput.ValueChanged += delegate { SaveIntervalSettings(); };
            grid.Controls.Add(intervalCard, 1, 2);
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

        private void SetActivePage(bool clicker)
        {
            if (clicker == activeClickerPage && pageTransitionTimer != null && pageTransitionTimer.Enabled == false) return;
            if (pageTransitionTimer == null)
            {
                pageTransitionTimer = new System.Windows.Forms.Timer { Interval = 15 };
                pageTransitionTimer.Tick += AnimatePageTransition;
            }
            if (pageTransitionTimer.Enabled)
            {
                pageTransitionTimer.Stop();
                UiAnimationClock.End();
                bool currentlyClicker = pageTransitionTarget;
                macroPage.Dock = DockStyle.Fill; autoClickerPage.Dock = DockStyle.Fill;
                macroPage.Visible = !currentlyClicker; autoClickerPage.Visible = currentlyClicker;
                activeClickerPage = currentlyClicker;
            }
            if (clicker == activeClickerPage) return;
            pageTransitionTarget = clicker;
            pageTransitionClock.Restart();
            pageTransitionFrom = activeClickerPage;
            macroPage.Dock = DockStyle.None;
            autoClickerPage.Dock = DockStyle.None;
            Rectangle area = pageHost.ClientRectangle;
            Control entering = clicker ? (Control)autoClickerPage : (Control)macroPage;
            Control leaving = clicker ? (Control)macroPage : (Control)autoClickerPage;
            entering.SetBounds(clicker ? area.Width : -area.Width, 0, area.Width, area.Height);
            leaving.SetBounds(0, 0, area.Width, area.Height);
            entering.Visible = true;
            leaving.Visible = true;
            entering.BringToFront();
            activeClickerPage = clicker;
            macroNavButton.Tag = clicker ? "secondary" : "selected";
            clickerNavButton.Tag = clicker ? "selected" : "secondary";
            ApplyControlTheme(headerPanel);
            UiAnimationClock.Begin();
            pageTransitionTimer.Start();
        }

        private bool pageTransitionFrom;

        private void AnimatePageTransition(object sender, EventArgs e)
        {
            float progress = Math.Min(1f, (float)(pageTransitionClock.Elapsed.TotalMilliseconds / 180.0));
            float eased = progress * progress * (3f - 2f * progress);
            int width = pageHost.ClientSize.Width;
            Control entering = pageTransitionTarget ? (Control)autoClickerPage : (Control)macroPage;
            Control leaving = pageTransitionFrom ? (Control)autoClickerPage : (Control)macroPage;
            int distance = (int)(width * eased);
            entering.Left = pageTransitionTarget ? width - distance : -width + distance;
            leaving.Left = pageTransitionTarget ? -distance : distance;
            if (progress < 1f) return;
            pageTransitionTimer.Stop();
            UiAnimationClock.End();
            pageTransitionClock.Stop();
            activeClickerPage = pageTransitionTarget;
            macroPage.Visible = !activeClickerPage;
            autoClickerPage.Visible = activeClickerPage;
            macroPage.Dock = DockStyle.Fill; autoClickerPage.Dock = DockStyle.Fill;
            macroPage.Location = Point.Empty; autoClickerPage.Location = Point.Empty;
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
            base.OnResize(e);
            if (WindowState == FormWindowState.Minimized && appMenu != null && !appMenu.IsDisposed) appMenu.Hide();
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
            bool showClickerPage = activeClickerPage;
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
            activeClickerPage = showClickerPage;
            BuildUi();
            macroPage.Visible = !showClickerPage;
            autoClickerPage.Visible = showClickerPage;
            macroPage.Dock = DockStyle.Fill;
            autoClickerPage.Dock = DockStyle.Fill;
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
            SaveSettings();
        }

        private string Tr(string portugueseText)
        {
            return Localization.Translate(uiLanguage, portugueseText);
        }

        private RoundedPanel CreateCard()
        {
            return new RoundedPanel { BackColor = palette.Card, BorderColor = palette.Border, Margin = new Padding(0, 8, 12, 8), CornerRadius = 15, Tag = "card" };
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
            clickerNavButton.Tag = activeClickerPage ? "selected" : "secondary";
            macroNavButton.Tag = activeClickerPage ? "secondary" : "selected";
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
                ApplyControlTheme(c);
            }
        }

        protected override void OnShown(EventArgs e)
        {
            base.OnShown(e);
            FitToWorkingArea();
            BeginInvoke((MethodInvoker)delegate
            {
                LoadMacros();
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
            base.OnLocationChanged(e);
            if (!IsHandleCreated || IsDisposed) return;
            Screen screen = Screen.FromControl(this);
            if (String.Equals(screen.DeviceName, currentMonitorDevice, StringComparison.OrdinalIgnoreCase)) return;
            currentMonitorDevice = screen.DeviceName;
            FitToWorkingArea();
        }

        protected override void WndProc(ref Message message)
        {
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
            if (recordHotkey != null && recordHotkey.Key == vk && recordHotkey.Ctrl == ctrl && recordHotkey.Alt == alt && recordHotkey.Shift == shift && recordHotkey.Win == win)
            {
                PostToUi((MethodInvoker)delegate { ToggleRecording(); });
                return true;
            }
            if (playHotkey != null && playHotkey.Key == vk && playHotkey.Ctrl == ctrl && playHotkey.Alt == alt && playHotkey.Shift == shift && playHotkey.Win == win)
            {
                PostToUi((MethodInvoker)delegate { TogglePlayback(); });
                return true;
            }
            return false;
        }

        private void ToggleRecording()
        {
            if (recording) { StopRecording(); return; }
            if (playing) { UpdateRuntimeUi("Pare a reprodução antes de gravar", "", true); return; }
            pendingEvents = null;
            saveButton.Enabled = false;
            lock (eventsLock) recordedEvents.Clear();
            recordLimitQueued = false;
            if (hooks != null) hooks.ResetMouseTracking();
            recordClock.Reset(); recordClock.Start();
            recording = true;
            eventsLabel.Text = "";
            UiSounds.RecordingStart();
            UpdateRuntimeUi("Gravando", "", true);
        }

        private void StopRecording()
        {
            if (!recording) return;
            recording = false;
            UiSounds.RecordingStop();
            recordClock.Stop();
            List<MacroEvent> copy;
            lock (eventsLock)
            {
                copy = new List<MacroEvent>(recordedEvents);
                BalanceInputs(copy, recordClock.ElapsedMilliseconds);
            }
            pendingEvents = copy.Count == 0 ? null : copy;
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
            if (pendingEvents == null || pendingEvents.Count == 0) return false;
            try
            {
                Directory.CreateDirectory(settings.Folder);
                string baseName = SafeFileName(String.IsNullOrWhiteSpace(nameBox.Text) ? Tr("Nova gravação") : nameBox.Text);
                string name = baseName;
                string path = Path.Combine(settings.Folder, name + ".json");
                int suffix = 2;
                while (File.Exists(path))
                {
                    name = baseName + " " + suffix.ToString();
                    path = Path.Combine(settings.Folder, name + ".json");
                    suffix++;
                }
                MacroFile macro = new MacroFile { Version = 1, Name = name, CreatedAt = DateTime.Now, Events = new List<MacroEvent>(pendingEvents) };
                File.WriteAllText(path, json.Serialize(macro), Encoding.UTF8);
                pendingEvents = null;
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

        private static void BalanceInputs(List<MacroEvent> events, long stopTimeMs)
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
            foreach (int key in heldKeys) events.Add(new MacroEvent { Kind = "keyUp", Code = key, TimeMs = stopTimeMs });
            foreach (KeyValuePair<int, MacroEvent> entry in heldButtons)
                events.Add(new MacroEvent { Kind = "mouseUp", Code = entry.Key, X = entry.Value.X, Y = entry.Value.Y, TimeMs = stopTimeMs });
        }

        public void RecordKey(string kind, int code) { if (recording) AddEvent(new MacroEvent { Kind = kind, Code = code }); }
        public void RecordMouseMove(int x, int y) { if (recording) AddEvent(new MacroEvent { Kind = "move", X = x, Y = y }); }
        public void RecordMouseButton(string kind, int button, int x, int y) { if (recording) AddEvent(new MacroEvent { Kind = kind, Code = button, X = x, Y = y }); }
        public void RecordWheel(int x, int y, int delta) { if (recording) AddEvent(new MacroEvent { Kind = "wheel", X = x, Y = y, Wheel = delta }); }

        private void AddEvent(MacroEvent e)
        {
            e.TimeMs = recordClock.ElapsedMilliseconds;
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
            if (recording) { UpdateRuntimeUi("Pare a gravação antes de reproduzir", "", true); return; }
            if (playing) { StopPlayback(); return; }
            if (pendingEvents != null && pendingEvents.Count > 0)
            {
                MacroFile preview = new MacroFile {
                    Version = 1,
                    Name = String.IsNullOrWhiteSpace(nameBox.Text) ? Tr("Prévia") : nameBox.Text.Trim(),
                    CreatedAt = DateTime.Now,
                    Events = new List<MacroEvent>(pendingEvents)
                };
                StartPlayback(preview);
                return;
            }
            MacroChoice choice = macroCombo.SelectedItem as MacroChoice;
            if (choice == null) return;
            MacroFile macro = ReadMacro(choice.Path);
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
            playing = true;
            playbackGeneration++;
            int generation = playbackGeneration;
            CancellationTokenSource source = new CancellationTokenSource();
            playbackCancel = source;
            double intervalSeconds = (double)minutesInput.Value * 60.0 + (double)secondsInput.Value;
            bool repeat = infiniteCheck.Checked;
            UiSounds.PlaybackStart();
            UpdateRuntimeUi("Reproduzindo", "", true);

            ThreadPool.QueueUserWorkItem(delegate
            {
                string error = null;
                try
                {
                    do
                    {
                        Stopwatch cycleClock = Stopwatch.StartNew();
                        for (int i = 0; i < macro.Events.Count; i++)
                        {
                            if (source.IsCancellationRequested) break;
                            MacroEvent item = macro.Events[i];
                            if (item == null || item.Kind == "limit") continue;
                            WaitUntil(cycleClock, item.TimeMs, source.Token);
                            if (source.IsCancellationRequested) break;
                            PlayEvent(item);
                        }
                        if (!repeat || source.IsCancellationRequested) break;
                        TimeSpan repeatPause = intervalSeconds > 0 ? TimeSpan.FromSeconds(intervalSeconds) : TimeSpan.FromMilliseconds(25);
                        if (WaitCancelable(repeatPause, source.Token)) break;
                    } while (!source.IsCancellationRequested);
                }
                catch (Exception ex) { error = ex.Message; }
                finally
                {
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
                        })) source.Dispose();
                    }
                }
            });
        }

        private static bool WaitCancelable(TimeSpan duration, CancellationToken token)
        {
            Stopwatch sw = Stopwatch.StartNew();
            while (sw.Elapsed < duration)
            {
                if (token.IsCancellationRequested) return true;
                int left = (int)Math.Min(100, Math.Max(1, (duration - sw.Elapsed).TotalMilliseconds));
                Thread.Sleep(left);
            }
            return token.IsCancellationRequested;
        }

        private static void WaitUntil(Stopwatch clock, long targetMs, CancellationToken token)
        {
            while (clock.ElapsedMilliseconds < targetMs)
            {
                if (token.IsCancellationRequested) return;
                long left = targetMs - clock.ElapsedMilliseconds;
                Thread.Sleep((int)Math.Min(15, Math.Max(1, left)));
            }
        }

        private void PlayEvent(MacroEvent e)
        {
            if (e.Kind == "keyDown" || e.Kind == "keyUp")
                keybd_event((byte)e.Code, 0, e.Kind == "keyUp" ? 0x0002u : 0u, UIntPtr.Zero);
            else if (e.Kind == "move")
                SetCursorPos(e.X, e.Y);
            else if (e.Kind == "wheel")
            {
                SetCursorPos(e.X, e.Y);
                mouse_event(0x0800, 0, 0, unchecked((uint)e.Wheel), UIntPtr.Zero);
            }
            else if (e.Kind == "mouseDown" || e.Kind == "mouseUp")
            {
                SetCursorPos(e.X, e.Y);
                int button = Math.Max(0, Math.Min(MouseDownFlags.Length - 1, e.Code));
                mouse_event(e.Kind == "mouseDown" ? MouseDownFlags[button] : MouseUpFlags[button], 0, 0, MouseData[button], UIntPtr.Zero);
            }
        }

        private void StopPlayback()
        {
            if (!playing) return;
            playing = false;
            playbackGeneration++;
            if (playbackCancel != null) { playbackCancel.Cancel(); playbackCancel = null; }
            UiSounds.PlaybackStop();
            UpdateRuntimeUi("Reprodução parada", "", false);
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
            playButton.Text = Tr(playing ? "Parar reprodução" : (pendingEvents != null && pendingEvents.Count > 0 ? "Testar gravação" : "Reproduzir"));
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
            string wanted = selectPath ?? settings.SelectedFile;
            int generation = Interlocked.Increment(ref macroLoadGeneration);
            ThreadPool.QueueUserWorkItem(delegate
            {
                List<MacroChoice> choices = new List<MacroChoice>();
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
                        string name = CleanMacroName(String.IsNullOrWhiteSpace(macro.Name) ? Path.GetFileNameWithoutExtension(files[i]) : macro.Name, Localization.IsEnglish(uiLanguage));
                        int duplicateCount;
                        if (displayNameCounts.TryGetValue(name, out duplicateCount))
                        {
                            duplicateCount++;
                            displayNameCounts[name] = duplicateCount;
                            name += " (" + duplicateCount.ToString() + ")";
                        }
                        else displayNameCounts.Add(name, 1);
                        choices.Add(new MacroChoice { Path = files[i], Name = name });
                    }
                }
                catch (Exception ex) { failure = ex; }

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
                        settings.SelectedFile = "";
                        SaveSettings();
                        if (pendingEvents == null) eventsLabel.Text = "";
                    }
                    else
                    {
                        settings.SelectedFile = selected.Path;
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

        private void SaveSelectedMacro()
        {
            MacroChoice choice = macroCombo.SelectedItem as MacroChoice;
            if (choice != null) { settings.SelectedFile = choice.Path; SaveSettings(); }
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
            eventsLabel.Text = "";
        }

        private void UpdateMacroActions()
        {
            if (deleteSelectedButton == null) return;
            deleteSelectedButton.Enabled = !recording && !playing && macroCombo.SelectedItem != null;
            deleteAllButton.Enabled = !recording && !playing && macroCombo.Items.Count > 0;
            if (playButton != null)
                playButton.Enabled = playing || (!recording && ((pendingEvents != null && pendingEvents.Count > 0) || macroCombo.SelectedItem != null));
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

        private void OnClosing(object sender, FormClosingEventArgs e)
        {
            if (recording) StopRecording();
            pendingEvents = null;
            if (playbackCancel != null) playbackCancel.Cancel();
            SaveSettings();
            if (toolTips != null) toolTips.Dispose();
            if (hooks != null) hooks.Dispose();
            if (pageTransitionTimer != null) { if (pageTransitionTimer.Enabled) UiAnimationClock.End(); pageTransitionTimer.Stop(); pageTransitionTimer.Dispose(); pageTransitionTimer = null; }
            if (optionsDialog != null) { optionsDialog.Dispose(); optionsDialog = null; }
            if (aboutDialog != null) { aboutDialog.Dispose(); aboutDialog = null; }
            if (appMenu != null) { appMenu.Dispose(); appMenu = null; }
        }
    }
}
