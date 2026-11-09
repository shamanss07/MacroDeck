using System;
using System.Collections;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Text.RegularExpressions;
using System.Windows.Forms;
using System.Diagnostics;

namespace MacroDeck
{
    internal sealed class VisualRecognitionMatch
    {
        public Point Center;
        public Rectangle Bounds;
        public double Confidence;
        public string Description = "";
    }

    internal static class VisualRecognitionService
    {
        private const int MaximumSearchPixels = 16000000;
        private const int ImagePollMilliseconds = 180;
        private const int TextPollMilliseconds = 450;
        private const double MinimumImageConfidence = 0.94;

        public static string ErrorMessage(Exception exception, string language)
        {
            while (exception.InnerException != null && (exception is TargetInvocationException || exception is AggregateException)) exception = exception.InnerException;
            return Localization.TranslateError(language, exception.Message);
        }

        public static void ValidateWorkflow(VisualWorkflow workflow, CancellationToken cancellation)
        {
            if (workflow == null || workflow.Steps == null || workflow.Steps.Count == 0)
                throw new InvalidOperationException("The visual workflow has no steps.");
            bool needsText = false;
            foreach (VisualWorkflowStep step in workflow.Steps)
            {
                cancellation.ThrowIfCancellationRequested();
                if (!step.HasRegion) throw new InvalidOperationException("Choose a screen search area for this step first.");
                Rectangle region = new Rectangle(step.RegionX, step.RegionY, step.RegionWidth, step.RegionHeight);
                ValidateRegion(region);
                if (step.Kind == "Image")
                {
                    if (!File.Exists(step.ImagePath)) throw new InvalidOperationException("Choose an existing reference image for this step.");
                    using (Bitmap image = new Bitmap(step.ImagePath))
                    {
                        if (image.Width > region.Width || image.Height > region.Height)
                            throw new InvalidOperationException("The reference image is larger than this search area. Select a larger area or a smaller image.");
                        new PreparedTemplate(image);
                    }
                }
                else
                {
                    if (String.IsNullOrWhiteSpace(step.Text)) throw new InvalidOperationException("Enter the text this step should find.");
                    needsText = true;
                }
            }
            if (needsText) using (WindowsOcrRecognizer ocr = new WindowsOcrRecognizer()) { }
        }

        public static void RunWorkflow(VisualWorkflow workflow, CancellationToken cancellation, Action<string> report, string language, ReaderExecution execution)
        {
            if (workflow == null || workflow.Steps == null || workflow.Steps.Count == 0)
                throw new InvalidOperationException("The visual workflow has no steps.");
            for (int index = 0; index < workflow.Steps.Count; index++)
            {
                cancellation.ThrowIfCancellationRequested();
                VisualWorkflowStep step = workflow.Steps[index];
                string label = String.IsNullOrWhiteSpace(step.Text) ? Path.GetFileName(step.ImagePath) : step.Text;
                if (report != null) report(String.Format("{0}/{1} · {2}", index + 1, workflow.Steps.Count, label));
                FindStep(step, cancellation, report, language, execution, true);
                execution.Wait(step.DelayAfterMilliseconds);
            }
        }

        public static VisualRecognitionMatch LocateStep(VisualWorkflowStep step, CancellationToken cancellation, Action<string> report, string language, ReaderExecution execution)
        {
            return FindStep(step, cancellation, report, language, execution, false);
        }

        public static Bitmap Capture(Rectangle region)
        {
            ValidateRegion(region);
            Bitmap bitmap = new Bitmap(region.Width, region.Height, PixelFormat.Format32bppArgb);
            try
            {
                using (Graphics graphics = Graphics.FromImage(bitmap))
                    graphics.CopyFromScreen(region.Left, region.Top, 0, 0, region.Size, CopyPixelOperation.SourceCopy);
                return bitmap;
            }
            catch
            {
                bitmap.Dispose();
                throw;
            }
        }

        private static VisualRecognitionMatch FindStep(VisualWorkflowStep step, CancellationToken cancellation, Action<string> report, string language, ReaderExecution execution, bool click)
        {
            if (step == null) throw new InvalidOperationException("Choose a visual step first.");
            if (!step.HasRegion) throw new InvalidOperationException("Choose a screen search area for this step first.");
            Rectangle region = new Rectangle(step.RegionX, step.RegionY, step.RegionWidth, step.RegionHeight);
            ValidateRegion(region);
            bool isImage = String.Equals(step.Kind, "Image", StringComparison.OrdinalIgnoreCase);
            if (isImage && (String.IsNullOrWhiteSpace(step.ImagePath) || !File.Exists(step.ImagePath)))
                throw new InvalidOperationException("Choose an existing reference image for this step.");
            if (!isImage && String.IsNullOrWhiteSpace(step.Text))
                throw new InvalidOperationException("Enter the text this step should find.");

            Bitmap template = null;
            PreparedTemplate prepared = null;
            WindowsOcrRecognizer ocr = null;
            try
            {
                if (isImage)
                {
                    using (Bitmap source = new Bitmap(step.ImagePath))
                    {
                        template = new Bitmap(source.Width, source.Height, PixelFormat.Format32bppArgb);
                        using (Graphics graphics = Graphics.FromImage(template)) graphics.DrawImage(source, new Rectangle(0, 0, template.Width, template.Height));
                    }
                    if (template.Width > region.Width || template.Height > region.Height)
                        throw new InvalidOperationException("The reference image is larger than this search area. Select a larger area or a smaller image.");
                    prepared = new PreparedTemplate(template);
                }
                else ocr = new WindowsOcrRecognizer();

                int timeout = Math.Max(1, Math.Min(600, step.TimeoutSeconds));
                Stopwatch clock = Stopwatch.StartNew();
                double latestScore = 0;
                VisualRecognitionMatch previous = null;
                int stableFrames = 0;
                while (clock.ElapsedMilliseconds < timeout * 1000L)
                {
                    cancellation.ThrowIfCancellationRequested();
                    execution.Check();
                    using (Bitmap frame = execution.Capture(region))
                    {
                        VisualRecognitionMatch match = isImage
                            ? prepared.Find(frame, region.Location, cancellation, out latestScore)
                            : ocr.FindText(frame, region.Location, step.Text, cancellation);
                        cancellation.ThrowIfCancellationRequested();
                        if (clock.ElapsedMilliseconds >= timeout * 1000L) break;
                        if (match != null && (!isImage || match.Confidence >= MinimumImageConfidence))
                        {
                            // Compare with the first frame, not the immediately preceding
                            // one: slow drift must not look stable indefinitely.
                            if (previous != null && previous.Bounds == match.Bounds) stableFrames++;
                            else { previous = match; stableFrames = 1; }
                            if (stableFrames >= 3)
                            {
                                Rectangle crop = match.Bounds;
                                crop.Offset(-region.X, -region.Y);
                                using (Bitmap observed = frame.Clone(crop, PixelFormat.Format32bppArgb))
                                    execution.Act(match, observed, click, delegate {
                                        if(clock.ElapsedMilliseconds >= timeout * 1000L) throw new TimeoutException(Localization.Translate(language,"O tempo de busca terminou antes do clique. Execute o fluxo novamente."));
                                    }, isImage ? (Func<bool>)null : delegate {
                                        // OCR labels often change background/antialiasing on
                                        // hover. Recognize the complete label again instead
                                        // of treating a pixel color difference as disappearance.
                                        using(Bitmap fresh=execution.Capture(region)) {
                                            VisualRecognitionMatch confirmed=ocr.FindText(fresh,region.Location,step.Text,cancellation);
                                            return confirmed!=null && Math.Abs(confirmed.Center.X-match.Center.X)<=2 && Math.Abs(confirmed.Center.Y-match.Center.Y)<=2;
                                        }
                                    });
                                return match;
                            }
                        }
                        else { previous = null; stableFrames = 0; }
                    }
                    int interval = isImage ? ImagePollMilliseconds : TextPollMilliseconds;
                    execution.Wait((int)Math.Min(interval, Math.Max(0, timeout * 1000L - clock.ElapsedMilliseconds)));
                }
                string targetName = isImage ? Path.GetFileName(step.ImagePath) : step.Text;
                if (isImage) throw new TimeoutException(String.Format(Localization.Translate(language, "Tempo limite esgotado antes de encontrar a imagem (melhor correspondência: {0}%)."), (int)Math.Round(latestScore * 100)));
                throw new TimeoutException(String.Format(Localization.Translate(language, "Tempo limite esgotado antes de encontrar o texto: {0}"), targetName));
            }
            finally
            {
                if (template != null) template.Dispose();
                if (ocr != null) ocr.Dispose();
            }
        }

        private static void ValidateRegion(Rectangle region)
        {
            if (region.Width < 6 || region.Height < 6) throw new InvalidOperationException("The selected search area is too small.");
            long area = (long)region.Width * region.Height;
            if (area > MaximumSearchPixels) throw new InvalidOperationException("The search area is too large. Select a smaller part of the screen for better performance.");
            Rectangle screen = SystemInformation.VirtualScreen;
            if (!screen.Contains(region)) throw new InvalidOperationException("The saved search area is outside the current desktop. Select it again after changing monitor layout.");
        }

        // Cache reference features once per step. A cheap gradient filter rejects most
        // screen positions before correlation, keeping work bounded to the chosen ROI.
        private sealed class PreparedTemplate
        {
            private readonly GrayImage target;
            private readonly byte[] targetPixels;
            private readonly List<Point> features = new List<Point>();
            private readonly List<Point> anchors = new List<Point>();
            private readonly List<int> gradients = new List<int>();
            private readonly double sumTarget;
            private readonly double varianceTarget;

            public PreparedTemplate(Bitmap bitmap)
            {
                if (bitmap.Width < 8 || bitmap.Height < 8 || (long)bitmap.Width * bitmap.Height > 2000000)
                    throw new InvalidOperationException("Use a reference image between 8 pixels and 2 megapixels, cropped around a distinct target.");
                target = ToGray(bitmap);
                targetPixels = ReadPixels(bitmap, new Rectangle(Point.Empty, bitmap.Size));
                List<Point> edgePoints = new List<Point>();
                for (int gy = 0; gy < 6; gy++)
                    for (int gx = 0; gx < 16; gx++)
                    {
                        Point best = Point.Empty;
                        int strongest = 0;
                        for (int y = gy * target.Height / 6; y < (gy + 1) * target.Height / 6; y++)
                            for (int x = gx * (target.Width - 1) / 16; x < (gx + 1) * (target.Width - 1) / 16; x++)
                            {
                                int gradient = Math.Abs(target.Pixels[y * target.Width + x + 1] - target.Pixels[y * target.Width + x]);
                                if (gradient > strongest) { strongest = gradient; best = new Point(x, y); }
                            }
                        if (strongest >= 24) edgePoints.Add(best);
                    }
                if (edgePoints.Count < 8)
                    throw new InvalidOperationException("This reference has too little detail. Crop a distinct label or icon instead of an empty background.");
                for (int i = 0; i < Math.Min(16, edgePoints.Count); i++)
                {
                    Point point = edgePoints[i * edgePoints.Count / Math.Min(16, edgePoints.Count)];
                    anchors.Add(point);
                    int offset = point.Y * target.Width + point.X;
                    gradients.Add(target.Pixels[offset + 1] - target.Pixels[offset]);
                }
                features.AddRange(edgePoints);
                foreach (Point point in edgePoints) features.Add(new Point(point.X + 1, point.Y));
                for (int y = 0; y < 8; y++)
                    for (int x = 0; x < 16; x++) features.Add(new Point(x * (target.Width - 1) / 15, y * (target.Height - 1) / 7));
                double sum = 0, squared = 0;
                foreach (Point point in features)
                {
                    int value = target.Pixels[point.Y * target.Width + point.X];
                    sum += value; squared += value * value;
                }
                sumTarget = sum;
                varianceTarget = features.Count * squared - sum * sum;
            }

            public VisualRecognitionMatch Find(Bitmap bitmap, Point origin, CancellationToken cancellation, out double confidence)
            {
                GrayImage search = ToGray(bitmap);
                List<VisualRecognitionMatch> matches = new List<VisualRecognitionMatch>();
                confidence = 0;
                int[] offsets = new int[features.Count];
                int[] reference = new int[features.Count];
                int[] anchorOffsets = new int[anchors.Count];
                for (int i = 0; i < features.Count; i++)
                {
                    offsets[i] = features[i].Y * search.Width + features[i].X;
                    reference[i] = target.Pixels[features[i].Y * target.Width + features[i].X];
                }
                for (int i = 0; i < anchors.Count; i++) anchorOffsets[i] = anchors[i].Y * search.Width + anchors[i].X;
                for (int y = 0; y <= search.Height - target.Height; y++)
                {
                    cancellation.ThrowIfCancellationRequested();
                    for (int x = 0; x <= search.Width - target.Width; x++)
                    {
                        int start = y * search.Width + x;
                        int failures = 0;
                        for (int i = 0; i < anchorOffsets.Length; i++)
                        {
                            int offset = start + anchorOffsets[i];
                            int gradient = search.Pixels[offset + 1] - search.Pixels[offset];
                            if (Math.Abs(gradient - gradients[i]) > 42 && ++failures > anchorOffsets.Length / 4) break;
                        }
                        if (failures > anchorOffsets.Length / 4) continue;
                        double sum = 0, squared = 0, product = 0, error = 0;
                        for (int i = 0; i < offsets.Length; i++)
                        {
                            int value = search.Pixels[start + offsets[i]];
                            sum += value; squared += value * value;
                            product += value * reference[i]; error += Math.Abs(value - reference[i]);
                        }
                        double variance = offsets.Length * squared - sum * sum;
                        if (variance < 1 || varianceTarget < 1 || error / offsets.Length > 32) continue;
                        double score = (offsets.Length * product - sumTarget * sum) / Math.Sqrt(varianceTarget * variance);
                        confidence = Math.Max(confidence, score);
                        if (score < MinimumImageConfidence) continue;
                        // Correlation on sampled edges only nominates a candidate. Check
                        // all RGB pixels in local blocks so a different digit/icon/color
                        // cannot hide in a large matching background.
                        if (!PixelsAgree(targetPixels, ReadPixels(bitmap, new Rectangle(x,y,target.Width,target.Height)), target.Width, target.Height)) continue;
                        Point center = new Point(origin.X + x + target.Width / 2, origin.Y + y + target.Height / 2);
                        bool sameTarget = false;
                        foreach (VisualRecognitionMatch existing in matches)
                        {
                            if (Math.Abs(center.X - existing.Center.X) < target.Width / 2 && Math.Abs(center.Y - existing.Center.Y) < target.Height / 2)
                            {
                                if (score > existing.Confidence) { existing.Confidence = score; existing.Center = center; existing.Bounds = new Rectangle(origin.X+x,origin.Y+y,target.Width,target.Height); }
                                sameTarget = true; break;
                            }
                        }
                        if (!sameTarget) matches.Add(new VisualRecognitionMatch { Center = center, Bounds = new Rectangle(origin.X+x,origin.Y+y,target.Width,target.Height), Confidence = score, Description = "Image" });
                        if (matches.Count > 1)
                            throw new InvalidOperationException("The image appears more than once in this search area. Select a smaller area.");
                    }
                }
                return matches.Count == 1 ? matches[0] : null;
            }
        }
        private static GrayImage ToGray(Bitmap input)
        {
            if (input.PixelFormat != PixelFormat.Format32bppArgb)
            {
                using (Bitmap normalized = new Bitmap(input.Width,input.Height,PixelFormat.Format32bppArgb))
                { using(Graphics g=Graphics.FromImage(normalized))g.DrawImageUnscaled(input,0,0); return ToGray(normalized); }
            }
                Rectangle rect = new Rectangle(0, 0, input.Width, input.Height);
                BitmapData data = input.LockBits(rect, ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
                try
                {
                    byte[] gray = new byte[input.Width * input.Height];
                    byte[] row = new byte[input.Width * 4];
                    for (int y = 0; y < input.Height; y++)
                    {
                        Marshal.Copy(IntPtr.Add(data.Scan0, y * data.Stride), row, 0, row.Length);
                        int destination = y * input.Width;
                        for (int x = 0; x < input.Width; x++)
                        {
                            int source = x * 4;
                            gray[destination + x] = (byte)((row[source + 2] * 77 + row[source + 1] * 150 + row[source] * 29) >> 8);
                        }
                    }
                    return new GrayImage { Width = input.Width, Height = input.Height, Pixels = gray };
                }
                finally { input.UnlockBits(data); }
        }

        private static byte[] ReadPixels(Bitmap bitmap, Rectangle region)
        {
            byte[] pixels = new byte[checked(region.Width * region.Height * 4)];
            BitmapData data=bitmap.LockBits(region,ImageLockMode.ReadOnly,PixelFormat.Format32bppArgb);
            try { for(int y=0;y<region.Height;y++)Marshal.Copy(IntPtr.Add(data.Scan0,y*data.Stride),pixels,y*region.Width*4,region.Width*4); }
            finally { bitmap.UnlockBits(data); }
            return pixels;
        }

        internal static bool SameTargetPixels(Bitmap first, Bitmap second)
        {
            if (first.Size != second.Size) return false;
            byte[] before = ReadPixels(first,new Rectangle(Point.Empty,first.Size));
            byte[] after = ReadPixels(second,new Rectangle(Point.Empty,second.Size));
            if(PixelsAgree(before,after,first.Width,first.Height))return true;
            // A hover highlight can recolor the same shape. Fit a single monotonic
            // color transform per channel, then require the local geometry to agree.
            // This is only used to revalidate an already recognized target.
            int count=first.Width*first.Height;
            for(int channel=0;channel<3;channel++) {
                double x=0,y=0,xx=0,xy=0;
                for(int p=channel;p<before.Length;p+=4) { double a=before[p],b=after[p];x+=a;y+=b;xx+=a*a;xy+=a*b; }
                double variance=count*xx-x*x;
                double gain=variance<count*count*4?1:(count*xy-x*y)/variance;
                if(gain<0.3||gain>3)return EdgesAgree(ReadPixels(first,new Rectangle(Point.Empty,first.Size)),after,first.Width,first.Height);
                double offset=(y-gain*x)/count;
                for(int p=channel;p<before.Length;p+=4)before[p]=(byte)Math.Max(0,Math.Min(255,Math.Round(before[p]*gain+offset)));
            }
            return PixelsAgree(before,after,first.Width,first.Height) || EdgesAgree(ReadPixels(first,new Rectangle(Point.Empty,first.Size)),after,first.Width,first.Height);
        }

        private static bool EdgesAgree(byte[] before,byte[] after,int width,int height)
        {
            int total=0;
            for(int top=1;top<height-1;top+=8)for(int left=1;left<width-1;left+=8) {
                int edges=0,missing=0;
                for(int y=top;y<Math.Min(top+8,height-1);y++)for(int x=left;x<Math.Min(left+8,width-1);x++) {
                    int p=(y*width+x)*4;
                    int gx=Luma(before,p+4)-Luma(before,p-4), gy=Luma(before,p+width*4)-Luma(before,p-width*4);
                    double a=gx*gx+gy*gy;
                    if(a<1600)continue;
                    edges++;
                    int hx=Luma(after,p+4)-Luma(after,p-4),hy=Luma(after,p+width*4)-Luma(after,p-width*4);
                    double b=hx*hx+hy*hy,dot=gx*hx+gy*hy;
                    if(b<400 || dot<=0 || dot*dot<a*b*0.9)missing++;
                }
                total+=edges;
                if(missing>Math.Max(1,edges/10))return false;
            }
            return total>=12;
        }
        private static int Luma(byte[] pixels,int p) { return (pixels[p+2]*77+pixels[p+1]*150+pixels[p]*29)>>8; }

        private static bool PixelsAgree(byte[] first, byte[] second, int width, int height)
        {
            for(int top=0;top<height;top+=8)for(int left=0;left<width;left+=8)
            {
                int error=0, changed=0, count=0;
                for(int y=top;y<Math.Min(top+8,height);y++)for(int x=left;x<Math.Min(left+8,width);x++)
                {
                    int p=(y*width+x)*4;
                    int difference=Math.Max(Math.Abs(first[p]-second[p]),Math.Max(Math.Abs(first[p+1]-second[p+1]),Math.Abs(first[p+2]-second[p+2])));
                    error+=difference; if(difference>32)changed++; count++;
                }
                if(error>count*18 || changed>Math.Max(1,count/8))return false;
            }
            return true;
        }

        private sealed class GrayImage
        {
            public int Width;
            public int Height;
            public byte[] Pixels;
        }

        private sealed class WindowsOcrRecognizer : IDisposable
        {
            private readonly Assembly projection;
            private readonly object engine;
            private readonly Type resultType;
            private readonly Type pixelFormatType;
            private readonly Type alphaModeType;
            private readonly MethodInfo asBuffer;
            private readonly MethodInfo createBitmap;
            private readonly MethodInfo recognizeAsync;
            private readonly MethodInfo asTask;

            public WindowsOcrRecognizer()
            {
                string windows = Environment.GetFolderPath(Environment.SpecialFolder.Windows);
                string architecture = Environment.Is64BitProcess ? "Framework64" : "Framework";
                string projectionPath = Path.Combine(windows, "Microsoft.NET\\" + architecture + "\\v4.0.30319\\System.Runtime.WindowsRuntime.dll");
                if (!File.Exists(projectionPath)) throw new NotSupportedException("Windows OCR is unavailable on this system.");
                projection = Assembly.LoadFrom(projectionPath);
                Type engineType = WinRtType("Windows.Media.Ocr.OcrEngine");
                Type bitmapType = WinRtType("Windows.Graphics.Imaging.SoftwareBitmap");
                pixelFormatType = WinRtType("Windows.Graphics.Imaging.BitmapPixelFormat");
                alphaModeType = WinRtType("Windows.Graphics.Imaging.BitmapAlphaMode");
                Type bufferExtensions = projection.GetType("System.Runtime.InteropServices.WindowsRuntime.WindowsRuntimeBufferExtensions", true);
                asBuffer = bufferExtensions.GetMethod("AsBuffer", new Type[] { typeof(byte[]) });
                createBitmap = FindMethod(bitmapType, "CreateCopyFromBuffer", 5);
                recognizeAsync = FindMethod(engineType, "RecognizeAsync", 1);
                Type taskExtensions = projection.GetType("System.WindowsRuntimeSystemExtensions", true);
                foreach (MethodInfo method in taskExtensions.GetMethods(BindingFlags.Public | BindingFlags.Static))
                {
                    ParameterInfo[] parameters = method.GetParameters();
                    if (method.Name == "AsTask" && method.IsGenericMethodDefinition && method.GetGenericArguments().Length == 1 && parameters.Length == 1 &&
                        parameters[0].ParameterType.IsGenericType && parameters[0].ParameterType.GetGenericTypeDefinition().FullName == "Windows.Foundation.IAsyncOperation`1")
                    { asTask = method; break; }
                }
                if (asTask == null) throw new NotSupportedException("Windows OCR asynchronous support is unavailable.");
                MethodInfo createEngine = FindMethod(engineType, "TryCreateFromUserProfileLanguages", 0);
                engine = createEngine.Invoke(null, null);
                if (engine == null) throw new NotSupportedException("Windows OCR needs at least one installed OCR language. Install the matching Windows language pack and try again.");
                Type asyncOperationType = FindWinRtInterface(recognizeAsync.ReturnType, "Windows.Foundation.IAsyncOperation`1");
                resultType = asyncOperationType.GetGenericArguments()[0];
            }

            public VisualRecognitionMatch FindText(Bitmap frame, Point origin, string targetText, CancellationToken cancellation)
            {
                cancellation.ThrowIfCancellationRequested();
                Type engineType = WinRtType("Windows.Media.Ocr.OcrEngine");
                int maxDimension = Convert.ToInt32(engineType.GetProperty("MaxImageDimension").GetValue(null, null));
                if (frame.Width > maxDimension || frame.Height > maxDimension)
                    throw new InvalidOperationException("The search area exceeds the Windows OCR size limit. Select a smaller area.");
                BitmapData data = frame.LockBits(new Rectangle(0, 0, frame.Width, frame.Height), ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
                byte[] pixels;
                try
                {
                    pixels = new byte[frame.Width * frame.Height * 4];
                    byte[] row = new byte[frame.Width * 4];
                    for (int y = 0; y < frame.Height; y++)
                    {
                        Marshal.Copy(IntPtr.Add(data.Scan0, y * data.Stride), row, 0, row.Length);
                        Buffer.BlockCopy(row, 0, pixels, y * row.Length, row.Length);
                    }
                }
                finally { frame.UnlockBits(data); }

                object buffer = asBuffer.Invoke(null, new object[] { pixels });
                object pixelFormat = Enum.Parse(pixelFormatType, "Bgra8");
                object alphaMode = Enum.Parse(alphaModeType, "Ignore");
                object bitmap = createBitmap.Invoke(null, new object[] { buffer, pixelFormat, frame.Width, frame.Height, alphaMode });
                try
                {
                    object operation = recognizeAsync.Invoke(engine, new object[] { bitmap });
                    Task task = (Task)asTask.MakeGenericMethod(resultType).Invoke(null, new object[] { operation });
                    // Wait for the native operation to finish before disposing its bitmap.
                    // Cancellation prevents any cursor movement or click after recognition.
                    task.Wait();
                    cancellation.ThrowIfCancellationRequested();
                    object result = task.GetType().GetProperty("Result").GetValue(task, null);
                    return MatchOcrResult(result, targetText, origin);
                }
                finally { DisposeWinRt(bitmap); }
            }

            private static VisualRecognitionMatch MatchOcrResult(object result, string targetText, Point origin)
            {
                if (result == null) return null;
                string query = NormalizeText(targetText);
                if (String.IsNullOrEmpty(query)) return null;
                IEnumerable lines = GetProperty(result, "Lines") as IEnumerable;
                if (lines == null) return null;
                List<RectangleF> matches = new List<RectangleF>();
                bool completeLabel = false;
                string[] queryWords = query.Split(new char[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
                foreach (object line in lines)
                {
                    List<object> words = new List<object>();
                    IEnumerable rawWords = GetProperty(line, "Words") as IEnumerable;
                    if (rawWords != null) foreach (object word in rawWords) words.Add(word);
                    if (words == null || words.Count == 0) continue;
                    List<string> normalizedWords = new List<string>();
                    for (int i = 0; i < words.Count; i++) normalizedWords.Add(NormalizeText(Convert.ToString(GetProperty(words[i], "Text"))));
                    for (int start = 0; start < normalizedWords.Count; start++)
                    {
                        int count = queryWords.Length;
                        if (start + count > normalizedWords.Count) break;
                        bool found = true;
                        for (int part = 0; part < count; part++)
                        {
                            if (!String.Equals(normalizedWords[start + part], queryWords[part], StringComparison.OrdinalIgnoreCase)) { found = false; break; }
                        }
                        if (!found) continue;
                        RectangleF bounds = RectangleF.Empty;
                        for (int part = 0; part < count; part++)
                        {
                            object rect = GetProperty(words[start + part], "BoundingRect");
                            float x = Convert.ToSingle(GetProperty(rect, "X"));
                            float y = Convert.ToSingle(GetProperty(rect, "Y"));
                            float width = Convert.ToSingle(GetProperty(rect, "Width"));
                            float height = Convert.ToSingle(GetProperty(rect, "Height"));
                            RectangleF item = new RectangleF(x, y, width, height);
                            bounds = bounds.IsEmpty ? item : RectangleF.Union(bounds, item);
                        }
                        matches.Add(bounds);
                        completeLabel = start == 0 && count == words.Count;
                    }
                }
                if (matches.Count == 0) return null;
                if (matches.Count > 1) throw new InvalidOperationException("The text appears more than once in this search area. Select a smaller area so MacroDeck can choose safely.");
                // A single prefix is still unsafe: "Shoot Arrow" must not choose
                // "Shoot Arrow II" merely because the requested option is absent.
                if (!completeLabel) return null;
                RectangleF match = matches[0];
                return new VisualRecognitionMatch {
                    Center = new Point(origin.X + (int)Math.Round(match.Left + match.Width / 2f), origin.Y + (int)Math.Round(match.Top + match.Height / 2f)),
                    Bounds = Rectangle.FromLTRB(origin.X+(int)Math.Floor(match.Left),origin.Y+(int)Math.Floor(match.Top),origin.X+(int)Math.Ceiling(match.Right),origin.Y+(int)Math.Ceiling(match.Bottom)),
                    Confidence = Double.NaN, Description = targetText
                };
            }

            private static string NormalizeText(string value)
            {
                if (String.IsNullOrWhiteSpace(value)) return "";
                string normalized = value.Normalize(NormalizationForm.FormKC).ToLowerInvariant();
                StringBuilder result = new StringBuilder(normalized.Length);
                bool space = true;
                foreach (char character in normalized)
                {
                    if (!Char.IsWhiteSpace(character)) { result.Append(character); space = false; }
                    else if (!space) { result.Append(' '); space = true; }
                }
                return Regex.Replace(result.ToString().Trim(), @"\s+", " ");
            }

            public void Dispose()
            {
                DisposeWinRt(engine);
            }
        }

        private static Type WinRtType(string fullName)
        {
            Type type = Type.GetType(fullName + ", Windows, ContentType=WindowsRuntime", false);
            if (type == null) throw new NotSupportedException("Windows OCR APIs are unavailable. Use a supported version of Windows.");
            return type;
        }

        private static MethodInfo FindMethod(Type type, string name, int parameterCount)
        {
            foreach (MethodInfo method in type.GetMethods(BindingFlags.Public | BindingFlags.Static | BindingFlags.Instance))
                if (method.Name == name && method.GetParameters().Length == parameterCount) return method;
            throw new MissingMethodException(type.FullName, name);
        }

        private static Type FindWinRtInterface(Type operationType, string name)
        {
            if (operationType.IsGenericType && operationType.GetGenericTypeDefinition().FullName == name) return operationType;
            foreach (Type contract in operationType.GetInterfaces())
                if (contract.IsGenericType && contract.GetGenericTypeDefinition().FullName == name) return contract;
            throw new NotSupportedException("Windows OCR returned an unsupported asynchronous result.");
        }

        private static object GetProperty(object source, string name)
        {
            if (source == null) return null;
            PropertyInfo property = source.GetType().GetProperty(name, BindingFlags.Public | BindingFlags.Instance);
            return property == null ? null : property.GetValue(source, null);
        }

        private static void DisposeWinRt(object value)
        {
            IDisposable disposable = value as IDisposable;
            if (disposable != null) disposable.Dispose();
        }
    }
}
