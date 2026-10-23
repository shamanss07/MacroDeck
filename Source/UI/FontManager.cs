using System;
using System.Drawing;
using System.Drawing.Text;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Collections.Generic;
using System.Threading;

namespace MacroDeck
{
    internal static class FontManager
    {
        private static readonly PrivateFontCollection privateFonts = new PrivateFontCollection();
        private static GCHandle fontDataHandle;
        private static FontFamily family;
        private static FontFamily selectedFamily;
        private static string selectedFamilyName = "";
        private static readonly object familyListLock = new object();
        private static string[] installedFamilyNames;

        static FontManager()
        {
            try
            {
                using (Stream stream = Assembly.GetExecutingAssembly().GetManifestResourceStream("MacroDeck.Assets.Montserrat.ttf"))
                {
                    if (stream != null)
                    {
                        byte[] bytes = new byte[stream.Length];
                        stream.Read(bytes, 0, bytes.Length);
                        fontDataHandle = GCHandle.Alloc(bytes, GCHandleType.Pinned);
                        privateFonts.AddMemoryFont(fontDataHandle.AddrOfPinnedObject(), bytes.Length);
                        if (privateFonts.Families.Length > 0) family = privateFonts.Families[0];
                    }
                }
            }
            catch
            {
                if (fontDataHandle.IsAllocated) fontDataHandle.Free();
            }

            if (family == null)
            {
                try { family = new FontFamily("Montserrat"); }
                catch { family = new FontFamily("Segoe UI"); }
            }
            selectedFamily = family;
            selectedFamilyName = family.Name;
        }

        public static string SelectedFamilyName { get { return selectedFamilyName; } }

        public static string[] InstalledFamilies()
        {
            lock (familyListLock)
            {
                if (installedFamilyNames != null) return (string[])installedFamilyNames.Clone();
                installedFamilyNames = EnumerateInstalledFamilies();
                return (string[])installedFamilyNames.Clone();
            }
        }

        public static void PreloadInstalledFamilies()
        {
            Thread worker = new Thread(new ThreadStart(delegate { lock (familyListLock) { if (installedFamilyNames == null) installedFamilyNames = EnumerateInstalledFamilies(); } }));
            worker.IsBackground = true;
            worker.Name = "MacroDeck font discovery";
            worker.Start();
        }

        private static string[] EnumerateInstalledFamilies()
        {
            List<string> names = new List<string>();
            names.Add("Montserrat");
            try
            {
                using (InstalledFontCollection fonts = new InstalledFontCollection())
                {
                    FontFamily[] families = fonts.Families;
                    foreach (FontFamily item in families)
                    {
                        try { if (!String.Equals(item.Name, "Montserrat", StringComparison.OrdinalIgnoreCase)) names.Add(item.Name); }
                        finally { item.Dispose(); }
                    }
                }
            }
            catch { }
            names.Sort(StringComparer.CurrentCultureIgnoreCase);
            names.Remove("Montserrat");
            names.Insert(0, "Montserrat");
            return names.ToArray();
        }

        public static void SetPreferredFamily(string name)
        {
            if (String.IsNullOrWhiteSpace(name)) { selectedFamily = family; selectedFamilyName = family.Name; return; }
            if (String.Equals(name, selectedFamilyName, StringComparison.OrdinalIgnoreCase)) return;
            try
            {
                if (String.Equals(name, "Montserrat", StringComparison.OrdinalIgnoreCase)) selectedFamily = family;
                else selectedFamily = new FontFamily(name);
                selectedFamilyName = selectedFamily.Name;
            }
            catch { selectedFamily = family; selectedFamilyName = family.Name; }
        }

        public static Font Create(float size, FontStyle style)
        {
            try { return new Font(selectedFamily ?? family, size, style, GraphicsUnit.Point); }
            catch { return new Font("Segoe UI", size, style, GraphicsUnit.Point); }
        }

        public static string LicenseText()
        {
            try
            {
                using (Stream stream = Assembly.GetExecutingAssembly().GetManifestResourceStream("MacroDeck.Assets.OFL.txt"))
                using (StreamReader reader = new StreamReader(stream)) return reader.ReadToEnd();
            }
            catch { return "Montserrat is licensed under the SIL Open Font License 1.1."; }
        }
    }
}
