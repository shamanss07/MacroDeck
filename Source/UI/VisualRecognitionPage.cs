using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace MacroDeck
{
    internal sealed class VisualRecognitionPage : Panel
    {
        private readonly Func<string, string> translate;
        private string macroFolder;
        private readonly string language;
        private readonly List<VisualWorkflowStep> steps = new List<VisualWorkflowStep>();
        private ThemePalette palette;
        private ModernScrollViewer scrollViewer;
        private ModernTextBox workflowNameBox, targetBox, timeoutBox, delayBox;
        private MacroSelector savedWorkflows, targetKindBox, defaultMonitorBox;
        private ListBox stepList;
        private RoundedPanel workflowCard, stepsCard, editorCard, executionCard;
        private RoundedButton saveWorkflowButton, newWorkflowButton, loadWorkflowButton, refreshWorkflowButton;
        private RoundedButton addTextButton, addImageButton, removeStepButton, moveUpButton, moveDownButton;
        private RoundedButton browseImageButton, pasteImageButton, captureTargetButton, selectRegionButton, fullMonitorButton;
        private RoundedButton testStepButton, runWorkflowButton, stopButton;
        private Label regionSummary, executionStatus, emptyWorkflowLabel, delayCaption, timeoutCaption;
        private Label targetCaption, clipboardHint, timeoutHint, delayHint, defaultRegionCaption, defaultRegionHint;
        private int selectedStep = -1;
        private bool populating;
        private bool isBusy;
        private bool isSelecting;
        private CancellationTokenSource runCancellation;
        public bool IsRunning { get { return isBusy; } }
        public bool IsSelecting { get { return isSelecting; } }
        public void ToggleExecution()
        {
            if(isBusy) { StopExecution(); return; }
            if(isSelecting)return;
            // After reopening the app, the saved selector can have an item while
            // the new draft is empty. Let the playback shortcut use that selection.
            if(steps.Count==0 && savedWorkflows.SelectedItem!=null)LoadWorkflow();
            StartWorkflow();
        }
        public Func<bool> CanStart { get; set; }
        public Action<string> DefaultMonitorChanged { get; set; }

        public string DefaultMonitorDevice
        {
            get { return defaultMonitorBox.SelectedItem == null ? "" : ((MacroChoice)defaultMonitorBox.SelectedItem).Path; }
            set
            {
                int index = defaultMonitorBox.Items.FindIndex(delegate(MacroChoice item) { return String.Equals(item.Path, value, StringComparison.OrdinalIgnoreCase); });
                if (index < 0) index = defaultMonitorBox.Items.FindIndex(delegate(MacroChoice item) { return item.Path == Screen.FromControl(this).DeviceName; });
                defaultMonitorBox.SelectedIndex = index < 0 ? 0 : index;
            }
        }

        private Rectangle DefaultSearchRegion()
        {
            foreach (Screen screen in Screen.AllScreens)
                if (screen.DeviceName == DefaultMonitorDevice) return screen.Bounds;
            return Screen.FromControl(this).Bounds;
        }

        private static void SetRegion(VisualWorkflowStep step, Rectangle bounds)
        {
            step.HasRegion = true;
            step.RegionX = bounds.X; step.RegionY = bounds.Y;
            step.RegionWidth = bounds.Width; step.RegionHeight = bounds.Height;
        }

        public VisualWorkflow ExportDraft()
        {
            CommitEditorChanges();
            VisualWorkflow draft = new VisualWorkflow { Name = workflowNameBox.Text };
            foreach (VisualWorkflowStep step in steps) draft.Steps.Add(step.Copy());
            return draft;
        }

        public void RestoreDraft(VisualWorkflow draft)
        {
            if (draft == null) return;
            steps.Clear();
            foreach (VisualWorkflowStep step in draft.Steps) steps.Add(step.Copy());
            workflowNameBox.Text = draft.Name;
            RefreshStepList(steps.Count > 0 ? 0 : -1);
        }

        public void SetMacroFolder(string folder)
        {
            macroFolder = folder;
            RefreshWorkflowNames();
        }

        public VisualRecognitionPage(string macroFolder, string language, Func<string, string> translate, ThemePalette palette)
        {
            this.macroFolder = macroFolder;
            this.language = language;
            this.translate = translate;
            this.palette = palette;
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
            Dock = DockStyle.Fill;
            Visible = false;
            Tag = "surface";
            BuildUi();
            RefreshWorkflowNames();
            ApplyTheme(palette);
        }

        private string Tr(string text) { return translate == null ? text : translate(text); }

        private void BuildUi()
        {
            scrollViewer = new ModernScrollViewer { Dock = DockStyle.Fill, Tag = "surface" };
            Controls.Add(scrollViewer);
            BufferedTableLayoutPanel grid = new BufferedTableLayoutPanel {
                Dock = DockStyle.Top, ColumnCount = 2, RowCount = 3, AutoSize = true,
                GrowStyle = TableLayoutPanelGrowStyle.AddRows, Padding = new Padding(0, 0, 0, 6), Tag = "surface"
            };
            grid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
            grid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
            grid.RowStyles.Add(new RowStyle(SizeType.Absolute, 138));
            grid.RowStyles.Add(new RowStyle(SizeType.Absolute, 510));
            grid.RowStyles.Add(new RowStyle(SizeType.Absolute, 154));
            scrollViewer.Content = grid;

            workflowCard = CreateCard();
            workflowCard.Dock = DockStyle.Fill;
            workflowCard.Resize += LayoutWorkflowCard;
            AddSectionTitle(workflowCard, Tr("LEITOR DE TELA"), 19, 13);
            workflowCard.Controls.Add(new Label {
                Text = Tr("Adicione etapas. Em cada uma, o leitor procura um texto ou imagem e clica nele."),
                Location = new Point(20, 38), AutoSize = false, Height = 20, Tag = "muted"
            });
            workflowNameBox = new ModernTextBox { Location = new Point(19, 70), Width = 240, Height = 31, Font = FontManager.Create(9f, FontStyle.Regular) };
            workflowNameBox.Text = Tr("Novo fluxo");
            workflowCard.Controls.Add(workflowNameBox);
            saveWorkflowButton = MakeButton(Tr("Salvar fluxo"), 0, 68, 116, 35, true);
            saveWorkflowButton.Click += delegate { SaveWorkflow(); };
            workflowCard.Controls.Add(saveWorkflowButton);
            newWorkflowButton = MakeButton(Tr("Novo"), 0, 68, 82, 35, false);
            newWorkflowButton.Click += delegate { NewWorkflow(); };
            workflowCard.Controls.Add(newWorkflowButton);
            savedWorkflows = MakeComboBox();
            savedWorkflows.EmptyText = "Nenhum fluxo salvo";
            savedWorkflows.SelectionHint = "Selecione um fluxo";
            savedWorkflows.AccessibleName = Tr("Fluxos salvos");
            workflowCard.Controls.Add(savedWorkflows);
            loadWorkflowButton = MakeButton(Tr("Abrir"), 0, 68, 78, 35, false);
            loadWorkflowButton.Click += delegate { LoadWorkflow(); };
            workflowCard.Controls.Add(loadWorkflowButton);
            refreshWorkflowButton = MakeButton("", 0, 68, 34, 35, false);
            refreshWorkflowButton.Icon = "refresh";
            refreshWorkflowButton.AccessibleName = Tr("Atualizar fluxos salvos");
            refreshWorkflowButton.Click += delegate { RefreshWorkflowNames(); };
            workflowCard.Controls.Add(refreshWorkflowButton);
            grid.Controls.Add(workflowCard, 0, 0);
            grid.SetColumnSpan(workflowCard, 2);

            stepsCard = CreateCard();
            stepsCard.Dock = DockStyle.Fill;
            AddSectionTitle(stepsCard, Tr("ETAPAS DO FLUXO"), 19, 14);
            emptyWorkflowLabel = new Label { Text = Tr("Adicione uma etapa de texto ou imagem para começar."), Location = new Point(24, 56), AutoSize = false, Width = 300, Height = 48, Tag = "muted" };
            stepList = new ListBox {
                Location = new Point(19, 43), Size = new Size(360, 226), DrawMode = DrawMode.OwnerDrawFixed,
                ItemHeight = 34, BorderStyle = BorderStyle.None, IntegralHeight = false,
                Font = FontManager.Create(8.5f, FontStyle.Regular), SelectionMode = SelectionMode.One,
                HorizontalScrollbar = false
            };
            stepList.DrawItem += DrawStepItem;
            stepList.SelectedIndexChanged += delegate { if (!populating) SelectStep(stepList.SelectedIndex); };
            stepsCard.Controls.Add(stepList);
            stepsCard.Controls.Add(emptyWorkflowLabel);
            addTextButton = MakeButton(Tr("+ Texto"), 19, 280, 130, 35, false);
            addTextButton.Click += delegate { AddStep("Text"); };
            addImageButton = MakeButton(Tr("+ Imagem"), 0, 280, 130, 35, false);
            addImageButton.Click += delegate { AddStep("Image"); };
            removeStepButton = MakeButton(Tr("Remover"), 0, 322, 105, 34, false);
            removeStepButton.Click += delegate { RemoveSelectedStep(); };
            moveUpButton = MakeButton("↑", 0, 322, 42, 34, false);
            moveUpButton.AccessibleName = Tr("Mover etapa para cima");
            moveUpButton.Click += delegate { MoveSelectedStep(-1); };
            moveDownButton = MakeButton("↓", 0, 322, 42, 34, false);
            moveDownButton.AccessibleName = Tr("Mover etapa para baixo");
            moveDownButton.Click += delegate { MoveSelectedStep(1); };
            stepsCard.Controls.Add(addTextButton); stepsCard.Controls.Add(addImageButton);
            stepsCard.Controls.Add(removeStepButton); stepsCard.Controls.Add(moveUpButton); stepsCard.Controls.Add(moveDownButton);
            defaultRegionCaption = MakeCaption(Tr("Área padrão das novas etapas"), 20, 338);
            stepsCard.Controls.Add(defaultRegionCaption);
            defaultMonitorBox = MakeComboBox();
            defaultMonitorBox.AccessibleName = Tr("Selecionar área de busca padrão");
            Screen[] monitors = Screen.AllScreens;
            for (int index = 0; index < monitors.Length; index++)
                defaultMonitorBox.Items.Add(new MacroChoice {
                    Name = String.Format(Tr("Monitor {0} inteiro · {1} × {2}"), index + 1, monitors[index].Bounds.Width, monitors[index].Bounds.Height),
                    Path = monitors[index].DeviceName
                });
            stepsCard.Controls.Add(defaultMonitorBox);
            defaultMonitorBox.SelectedIndexChanged += delegate {
                Action<string> changed = DefaultMonitorChanged;
                if (changed != null) changed(DefaultMonitorDevice);
            };
            DefaultMonitorDevice = "";
            defaultRegionHint = new Label {
                Text = Tr("Novas etapas já procuram no monitor inteiro. Em cada etapa, você pode escolher uma área menor."),
                Location = new Point(20, 405), AutoSize = false, Height = 64, Tag = "muted"
            };
            stepsCard.Controls.Add(defaultRegionHint);
            stepsCard.Resize += LayoutStepsCard;
            grid.Controls.Add(stepsCard, 0, 1);

            editorCard = CreateCard();
            editorCard.Dock = DockStyle.Fill;
            AddSectionTitle(editorCard, Tr("ETAPA DO FLUXO"), 19, 14);
            editorCard.Controls.Add(MakeCaption(Tr("Tipo de alvo"), 20, 48));
            targetKindBox = MakeComboBox();
            targetKindBox.Items.Add(new MacroChoice { Name = Tr("Texto na tela"), Path = "Text" });
            targetKindBox.Items.Add(new MacroChoice { Name = Tr("Imagem de referência"), Path = "Image" });
            targetKindBox.AccessibleName = Tr("Tipo de alvo");
            targetKindBox.SelectedIndex = 0;
            targetKindBox.Location = new Point(19, 68);
            targetKindBox.Height = 32;
            targetKindBox.SelectedIndexChanged += delegate { if (!populating) ChangeTargetKind(); };
            editorCard.Controls.Add(targetKindBox);
            targetCaption = MakeCaption(Tr("Texto que o leitor deve encontrar"), 20, 108);
            editorCard.Controls.Add(targetCaption);
            targetBox = new ModernTextBox { Location = new Point(19, 130), Width = 300, Height = 31, Font = FontManager.Create(8.7f, FontStyle.Regular) };
            editorCard.Controls.Add(targetBox);
            targetBox.TextChanged += delegate { if (!populating) CommitEditorChanges(); };
            browseImageButton = MakeButton(Tr("Escolher arquivo"), 19, 168, 135, 32, false);
            browseImageButton.Click += delegate { ChooseTargetImage(); };
            pasteImageButton = MakeButton(Tr("Colar imagem"), 162, 168, 132, 32, false);
            pasteImageButton.Click += delegate { PasteReferenceImage(); };
            captureTargetButton = MakeButton(Tr("Recortar da tela"), 162, 168, 132, 32, false);
            captureTargetButton.Click += delegate { CaptureTargetImage(); };
            editorCard.Controls.Add(browseImageButton); editorCard.Controls.Add(pasteImageButton); editorCard.Controls.Add(captureTargetButton);
            clipboardHint = new Label { Text = Tr("Ou copie uma imagem e pressione Ctrl+V nesta aba."), AutoSize = false, Height = 36, Tag = "muted" };
            editorCard.Controls.Add(clipboardHint);
            selectRegionButton = MakeButton(Tr("Escolher área"), 19, 212, 150, 34, false);
            selectRegionButton.Click += delegate { SelectSearchRegion(); };
            editorCard.Controls.Add(selectRegionButton);
            fullMonitorButton = MakeButton(Tr("Monitor inteiro"), 0, 212, 150, 34, false);
            fullMonitorButton.Click += delegate {
                VisualWorkflowStep step = GetSelectedStep();
                if (step == null || isBusy) return;
                SetRegion(step, DefaultSearchRegion());
                PopulateEditor(); stepList.Invalidate();
            };
            editorCard.Controls.Add(fullMonitorButton);
            regionSummary = new Label { Text = Tr("Nenhuma área selecionada"), Location = new Point(20, 252), AutoSize = false, Height = 32, Width = 320, Tag = "muted" };
            editorCard.Controls.Add(regionSummary);
            timeoutCaption = MakeCaption(Tr("Procurar por até (s)"), 20, 285);
            editorCard.Controls.Add(timeoutCaption);
            timeoutBox = new ModernTextBox { Text = "15", Location = new Point(19, 307), Width = 105, Height = 31, Font = FontManager.Create(8.7f, FontStyle.Regular) };
            editorCard.Controls.Add(timeoutBox);
            delayCaption = MakeCaption(Tr("Pausa após clicar (ms)"), 146, 285);
            editorCard.Controls.Add(delayCaption);
            delayBox = new ModernTextBox { Text = "250", Location = new Point(145, 307), Width = 118, Height = 31, Font = FontManager.Create(8.7f, FontStyle.Regular) };
            editorCard.Controls.Add(delayBox);
            timeoutBox.TextChanged += delegate { if (!populating) CommitEditorChanges(); };
            delayBox.TextChanged += delegate { if (!populating) CommitEditorChanges(); };
            timeoutHint = new Label { Text = Tr("Encontrou antes? Continua. Se o alvo não aparecer nesse tempo, o fluxo para."), AutoSize = false, Height = 72, Tag = "muted" };
            delayHint = new Label { Text = Tr("Espera depois do clique, antes da próxima etapa. 1000 ms = 1 segundo."), AutoSize = false, Height = 72, Tag = "muted" };
            editorCard.Controls.Add(timeoutHint); editorCard.Controls.Add(delayHint);
            editorCard.Resize += LayoutEditorCard;
            grid.Controls.Add(editorCard, 1, 1);

            executionCard = CreateCard();
            executionCard.Dock = DockStyle.Fill;
            AddSectionTitle(executionCard, Tr("EXECUÇÃO"), 19, 13);
            testStepButton = MakeButton(Tr("Testar etapa"), 19, 47, 132, 36, false);
            testStepButton.Click += delegate { TestSelectedStep(); };
            runWorkflowButton = MakeButton(Tr("Executar fluxo"), 161, 47, 150, 36, true);
            runWorkflowButton.Click += delegate { StartWorkflow(); };
            stopButton = MakeButton(Tr("Parar"), 0, 47, 100, 36, false);
            stopButton.Enabled = false;
            stopButton.Click += delegate { StopExecution(); };
            executionStatus = new Label { Text = Tr("Pronto para executar"), Location = new Point(20, 87), AutoSize = false, Height = 33, Width = 440, Tag = "muted" };
            executionCard.Controls.Add(testStepButton); executionCard.Controls.Add(runWorkflowButton);
            executionCard.Controls.Add(stopButton); executionCard.Controls.Add(executionStatus);
            executionCard.Resize += LayoutExecutionCard;
            grid.Controls.Add(executionCard, 0, 2);
            grid.SetColumnSpan(executionCard, 2);
            LayoutWorkflowCard(null, EventArgs.Empty);
            LayoutStepsCard(null, EventArgs.Empty);
            LayoutEditorCard(null, EventArgs.Empty);
            LayoutExecutionCard(null, EventArgs.Empty);
            PopulateEditor();
            UpdateEmptyState();
        }

        private RoundedPanel CreateCard()
        {
            return new RoundedPanel { BackColor = palette.Card, BorderColor = palette.Border, Margin = new Padding(0, 8, 12, 8), CornerRadius = 15, Tag = "card" };
        }

        private Label MakeCaption(string text, int x, int y)
        {
            return new Label { Text = text, Location = new Point(x, y), AutoSize = true, ForeColor = palette.Text };
        }

        private void AddSectionTitle(Control parent, string text, int x, int y)
        {
            parent.Controls.Add(new Label { Text = text, AutoSize = true, Location = new Point(x, y), Font = FontManager.Create(8f, FontStyle.Bold), ForeColor = palette.Muted, Tag = "muted" });
        }

        private RoundedButton MakeButton(string text, int x, int y, int width, int height, bool primary)
        {
            return new RoundedButton { Text = text, Location = new Point(x, y), Size = new Size(width, height), Font = FontManager.Create(8.5f, FontStyle.Bold), Tag = primary ? "primary" : "secondary", BackColor = primary ? palette.Accent : palette.Input, ForeColor = palette.Text };
        }

        private MacroSelector MakeComboBox()
        {
            return new MacroSelector { Language = language, Font = FontManager.Create(8.5f, FontStyle.Regular) };
        }

        private void LayoutWorkflowCard(object sender, EventArgs e)
        {
            if (workflowCard == null) return;
            int half = Math.Max(270, (workflowCard.ClientSize.Width - 48) / 2);
            int left = 19;
            int right = left + half + 10;
            int saveWidth = Math.Max(112, TextRenderer.MeasureText(saveWorkflowButton.Text, saveWorkflowButton.Font, Size.Empty, TextFormatFlags.NoPadding | TextFormatFlags.SingleLine).Width + 20);
            workflowNameBox.SetBounds(left, 70, Math.Max(80, half - saveWidth - 91), 35);
            saveWorkflowButton.SetBounds(workflowNameBox.Right + 7, 70, saveWidth, 35);
            newWorkflowButton.SetBounds(saveWorkflowButton.Right + 6, 70, 78, 35);
            savedWorkflows.SetBounds(right, 70, Math.Max(120, half - 122), 35);
            loadWorkflowButton.SetBounds(savedWorkflows.Right + 6, 70, 76, 35);
            refreshWorkflowButton.SetBounds(loadWorkflowButton.Right + 6, 70, 34, 35);
            Control description = workflowCard.Controls.Count > 1 ? workflowCard.Controls[1] : null;
            if (description != null) description.Width = Math.Max(250, workflowCard.ClientSize.Width - 40);
        }

        private void LayoutStepsCard(object sender, EventArgs e)
        {
            if (stepsCard == null) return;
            int width = Math.Max(180, stepsCard.ClientSize.Width - 40);
            stepList.SetBounds(19, 43, width, 188);
            emptyWorkflowLabel.SetBounds(24, 55, width - 10, 48);
            int buttonWidth = Math.Max(90, (width - 8) / 2);
            addTextButton.SetBounds(19, 242, buttonWidth, 35);
            addImageButton.SetBounds(addTextButton.Right + 8, 242, buttonWidth, 35);
            removeStepButton.SetBounds(19, 285, Math.Min(125, width - 108), 34);
            moveDownButton.SetBounds(stepsCard.ClientSize.Width - 61, 285, 42, 34);
            moveUpButton.SetBounds(moveDownButton.Left - 48, 285, 42, 34);
            defaultMonitorBox.SetBounds(19, 362, width, 34);
            defaultRegionHint.Width = width;
        }

        private void LayoutEditorCard(object sender, EventArgs e)
        {
            if (editorCard == null) return;
            int width = Math.Max(160, editorCard.ClientSize.Width - 40);
            bool image = GetSelectedStep() != null && GetSelectedStep().Kind == "Image";
            targetKindBox.Width = width;
            targetBox.Width = width;
            browseImageButton.SetBounds(19, 168, Math.Max(100, (width - 8) / 2), 32);
            pasteImageButton.SetBounds(browseImageButton.Right + 8, 168, Math.Max(100, width - browseImageButton.Width - 8), 32);
            captureTargetButton.SetBounds(19, 208, browseImageButton.Width, 32);
            if(image) clipboardHint.SetBounds(pasteImageButton.Left, 208, pasteImageButton.Width, 38);
            else clipboardHint.SetBounds(19,168,width,42);
            int areaTop = image ? 256 : 218;
            selectRegionButton.SetBounds(19, areaTop, browseImageButton.Width, 34);
            fullMonitorButton.SetBounds(pasteImageButton.Left, areaTop, pasteImageButton.Width, 34);
            regionSummary.Top = areaTop + 42;
            regionSummary.Width = width;
            int half = Math.Max(90, (width - 18) / 2);
            timeoutCaption.Top = areaTop + 84;
            delayCaption.Top = timeoutCaption.Top;
            timeoutBox.Top = timeoutCaption.Top + 22;
            timeoutBox.Width = half;
            delayBox.SetBounds(timeoutBox.Right + 18, timeoutBox.Top, half, 31);
            delayCaption.Left = delayBox.Left;
            timeoutHint.SetBounds(20, timeoutBox.Bottom + 8, half, 86);
            delayHint.SetBounds(delayBox.Left, delayBox.Bottom + 8, half, 86);
        }

        private void LayoutExecutionCard(object sender, EventArgs e)
        {
            if (executionCard == null) return;
            int width = Math.Max(220, executionCard.ClientSize.Width - 40);
            testStepButton.SetBounds(19, 47, Math.Max(110, (width - 16) / 3), 36);
            runWorkflowButton.SetBounds(testStepButton.Right + 8, 47, Math.Max(120, (width - 16) / 3), 36);
            stopButton.SetBounds(runWorkflowButton.Right + 8, 47, Math.Max(85, width - testStepButton.Width - runWorkflowButton.Width - 16), 36);
            executionStatus.Width = width;
        }

        private void AddStep(string kind)
        {
            if (isBusy) return;
            if (steps.Count >= 100) { SetStatus(Tr("O limite é de 100 etapas por fluxo.")); return; }
            CommitEditorChanges();
            VisualWorkflowStep step = new VisualWorkflowStep { Kind = kind };
            SetRegion(step, DefaultSearchRegion());
            steps.Add(step);
            RefreshStepList(steps.Count - 1);
        }

        private void RemoveSelectedStep()
        {
            if (isBusy || selectedStep < 0 || selectedStep >= steps.Count) return;
            steps.RemoveAt(selectedStep);
            RefreshStepList(Math.Min(selectedStep, steps.Count - 1));
        }

        private void MoveSelectedStep(int offset)
        {
            if (isBusy || selectedStep < 0 || selectedStep >= steps.Count) return;
            int next = selectedStep + offset;
            if (next < 0 || next >= steps.Count) return;
            VisualWorkflowStep item = steps[selectedStep];
            steps.RemoveAt(selectedStep);
            steps.Insert(next, item);
            RefreshStepList(next);
        }

        private void RefreshStepList(int select)
        {
            populating = true;
            try
            {
                stepList.BeginUpdate();
                stepList.Items.Clear();
                foreach (VisualWorkflowStep step in steps) stepList.Items.Add(step);
                stepList.SelectedIndex = select >= 0 && select < steps.Count ? select : -1;
                selectedStep = stepList.SelectedIndex;
            }
            finally { stepList.EndUpdate(); populating = false; }
            UpdateEmptyState();
            PopulateEditor();
            UpdateEnabledState();
            stepList.Invalidate();
        }

        private void SelectStep(int index)
        {
            CommitEditorChanges();
            selectedStep = index;
            PopulateEditor();
            UpdateEnabledState();
        }

        private void PopulateEditor()
        {
            populating = true;
            try
            {
                VisualWorkflowStep step = GetSelectedStep();
                bool available = step != null;
                targetKindBox.SelectedIndex = !available || !String.Equals(step.Kind, "Image", StringComparison.OrdinalIgnoreCase) ? 0 : 1;
                targetBox.ReadOnly = available && String.Equals(step.Kind, "Image", StringComparison.OrdinalIgnoreCase);
                bool image = available && String.Equals(step.Kind, "Image", StringComparison.OrdinalIgnoreCase);
                targetCaption.Text = Tr(image ? "Imagem que o leitor deve encontrar" : "Texto que o leitor deve encontrar");
                targetBox.Text = !available ? "" : (image ? ReferenceDisplayName(step.ImagePath) : step.Text);
                timeoutBox.Text = available ? Math.Max(1, step.TimeoutSeconds).ToString() : "15";
                delayBox.Text = available ? Math.Max(0, step.DelayAfterMilliseconds).ToString() : "250";
                regionSummary.Text = available && step.HasRegion
                    ? RegionDescription(step)
                    : Tr("Nenhuma área selecionada");
                browseImageButton.Visible = available && String.Equals(step.Kind, "Image", StringComparison.OrdinalIgnoreCase);
                captureTargetButton.Visible = browseImageButton.Visible;
                pasteImageButton.Visible = browseImageButton.Visible;
                clipboardHint.Visible = available;
                clipboardHint.Text = Tr(browseImageButton.Visible ? "Ou copie uma imagem e pressione Ctrl+V nesta aba." : "Digite o texto completo da opção, incluindo números e sinais.");
                targetBox.Enabled = available;
                timeoutBox.Enabled = available;
                delayBox.Enabled = available;
                targetKindBox.Enabled = available;
                selectRegionButton.Enabled = available;
                fullMonitorButton.Enabled = available;
            }
            finally { populating = false; }
            UpdateEmptyState();
            LayoutEditorCard(null, EventArgs.Empty);
        }

        private void ChangeTargetKind()
        {
            VisualWorkflowStep step = GetSelectedStep();
            if (step == null) return;
            step.Kind = targetKindBox.SelectedIndex == 1 ? "Image" : "Text";
            PopulateEditor();
            stepList.Invalidate();
        }

        private void CommitEditorChanges()
        {
            if (populating) return;
            VisualWorkflowStep step = GetSelectedStep();
            if (step == null) return;
            if (targetKindBox.SelectedIndex == 1)
            {
                // Image paths come from import/capture, while the field shows a friendly name.
            }
            else { step.Text = targetBox.Text; step.Kind = "Text"; }
            int value;
            if (Int32.TryParse(timeoutBox.Text, out value)) step.TimeoutSeconds = Math.Max(1, Math.Min(600, value));
            if (Int32.TryParse(delayBox.Text, out value)) step.DelayAfterMilliseconds = Math.Max(0, Math.Min(60000, value));
            stepList.Invalidate();
        }

        private VisualWorkflowStep GetSelectedStep()
        {
            return selectedStep >= 0 && selectedStep < steps.Count ? steps[selectedStep] : null;
        }

        private string ReferenceDisplayName(string path)
        {
            if (String.IsNullOrWhiteSpace(path)) return Tr("Escolha, recorte ou cole uma imagem");
            string name = Path.GetFileName(path);
            if (name.StartsWith("clipboard-", StringComparison.OrdinalIgnoreCase)) return Tr("Imagem colada da área de transferência");
            if (name.StartsWith("capture-", StringComparison.OrdinalIgnoreCase)) return Tr("Imagem recortada da tela");
            if (System.Text.RegularExpressions.Regex.IsMatch(name, "^[a-fA-F0-9]{64}\\.png$")) return Tr("Imagem de referência salva");
            return name;
        }

        private string RegionDescription(VisualWorkflowStep step)
        {
            Rectangle area = new Rectangle(step.RegionX, step.RegionY, step.RegionWidth, step.RegionHeight);
            foreach (Screen monitor in Screen.AllScreens)
                if (monitor.Bounds == area) return String.Format(Tr("Monitor inteiro · {0} × {1}"), area.Width, area.Height);
            return String.Format(Tr("Área desta etapa · {0} × {1}"), area.Width, area.Height);
        }

        protected override bool ProcessCmdKey(ref Message message, Keys keyData)
        {
            VisualWorkflowStep step = GetSelectedStep();
            if (keyData == (Keys.Control | Keys.V) && step != null && step.Kind == "Image" && !isBusy && !isSelecting && PasteReferenceImage(false)) return true;
            return base.ProcessCmdKey(ref message, keyData);
        }

        private void PasteReferenceImage() { PasteReferenceImage(true); }

        private bool PasteReferenceImage(bool showMissing)
        {
            VisualWorkflowStep step = GetSelectedStep();
            if (step == null || step.Kind != "Image" || isBusy || isSelecting) return false;
            try
            {
                using (Bitmap image = ClipboardImageImport.ReadImage(Clipboard.GetDataObject()))
                {
                    if (image == null)
                    {
                        if (showMissing) SetStatus(Tr("Copie uma imagem no Lightshot ou em outro programa e clique em Colar imagem."));
                        return false;
                    }
                    step.ImagePath = ClipboardImageImport.SaveReference(image, "clipboard");
                }
                PopulateEditor();
                stepList.Invalidate();
                SetStatus(Tr("Imagem colada. Use Testar etapa para conferir."));
                return true;
            }
            catch (System.Runtime.InteropServices.ExternalException)
            {
                SetStatus(Tr("A área de transferência está ocupada. Tente colar novamente."));
                return true;
            }
            catch (Exception ex)
            {
                SetStatus(Tr("Não foi possível colar a imagem") + ": " + ex.Message);
                return true;
            }
        }

        private void ChooseTargetImage()
        {
            VisualWorkflowStep step = GetSelectedStep();
            if (step == null) return;
            using (OpenFileDialog dialog = new OpenFileDialog())
            {
                dialog.Title = Tr("Escolha a imagem de referência");
                dialog.Filter = Tr("Imagens") + "|*.png;*.jpg;*.jpeg;*.bmp|PNG|*.png|JPEG|*.jpg;*.jpeg|Bitmap|*.bmp";
                dialog.CheckFileExists = true;
                if (File.Exists(step.ImagePath)) dialog.FileName = step.ImagePath;
                if (dialog.ShowDialog(FindForm()) != DialogResult.OK) return;
                step.Kind = "Image";
                step.ImagePath = dialog.FileName;
                PopulateEditor();
                stepList.Invalidate();
            }
        }

        private async void CaptureTargetImage()
        {
            VisualWorkflowStep step = GetSelectedStep();
            if (step == null) return;
            Rectangle selected = await SelectScreenRectangle(Tr("Arraste para selecionar a imagem que o MacroDeck deve encontrar."));
            if (selected.IsEmpty) return;
            try
            {
                // The selector captured the screen before restoring MacroDeck.
                string path;
                using (Bitmap bitmap = selectedCapture) path = ClipboardImageImport.SaveReference(bitmap, "capture");
                selectedCapture = null;
                step.Kind = "Image";
                step.ImagePath = path;
                PopulateEditor();
                stepList.Invalidate();
            }
            catch (Exception ex) { ShowError(ex.Message); }
        }

        private async void SelectSearchRegion()
        {
            VisualWorkflowStep step = GetSelectedStep();
            if (step == null) return;
            Rectangle selected = await SelectScreenRectangle(Tr("Arraste para definir a área onde esta etapa deve procurar."));
            if (selectedCapture != null) { selectedCapture.Dispose(); selectedCapture = null; }
            if (selected.IsEmpty) return;
            step.HasRegion = true;
            step.RegionX = selected.X;
            step.RegionY = selected.Y;
            step.RegionWidth = selected.Width;
            step.RegionHeight = selected.Height;
            PopulateEditor();
            stepList.Invalidate();
        }

        private Bitmap selectedCapture;
        private async Task<Rectangle> SelectScreenRectangle(string prompt)
        {
            if (isSelecting) return Rectangle.Empty;
            isSelecting = true;
            Form owner = FindForm();
            owner.Hide();
            try
            {
                await Task.Delay(250);
                using (ScreenRegionSelector selector = new ScreenRegionSelector(prompt, language))
                {
                    if (selector.ShowDialog() != DialogResult.OK) return Rectangle.Empty;
                    selectedCapture = selector.CopySelection();
                    return selector.SelectedRegion;
                }
            }
            catch (Exception ex) { ShowError(ex.Message); return Rectangle.Empty; }
            finally { isSelecting = false; if (!owner.IsDisposed) { owner.Show(); owner.Activate(); } }
        }

        private bool ValidateTiming()
        {
            if (GetSelectedStep() == null) return true;
            int timeout, delay;
            if (!Int32.TryParse(timeoutBox.Text, out timeout) || timeout < 1 || timeout > 600 ||
                !Int32.TryParse(delayBox.Text, out delay) || delay < 0 || delay > 60000)
            {
                SetStatus(Tr("Use tempo limite de 1 a 600 segundos e espera de 0 a 60000 ms."));
                return false;
            }
            return true;
        }

        private void TestSelectedStep()
        {
            if (!ValidateTiming()) return;
            CommitEditorChanges();
            VisualWorkflowStep step = GetSelectedStep();
            if (step == null) { SetStatus(Tr("Selecione uma etapa para testar.")); return; }
            StartOperation(new VisualWorkflow { Name = "Test", Steps = new List<VisualWorkflowStep> { step.Copy() } }, false);
        }

        private void StartWorkflow()
        {
            if (!ValidateTiming()) return;
            CommitEditorChanges();
            if (steps.Count == 0) { SetStatus(Tr("Adicione ao menos uma etapa ao fluxo.")); return; }
            VisualWorkflow workflow = new VisualWorkflow { Name = workflowNameBox.Text.Trim() };
            foreach (VisualWorkflowStep step in steps) workflow.Steps.Add(step.Copy());
            StartOperation(workflow, true);
        }

        private void StartOperation(VisualWorkflow workflow, bool click)
        {
            if (isBusy) return;
            if (CanStart != null && !CanStart()) { SetStatus(Tr("Pare a gravação ou reprodução de macros antes de executar o fluxo visual.")); return; }
            CancellationTokenSource cancellation = new CancellationTokenSource();
            runCancellation = cancellation;
            SetBusy(true);
            SetStatus(click ? Tr("Procurando o primeiro alvo…") : Tr("Localizando o alvo sem clicar…"));
            Task.Run(delegate
            {
                try
                {
                    VisualRecognitionService.ValidateWorkflow(workflow, cancellation.Token);
                    for (int second = 3; second > 0; second--)
                    {
                        PostStatus(String.Format(Tr("Iniciando em {0}s. Abra o jogo. Esc para parar."), second));
                        if (cancellation.Token.WaitHandle.WaitOne(1000)) cancellation.Token.ThrowIfCancellationRequested();
                    }
                    PostStatus(Tr("Procurando alvo… Esc para parar."));
                    using (ReaderExecution execution = new ReaderExecution(cancellation, new WindowsReaderDesktop()))
                    {
                        if (click) VisualRecognitionService.RunWorkflow(workflow, cancellation.Token, delegate(string value) { PostStatus(value); }, language, execution);
                        else
                        {
                            VisualRecognitionMatch match = VisualRecognitionService.LocateStep(workflow.Steps[0], cancellation.Token, delegate(string value) { PostStatus(value); }, language, execution);
                            cancellation.Token.ThrowIfCancellationRequested();
                            PostStatus(Tr("Alvo localizado; o cursor foi movido sem clicar") + (Double.IsNaN(match.Confidence) ? " · OCR" : " · " + (int)Math.Round(match.Confidence * 100) + "%"));
                        }
                        if (click) PostStatus(Tr("Fluxo concluído"));
                    }
                }
                catch (OperationCanceledException) { PostStatus(Tr("Execução interrompida")); }
                catch (Exception ex) { PostStatus(Tr("Não foi possível continuar") + ": " + VisualRecognitionService.ErrorMessage(ex, language)); }
                finally { FinishOperation(cancellation); }
            });
        }

        public void StopExecution()
        {
            if (runCancellation == null) return;
            ReaderExecution.Cancel(runCancellation);
            SetStatus(Tr("Parando…"));
        }

        private void FinishOperation(CancellationTokenSource cancellation)
        {
            if (IsDisposed || !IsHandleCreated) { cancellation.Dispose(); return; }
            try
            {
                BeginInvoke((MethodInvoker)delegate
                {
                    if (ReferenceEquals(runCancellation, cancellation))
                    {
                        runCancellation = null;
                        cancellation.Dispose();
                        SetBusy(false);
                    }
                });
            }
            catch (InvalidOperationException) { cancellation.Dispose(); }
        }

        private void PostStatus(string text)
        {
            if (IsDisposed || !IsHandleCreated) return;
            try { BeginInvoke((MethodInvoker)delegate { if (!IsDisposed) SetStatus(text); }); }
            catch (InvalidOperationException) { }
        }

        private void SetStatus(string text)
        {
            executionStatus.Text = text ?? "";
            executionStatus.Invalidate();
        }

        private void SetBusy(bool busy)
        {
            isBusy = busy;
            UpdateEnabledState();
        }

        private void UpdateEnabledState()
        {
            bool hasStep = GetSelectedStep() != null;
            bool hasSteps = steps.Count > 0;
            stepList.Enabled = !isBusy;
            addTextButton.Enabled = !isBusy;
            addImageButton.Enabled = !isBusy;
            removeStepButton.Enabled = !isBusy && hasStep;
            moveUpButton.Enabled = !isBusy && selectedStep > 0;
            moveDownButton.Enabled = !isBusy && selectedStep >= 0 && selectedStep < steps.Count - 1;
            workflowNameBox.Enabled = !isBusy;
            savedWorkflows.Enabled = !isBusy && savedWorkflows.Items.Count > 0;
            defaultMonitorBox.Enabled = !isBusy;
            saveWorkflowButton.Enabled = !isBusy && hasSteps;
            newWorkflowButton.Enabled = !isBusy;
            loadWorkflowButton.Enabled = !isBusy && savedWorkflows.Items.Count > 0;
            refreshWorkflowButton.Enabled = !isBusy;
            testStepButton.Enabled = !isBusy && hasStep;
            runWorkflowButton.Enabled = !isBusy && hasSteps;
            stopButton.Enabled = isBusy;
            targetKindBox.Enabled = !isBusy && hasStep;
            targetBox.Enabled = !isBusy && hasStep;
            timeoutBox.Enabled = !isBusy && hasStep;
            delayBox.Enabled = !isBusy && hasStep;
            selectRegionButton.Enabled = !isBusy && hasStep;
            fullMonitorButton.Enabled = !isBusy && hasStep;
            browseImageButton.Enabled = !isBusy && hasStep && targetKindBox.SelectedIndex == 1;
            captureTargetButton.Enabled = browseImageButton.Enabled;
            pasteImageButton.Enabled = browseImageButton.Enabled;
        }

        private void UpdateEmptyState()
        {
            emptyWorkflowLabel.Visible = steps.Count == 0;
            stepList.Visible = steps.Count > 0;
        }

        private void DrawStepItem(object sender, DrawItemEventArgs e)
        {
            if (e.Index < 0 || e.Index >= steps.Count) return;
            bool selected = (e.State & DrawItemState.Selected) == DrawItemState.Selected;
            using (SolidBrush fill = new SolidBrush(selected ? palette.Accent : palette.Card)) e.Graphics.FillRectangle(fill, e.Bounds);
            Color foreground = selected ? Color.White : palette.Text;
            VisualWorkflowStep step = steps[e.Index];
            string kind = String.Equals(step.Kind, "Image", StringComparison.OrdinalIgnoreCase) ? Tr("Imagem") : Tr("Texto");
            string target = kind + ": " + (kind == Tr("Imagem") ? Path.GetFileName(step.ImagePath) : step.Text);
            if (String.IsNullOrWhiteSpace(target) || target.EndsWith(": ", StringComparison.Ordinal)) target = kind + ": " + Tr("alvo não definido");
            string region = step.HasRegion ? " · " + RegionDescription(step) : " · " + Tr("sem área");
            string line = String.Format("{0:00}. {1}{2}", e.Index + 1, target, region);
            using (Font font = FontManager.Create(8.2f, FontStyle.Regular))
                TextRenderer.DrawText(e.Graphics, line, font, Rectangle.Inflate(e.Bounds, -9, 0), foreground,
                    TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix);
            e.DrawFocusRectangle();
        }

        private void SaveWorkflow()
        {
            if (!ValidateTiming()) return;
            CommitEditorChanges();
            if (steps.Count == 0) { SetStatus(Tr("Adicione ao menos uma etapa ao fluxo.")); return; }
            try
            {
                VisualWorkflow workflow = new VisualWorkflow { Name = workflowNameBox.Text.Trim() };
                foreach (VisualWorkflowStep step in steps) workflow.Steps.Add(step.Copy());
                VisualWorkflowStore.Save(macroFolder, workflow);
                for (int index = 0; index < steps.Count; index++) steps[index].ImagePath = workflow.Steps[index].ImagePath;
                PopulateEditor();
                workflowNameBox.Text = workflow.Name;
                RefreshWorkflowNames();
                for (int index = 0; index < savedWorkflows.Items.Count; index++)
                    if (savedWorkflows.Items[index].Name == workflow.Name) savedWorkflows.SelectedIndex = index;
                SetStatus(Tr("Fluxo salvo"));
            }
            catch (Exception ex) { ShowError(VisualRecognitionService.ErrorMessage(ex, language)); }
        }

        private void LoadWorkflow()
        {
            string name = savedWorkflows.SelectedItem == null ? "" : ((MacroChoice)savedWorkflows.SelectedItem).Name;
            if (String.IsNullOrWhiteSpace(name)) return;
            try
            {
                VisualWorkflow workflow = VisualWorkflowStore.Load(macroFolder, name);
                steps.Clear();
                foreach (VisualWorkflowStep step in workflow.Steps) steps.Add(step);
                workflowNameBox.Text = workflow.Name;
                RefreshStepList(steps.Count > 0 ? 0 : -1);
                SetStatus(Tr("Fluxo carregado"));
            }
            catch (Exception ex) { ShowError(VisualRecognitionService.ErrorMessage(ex, language)); }
        }

        private void NewWorkflow()
        {
            if (isBusy) return;
            steps.Clear();
            workflowNameBox.Text = Tr("Novo fluxo");
            RefreshStepList(-1);
            SetStatus(Tr("Novo fluxo"));
        }

        private void RefreshWorkflowNames()
        {
            string selected = savedWorkflows.SelectedItem == null ? "" : ((MacroChoice)savedWorkflows.SelectedItem).Name;
            try
            {
                savedWorkflows.BeginUpdate();
                savedWorkflows.ClearItems();
                foreach (string name in VisualWorkflowStore.ListNames(macroFolder)) savedWorkflows.Items.Add(new MacroChoice { Name = name, Path = name });
                int index = savedWorkflows.Items.FindIndex(delegate(MacroChoice choice) { return choice.Name == selected; });
                if (index >= 0) savedWorkflows.SelectedIndex = index;
                else if (savedWorkflows.Items.Count > 0) savedWorkflows.SelectedIndex = 0;
            }
            catch { }
            finally { savedWorkflows.EndUpdate(); }
            UpdateEnabledState();
        }

        private void ShowError(string message)
        {
            MessageBox.Show(FindForm(), message, Tr("Leitor de tela"), MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }

        public void ApplyTheme(ThemePalette current)
        {
            palette = current;
            BackColor = palette.Background;
            scrollViewer.ApplyTheme(palette.Background, palette.Background, palette.Border);
            foreach (Control control in new Control[] { workflowCard, stepsCard, editorCard, executionCard })
            {
                if (control == null) continue;
                control.BackColor = palette.Card;
                RoundedPanel card = control as RoundedPanel;
                if (card != null) card.BorderColor = palette.Border;
            }
            stepList.BackColor = palette.Card;
            stepList.ForeColor = palette.Text;
            savedWorkflows.ApplyTheme(palette);
            targetKindBox.ApplyTheme(palette);
            defaultMonitorBox.ApplyTheme(palette);
            Invalidate(true);
            stepList.Invalidate();
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing && runCancellation != null)
            {
                ReaderExecution.Cancel(runCancellation);
            }
            if (disposing && selectedCapture != null) { selectedCapture.Dispose(); selectedCapture = null; }
            base.Dispose(disposing);
        }
    }

}
