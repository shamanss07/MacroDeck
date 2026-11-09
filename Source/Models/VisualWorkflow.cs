using System;
using System.Collections.Generic;
using System.IO;
using System.Web.Script.Serialization;
using System.Security.Cryptography;
using System.Drawing;

namespace MacroDeck
{
    internal sealed class VisualWorkflow
    {
        public string Name = "";
        public List<VisualWorkflowStep> Steps = new List<VisualWorkflowStep>();

        public VisualWorkflow Copy()
        {
            VisualWorkflow copy = new VisualWorkflow { Name = Name ?? "" };
            if (Steps != null)
                foreach (VisualWorkflowStep step in Steps)
                    if (step != null) copy.Steps.Add(step.Copy());
            return copy;
        }
    }

    internal sealed class VisualWorkflowStep
    {
        public string Id = Guid.NewGuid().ToString("N");
        public string Kind = "Text";
        public string Text = "";
        public string ImagePath = "";
        public bool HasRegion;
        public int RegionX;
        public int RegionY;
        public int RegionWidth;
        public int RegionHeight;
        public int TimeoutSeconds = 15;
        public int DelayAfterMilliseconds = 250;

        public override string ToString() { return Kind == "Image" ? Path.GetFileName(ImagePath ?? "") : Text; }

        public VisualWorkflowStep Copy()
        {
            return new VisualWorkflowStep {
                Id = Id, Kind = Kind, Text = Text, ImagePath = ImagePath, HasRegion = HasRegion,
                RegionX = RegionX, RegionY = RegionY, RegionWidth = RegionWidth, RegionHeight = RegionHeight,
                TimeoutSeconds = TimeoutSeconds, DelayAfterMilliseconds = DelayAfterMilliseconds
            };
        }
    }

    internal static class VisualWorkflowStore
    {
        private const string DocumentName = "workflow.json";
        private static readonly JavaScriptSerializer Serializer = new JavaScriptSerializer { MaxJsonLength = Int32.MaxValue };

        public static string Root(string macroFolder)
        {
            return Path.Combine(macroFolder, "VisualWorkflows");
        }

        public static string[] ListNames(string macroFolder)
        {
            string root = Root(macroFolder);
            if (!Directory.Exists(root)) return new string[0];
            List<string> names = new List<string>();
            foreach (string directory in Directory.GetDirectories(root))
                if (File.Exists(Path.Combine(directory, DocumentName))) names.Add(Path.GetFileName(directory));
            names.Sort(StringComparer.CurrentCultureIgnoreCase);
            return names.ToArray();
        }

        public static void Save(string macroFolder, VisualWorkflow workflow)
        {
            if (workflow == null) throw new ArgumentNullException("workflow");
            VisualWorkflow original = workflow;
            if (workflow.Steps == null || workflow.Steps.Exists(delegate(VisualWorkflowStep s) { return s == null; }))
                throw new InvalidDataException("The workflow file is invalid.");
            workflow = workflow.Copy();
            if (workflow.Steps != null && workflow.Steps.Count > 100) throw new InvalidDataException("A visual workflow supports up to 100 steps.");
            string safeName = SafeName(workflow.Name);
            string root = Root(macroFolder);
            string directory = Path.Combine(root, safeName);
            Directory.CreateDirectory(directory);
            VisualWorkflowDocument document = new VisualWorkflowDocument { Name = safeName, Steps = new List<VisualWorkflowStepDocument>() };

            if (workflow.Steps != null)
            {
                foreach (VisualWorkflowStep step in workflow.Steps)
                {
                    ValidateKind(step.Kind);
                    if (step == null) continue;
                    Guid id;
                    if (!Guid.TryParse(step.Id, out id)) id = Guid.NewGuid();
                    step.Id = id.ToString("N");
                    VisualWorkflowStepDocument saved = new VisualWorkflowStepDocument {
                        Id = step.Id, Kind = String.Equals(step.Kind, "Image", StringComparison.OrdinalIgnoreCase) ? "Image" : "Text",
                        Text = step.Text ?? "", HasRegion = step.HasRegion, RegionX = step.RegionX, RegionY = step.RegionY,
                        RegionWidth = step.RegionWidth, RegionHeight = step.RegionHeight,
                        TimeoutSeconds = Math.Max(1, Math.Min(600, step.TimeoutSeconds)),
                        DelayAfterMilliseconds = Math.Max(0, Math.Min(60000, step.DelayAfterMilliseconds))
                    };
                    if (String.Equals(saved.Kind, "Image", StringComparison.OrdinalIgnoreCase))
                    {
                        if (!File.Exists(step.ImagePath)) throw new InvalidDataException("Choose an existing reference image for every image step before saving.");
                        string targetDirectory = Path.Combine(directory, "targets");
                        Directory.CreateDirectory(targetDirectory);
                        byte[] imageBytes;
                        using (Bitmap source = new Bitmap(step.ImagePath))
                        {
                            if (source.Width < 8 || source.Height < 8 || (long)source.Width * source.Height > 2000000)
                                throw new InvalidDataException("Use a reference image between 8 pixels and 2 megapixels, cropped around a distinct target.");
                            using (Bitmap bitmap = new Bitmap(source))
                            using (MemoryStream stream = new MemoryStream()) {
                                bitmap.Save(stream, System.Drawing.Imaging.ImageFormat.Png);
                                imageBytes = stream.ToArray();
                            }
                        }
                        string fileName;
                        using (SHA256 hash = SHA256.Create()) fileName = BitConverter.ToString(hash.ComputeHash(imageBytes)).Replace("-", "").ToLowerInvariant() + ".png";
                        string destination = Path.Combine(targetDirectory, fileName);
                        bool valid = false;
                        if (File.Exists(destination))
                            using (SHA256 hash = SHA256.Create())
                            using (FileStream existing = File.OpenRead(destination))
                                valid = BitConverter.ToString(hash.ComputeHash(existing)).Replace("-", "").ToLowerInvariant() + ".png" == fileName;
                        if (!valid) AtomicWrite(destination, imageBytes);
                        saved.ImageFile = Path.Combine("targets", fileName);
                        step.ImagePath = destination;
                    }
                    else saved.ImageFile = "";
                    document.Steps.Add(saved);
                }
            }

            string path = Path.Combine(directory, DocumentName);
            byte[] json = new System.Text.UTF8Encoding(false).GetBytes(Serializer.Serialize(document));
            if (json.Length > 1024 * 1024) throw new InvalidDataException("The workflow file is too large.");
            AtomicWrite(path, json);
            original.Name = safeName;
            for (int i=0;i<workflow.Steps.Count;i++) {
                original.Steps[i].Id=workflow.Steps[i].Id;
                original.Steps[i].ImagePath=workflow.Steps[i].ImagePath;
            }
        }

        private static void AtomicWrite(string path, byte[] bytes)
        {
            string temporary = Path.Combine(Path.GetDirectoryName(path), Guid.NewGuid().ToString("N") + ".tmp");
            try {
                using(FileStream stream=new FileStream(temporary,FileMode.CreateNew,FileAccess.Write,FileShare.None)) { stream.Write(bytes,0,bytes.Length); stream.Flush(true); }
                if(File.Exists(path))File.Replace(temporary,path,null);else File.Move(temporary,path);
            }
            finally { if(File.Exists(temporary))File.Delete(temporary); }
        }

        private static void ValidateKind(string kind)
        {
            if (!String.Equals(kind,"Image",StringComparison.OrdinalIgnoreCase) && !String.Equals(kind,"Text",StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("The workflow contains an unsupported step type.");
        }

        public static VisualWorkflow Load(string macroFolder, string name)
        {
            if (String.IsNullOrWhiteSpace(name)) throw new InvalidDataException("Choose a saved visual workflow.");
            string root = Path.GetFullPath(Root(macroFolder));
            string directory = Path.GetFullPath(Path.Combine(root, name));
            if (!directory.StartsWith(root + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("The workflow path is invalid.");
            string path = Path.Combine(directory, DocumentName);
            if (new FileInfo(path).Length > 1024 * 1024) throw new InvalidDataException("The workflow file is too large.");
            VisualWorkflowDocument document = Serializer.Deserialize<VisualWorkflowDocument>(File.ReadAllText(path, System.Text.Encoding.UTF8));
            if (document == null) throw new InvalidDataException("The workflow file is invalid.");
            VisualWorkflow workflow = new VisualWorkflow { Name = Path.GetFileName(directory) };
            if (document.Steps == null) return workflow;
            if (document.Steps.Count > 100) throw new InvalidDataException("A visual workflow supports up to 100 steps.");
            foreach (VisualWorkflowStepDocument saved in document.Steps)
            {
                if (saved == null) throw new InvalidDataException("The workflow file is invalid.");
                ValidateKind(saved.Kind);
                string imagePath = "";
                if (!String.IsNullOrWhiteSpace(saved.ImageFile))
                {
                    imagePath = Path.GetFullPath(Path.Combine(directory, saved.ImageFile));
                    if (!imagePath.StartsWith(directory + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)) throw new InvalidDataException("The workflow path is invalid.");
                }
                if (String.Equals(saved.Kind,"Image",StringComparison.OrdinalIgnoreCase) && !File.Exists(imagePath))
                    throw new InvalidDataException("A workflow reference image is missing. Restore it before opening this workflow.");
                workflow.Steps.Add(new VisualWorkflowStep {
                    Id = String.IsNullOrWhiteSpace(saved.Id) ? Guid.NewGuid().ToString("N") : saved.Id,
                    Kind = String.Equals(saved.Kind, "Image", StringComparison.OrdinalIgnoreCase) ? "Image" : "Text",
                    Text = saved.Text ?? "", ImagePath = imagePath, HasRegion = saved.HasRegion,
                    RegionX = saved.RegionX, RegionY = saved.RegionY, RegionWidth = saved.RegionWidth, RegionHeight = saved.RegionHeight,
                    TimeoutSeconds = Math.Max(1, Math.Min(600, saved.TimeoutSeconds <= 0 ? 15 : saved.TimeoutSeconds)),
                    DelayAfterMilliseconds = Math.Max(0, Math.Min(60000, saved.DelayAfterMilliseconds))
                });
            }
            return workflow;
        }

        private static string SafeName(string name)
        {
            if (String.IsNullOrWhiteSpace(name)) throw new InvalidDataException("Enter a name for the workflow.");
            char[] invalid = Path.GetInvalidFileNameChars();
            string value = name.Trim();
            foreach (char character in invalid) value = value.Replace(character, '-');
            value = value.Trim().TrimEnd('.');
            if (String.IsNullOrWhiteSpace(value) || value == "." || value == "..") throw new InvalidDataException("Enter a valid workflow name.");
            if (value.Length > 80) value = value.Substring(0, 80).TrimEnd();
            if (System.Text.RegularExpressions.Regex.IsMatch(value, @"^(CON|PRN|AUX|NUL|COM[1-9]|LPT[1-9])(?:\.|$)", System.Text.RegularExpressions.RegexOptions.IgnoreCase)) value = "Flow " + value;
            return value;
        }

        private sealed class VisualWorkflowDocument
        {
            public string Name = "";
            public List<VisualWorkflowStepDocument> Steps = new List<VisualWorkflowStepDocument>();
        }

        private sealed class VisualWorkflowStepDocument
        {
            public string Id = "";
            public string Kind = "Text";
            public string Text = "";
            public string ImageFile = "";
            public bool HasRegion;
            public int RegionX;
            public int RegionY;
            public int RegionWidth;
            public int RegionHeight;
            public int TimeoutSeconds = 15;
            public int DelayAfterMilliseconds = 250;
        }
    }
}
