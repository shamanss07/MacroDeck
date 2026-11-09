using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Security.Principal;
using System.Text;
using System.Web.Script.Serialization;
using System.Windows.Forms;

namespace MacroDeck
{
    internal static class Administration
    {
        private const string RestartArgument = "--elevation-requested";
        public static string SettingsPath { get { return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "MacroDeck", "settings.json"); } }

        public static bool IsElevated()
        {
            using (WindowsIdentity identity = WindowsIdentity.GetCurrent())
                return new WindowsPrincipal(identity).IsInRole(WindowsBuiltInRole.Administrator);
        }

        public static bool ShouldRequestAutomatically(bool elevated, bool preference, bool attempted)
        {
            return !elevated && preference && !attempted;
        }

        public static ProcessStartInfo ElevationRequest(string executable)
        {
            return new ProcessStartInfo {
                FileName = executable, WorkingDirectory = Path.GetDirectoryName(executable),
                UseShellExecute = true, Verb = "runas", Arguments = RestartArgument
            };
        }

        public static string ErrorText(Exception error, string language)
        {
            Win32Exception native = error as Win32Exception;
            string text = native != null && native.NativeErrorCode == 1223
                ? "A solicitação de administrador foi cancelada. Você pode tentar novamente ou continuar sem administrador."
                : "Não foi possível iniciar como administrador. Você pode tentar novamente ou continuar sem administrador.";
            return Localization.Translate(language, text);
        }

        private static Dictionary<string, object> ReadDocument(string path)
        {
            if (!File.Exists(path)) return new Dictionary<string, object>();
            if (new FileInfo(path).Length > 1024 * 1024) throw new InvalidDataException();
            var data = new JavaScriptSerializer().Deserialize<Dictionary<string, object>>(File.ReadAllText(path, Encoding.UTF8));
            if (data == null) throw new InvalidDataException();
            return data;
        }

        public static void SavePreference(string path, bool value)
        {
            var data = ReadDocument(path);
            data["AlwaysRunAsAdministrator"] = value;
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            string temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
            try
            {
                byte[] bytes = new UTF8Encoding(false).GetBytes(new JavaScriptSerializer().Serialize(data));
                using (var stream = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None))
                { stream.Write(bytes, 0, bytes.Length); stream.Flush(true); }
                if (File.Exists(path)) File.Replace(temporary, path, null); else File.Move(temporary, path);
            }
            finally { if (File.Exists(temporary)) File.Delete(temporary); }
        }

        // Called before creating MainForm, hooks, or tray icon. Only a successful
        // elevation request exits the original instance; cancellation stays usable.
        public static bool AllowStartup(string[] arguments)
        {
            if (IsElevated()) return true;
            bool always = false;
            string language = Localization.DetectDefaultLanguage();
            try
            {
                var data = ReadDocument(SettingsPath); object value;
                if (data.TryGetValue("AlwaysRunAsAdministrator", out value) && value is bool) always = (bool)value;
                if (data.TryGetValue("Language", out value) && (Equals(value, "en-US") || Equals(value, "pt-BR"))) language = (string)value;
            }
            catch { }
            bool automatic = ShouldRequestAutomatically(false, always, Array.IndexOf(arguments, RestartArgument) >= 0);
            string error = "";
            while (true)
            {
                if (!automatic)
                {
                    using (var prompt = new AdministratorPrompt(language, always, error))
                    {
                        DialogResult choice = prompt.ShowDialog();
                        if (choice != DialogResult.Yes && choice != DialogResult.No) return false;
                        if (prompt.AlwaysRequest != always)
                        {
                            try { SavePreference(SettingsPath, prompt.AlwaysRequest); always = prompt.AlwaysRequest; }
                            catch
                            {
                                MessageBox.Show(Localization.Translate(language, "Não foi possível salvar a preferência de administrador."), "MacroDeck", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                            }
                        }
                        if (choice == DialogResult.No) return true;
                    }
                }
                automatic = false;
                try
                {
                    Process process = Process.Start(ElevationRequest(Application.ExecutablePath));
                    if (process != null) { process.Dispose(); return false; }
                    error = ErrorText(new InvalidOperationException(), language);
                }
                catch (Exception ex) { error = ErrorText(ex, language); }
            }
        }
    }
}
