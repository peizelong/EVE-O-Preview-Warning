using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Reflection;
using System.Threading.Tasks;

namespace EveOPreview.Services.Detection
{
    public class ImageTemplateDetector : IDisposable
    {
        private Bitmap _whiteTemplate;
        private Bitmap _redTemplate;
        private Bitmap _orangeTemplate;
        private double _matchThreshold = 0.85;
        private OcrRecognizer _ocrRecognizer;
        private int _textRegionWidth = 150;
        private int _textRegionHeight = 30;
        private bool _disposed;

        public double MatchThreshold
        {
            get => _matchThreshold;
            set => _matchThreshold = Math.Clamp(value, 0.5, 1.0);
        }

        public int TextRegionWidth
        {
            get => _textRegionWidth;
            set => _textRegionWidth = Math.Max(50, Math.Min(500, value));
        }

        public int TextRegionHeight
        {
            get => _textRegionHeight;
            set => _textRegionHeight = Math.Max(15, Math.Min(100, value));
        }

        public OcrRecognizer Ocr => _ocrRecognizer;

        public ImageTemplateDetector()
        {
            LoadTemplatesFromDefaultDirectory();
            // 如果文件系统加载失败，回退到嵌入式资源
            if (!HasTemplatesLoaded())
            {
                LoadTemplatesFromEmbeddedResources();
            }
            try
            {
                _ocrRecognizer = new OcrRecognizer();
            }
            catch (Exception ex)
            {
                System.Console.WriteLine($"[ImageTemplateDetector] OCR 初始化失败: {ex.Message}");
                _ocrRecognizer = null;
            }
        }

        private void LoadTemplatesFromDefaultDirectory()
        {
            var dir = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Templates");
            LoadTemplatesFromDirectory(dir);
        }

        private void LoadTemplatesFromEmbeddedResources()
        {
            var assembly = Assembly.GetExecutingAssembly();
            LoadEmbeddedTemplate(assembly, "EveOPreview.Templates.白.png", b => _whiteTemplate = b);
            LoadEmbeddedTemplate(assembly, "EveOPreview.Templates.红.png", b => _redTemplate = b);
            LoadEmbeddedTemplate(assembly, "EveOPreview.Templates.橙.png", b => _orangeTemplate = b);
        }

        private static void LoadEmbeddedTemplate(Assembly assembly, string resourceName, Action<Bitmap> assign)
        {
            try
            {
                using var stream = assembly.GetManifestResourceStream(resourceName);
                if (stream != null)
                {
                    assign(new Bitmap(stream));
                }
            }
            catch (Exception ex)
            {
                System.Console.WriteLine($"[ImageTemplateDetector] 加载嵌入式模板失败 {resourceName}: {ex.Message}");
            }
        }

        private void LoadTemplateFromFile(string path, Action<Bitmap> assign)
        {
            try
            {
                if (File.Exists(path))
                {
                    assign(new Bitmap(path));
                }
            }
            catch (Exception ex)
            {
                System.Console.WriteLine($"[ImageTemplateDetector] 加载模板失败 {path}: {ex.Message}");
            }
        }

        public void LoadTemplates(string whitePath, string redPath, string orangePath)
        {
            LoadTemplateFromFile(whitePath,  b => _whiteTemplate  = b);
            LoadTemplateFromFile(redPath,    b => _redTemplate    = b);
            LoadTemplateFromFile(orangePath, b => _orangeTemplate = b);
        }

        public void LoadTemplatesFromDirectory(string directory)
        {
            if (string.IsNullOrEmpty(directory) || !Directory.Exists(directory)) return;
            LoadTemplates(
                Path.Combine(directory, "白.png"),
                Path.Combine(directory, "红.png"),
                Path.Combine(directory, "橙.png"));
        }

        public bool HasTemplatesLoaded()
        {
            return _whiteTemplate != null || _redTemplate != null || _orangeTemplate != null;
        }

        public bool HasTemplate(ColorType color)
        {
            return color switch
            {
                ColorType.Red => _redTemplate != null,
                ColorType.Orange => _orangeTemplate != null,
                ColorType.White => _whiteTemplate != null,
                _ => false
            };
        }

        public async Task<TemplateMatchResult> DetectInRegionAsync(Bitmap screenshot, Rectangle region)
        {
            var result = new TemplateMatchResult();

            if (_redTemplate != null)
            {
                var matches = FindTemplateMatches(screenshot, _redTemplate, region, ColorType.Red);
                if (_ocrRecognizer != null) await RecognizeTextForMatches(screenshot, matches);
                result.RedMatches.AddRange(matches);
            }

            if (_orangeTemplate != null)
            {
                var matches = FindTemplateMatches(screenshot, _orangeTemplate, region, ColorType.Orange);
                if (_ocrRecognizer != null) await RecognizeTextForMatches(screenshot, matches);
                result.OrangeMatches.AddRange(matches);
            }

            if (_whiteTemplate != null)
            {
                var matches = FindTemplateMatches(screenshot, _whiteTemplate, region, ColorType.White);
                if (_ocrRecognizer != null) await RecognizeTextForMatches(screenshot, matches);
                result.WhiteMatches.AddRange(matches);
            }

            result.HasAlert = result.RedMatches.Count > 0 || result.OrangeMatches.Count > 0 || result.WhiteMatches.Count > 0;
            return result;
        }

        private async Task RecognizeTextForMatches(Bitmap screenshot, List<TemplateMatch> matches)
        {
            foreach (var match in matches)
            {
                int templateWidth = GetTemplateWidth(match.ColorType) ?? 20;
                int textX = match.Location.X + templateWidth + 5;
                int textY = match.Location.Y + 2;

                textX = Math.Max(0, textX);
                textY = Math.Max(0, textY);

                int textWidth = Math.Min(_textRegionWidth, screenshot.Width - textX);
                int textHeight = Math.Min(_textRegionHeight - 4, screenshot.Height - textY);

                if (textWidth > 0 && textHeight > 0)
                {
                    var textRegion = new Rectangle(textX, textY, textWidth, textHeight);
                    match.TextRegion = textRegion;
                    try
                    {
                        string rawText = await _ocrRecognizer.RecognizeTextAsync(screenshot, textRegion);
                        match.RecognizedText = CleanRecognizedText(rawText);
                    }
                    catch (Exception ex)
                    {
                        System.Console.WriteLine($"[ImageTemplateDetector] OCR 失败: {ex.Message}");
                    }
                }
            }
        }

        private static string CleanRecognizedText(string text)
        {
            if (string.IsNullOrWhiteSpace(text)) return string.Empty;
            text = text.Trim();
            text = System.Text.RegularExpressions.Regex.Replace(text, @"^[^\w]+", "");
            text = System.Text.RegularExpressions.Regex.Replace(text, @"[^\w\s\-]", "");
            text = System.Text.RegularExpressions.Regex.Replace(text, @"\s+", " ").Trim();
            return text;
        }

        private int? GetTemplateWidth(ColorType colorType)
        {
            return colorType switch
            {
                ColorType.Red => _redTemplate?.Width,
                ColorType.Orange => _orangeTemplate?.Width,
                ColorType.White => _whiteTemplate?.Width,
                _ => null
            };
        }

        public TemplateMatchResult DetectInRegion(Bitmap screenshot, Rectangle region)
        {
            var result = new TemplateMatchResult();
            if (_redTemplate != null)    result.RedMatches.AddRange(FindTemplateMatches(screenshot, _redTemplate,    region, ColorType.Red));
            if (_orangeTemplate != null) result.OrangeMatches.AddRange(FindTemplateMatches(screenshot, _orangeTemplate, region, ColorType.Orange));
            if (_whiteTemplate != null)  result.WhiteMatches.AddRange(FindTemplateMatches(screenshot, _whiteTemplate,  region, ColorType.White));
            result.HasAlert = result.RedMatches.Count > 0 || result.OrangeMatches.Count > 0 || result.WhiteMatches.Count > 0;
            return result;
        }

        private List<TemplateMatch> FindTemplateMatches(Bitmap source, Bitmap template, Rectangle searchRegion, ColorType colorType)
        {
            var matches = new List<TemplateMatch>();
            if (source == null || template == null) return matches;

            int templateWidth = template.Width;
            int templateHeight = template.Height;

            int startX = Math.Max(0, searchRegion.X);
            int startY = Math.Max(0, searchRegion.Y);
            int endX = Math.Min(source.Width - templateWidth, searchRegion.X + searchRegion.Width - templateWidth);
            int endY = Math.Min(source.Height - templateHeight, searchRegion.Y + searchRegion.Height - templateHeight);

            if (endX <= startX || endY <= startY) return matches;

            BitmapData sourceData = source.LockBits(
                new Rectangle(0, 0, source.Width, source.Height),
                ImageLockMode.ReadOnly,
                PixelFormat.Format32bppArgb);

            BitmapData templateData = template.LockBits(
                new Rectangle(0, 0, templateWidth, templateHeight),
                ImageLockMode.ReadOnly,
                PixelFormat.Format32bppArgb);

            try
            {
                unsafe
                {
                    byte* sourcePtr = (byte*)sourceData.Scan0;
                    byte* templatePtr = (byte*)templateData.Scan0;
                    int sourceStride = sourceData.Stride;
                    int templateStride = templateData.Stride;
                    int step = Math.Max(templateWidth / 4, 2);

                    for (int y = startY; y < endY; y += step)
                    {
                        for (int x = startX; x < endX; x += step)
                        {
                            double similarity = CalculateSimilarity(sourcePtr, templatePtr,
                                x, y, templateWidth, templateHeight,
                                sourceStride, templateStride);

                            if (similarity >= _matchThreshold)
                            {
                                matches.Add(new TemplateMatch
                                {
                                    Location = new Point(x, y),
                                    Similarity = similarity,
                                    ColorType = colorType
                                });
                                x += templateWidth;
                            }
                        }
                    }
                }
            }
            finally
            {
                source.UnlockBits(sourceData);
                template.UnlockBits(templateData);
            }
            return matches;
        }

        private static unsafe double CalculateSimilarity(byte* sourcePtr, byte* templatePtr,
            int offsetX, int offsetY, int templateWidth, int templateHeight,
            int sourceStride, int templateStride)
        {
            long totalDiff = 0;
            for (int ty = 0; ty < templateHeight; ty += 2)
            {
                for (int tx = 0; tx < templateWidth; tx += 2)
                {
                    int sourceOffset = (offsetY + ty) * sourceStride + (offsetX + tx) * 4;
                    int templateOffset = ty * templateStride + tx * 4;
                    byte sb = sourcePtr[sourceOffset];
                    byte sg = sourcePtr[sourceOffset + 1];
                    byte sr = sourcePtr[sourceOffset + 2];
                    byte tb = templatePtr[templateOffset];
                    byte tg = templatePtr[templateOffset + 1];
                    byte tr = templatePtr[templateOffset + 2];
                    int diff = Math.Abs(sr - tr) + Math.Abs(sg - tg) + Math.Abs(sb - tb);
                    totalDiff += diff;
                }
            }
            int sampledPixels = (templateWidth / 2) * (templateHeight / 2);
            double avgDiff = (double)totalDiff / sampledPixels;
            double maxDiff = 255 * 3;
            return 1.0 - (avgDiff / maxDiff);
        }

        public void Dispose()
        {
            if (_disposed) return;
            _whiteTemplate?.Dispose();
            _redTemplate?.Dispose();
            _orangeTemplate?.Dispose();
            _ocrRecognizer?.Dispose();
            _disposed = true;
            GC.SuppressFinalize(this);
        }
    }
}
