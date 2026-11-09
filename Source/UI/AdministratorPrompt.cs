using System;
using System.Drawing;
using System.Windows.Forms;

namespace MacroDeck
{
    internal sealed class AdministratorPrompt : Form
    {
        private readonly CheckBox always;
        public bool AlwaysRequest { get { return always.Checked; } }
        public AdministratorPrompt(string language, bool preference, string error)
        {
            Func<string, string> text = delegate(string value) { return Localization.Translate(language, value); };
            Text = "MacroDeck"; StartPosition = FormStartPosition.CenterScreen;
            FormBorderStyle = FormBorderStyle.FixedDialog; MaximizeBox = false; MinimizeBox = false;
            AutoScaleMode = AutoScaleMode.Dpi; ClientSize = new Size(540, 286);
            Font = new Font("Segoe UI", 9f);
            Controls.Add(new Label { Text = text("MacroDeck está sem modo administrador"), Location = new Point(20, 18), Size = new Size(500, 30), Font = new Font(Font, FontStyle.Bold) });
            Controls.Add(new Label { Text = text("Permissões limitadas podem impedir ações em outros programas. Reinicie como administrador para solicitar acesso pelo Windows."), Location = new Point(20, 54), Size = new Size(500, 46) });
            always = new CheckBox { Text = text("Sempre solicitar administrador ao abrir"), Checked = preference, Location = new Point(20, 107), Size = new Size(500, 26) };
            Controls.Add(always);
            Controls.Add(new Label { Text = text("O Windows continuará pedindo sua autorização pelo UAC."), Location = new Point(20, 140), Size = new Size(500, 30) });
            Controls.Add(new Label { Text = error ?? "", ForeColor = Color.Firebrick, Location = new Point(20, 176), Size = new Size(500, 40) });
            var continueButton = new Button { Text = text("Continuar sem administrador"), Location = new Point(20, 232), Size = new Size(238, 34), DialogResult = DialogResult.No };
            var restartButton = new Button { Text = text("Reiniciar como administrador"), Location = new Point(270, 232), Size = new Size(250, 34), DialogResult = DialogResult.Yes };
            Controls.Add(continueButton); Controls.Add(restartButton);
            AcceptButton = restartButton;
        }
    }
}
