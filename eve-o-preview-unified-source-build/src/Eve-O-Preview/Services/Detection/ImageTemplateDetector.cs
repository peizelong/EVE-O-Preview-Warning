using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Reflection;
using System.Threading.Tasks;
using OpenCvSharp;
using OpenCvSharp.Extensions;
using CvPoint = OpenCvSharp.Point;
using SDPoint = System.Drawing.Point;

namespace EveOPreview.Services.Detection
{
    public class ImageTemplateDetector : IDisposable
    {
        private Bitmap _whiteTemplate;
        private Bitmap _redTemplate;
        private Bitmap _orangeTemplate;
        private double _matchThreshold = 0.60;
        private OcrRecognizer _ocrRecognizer;
        private int _textRegionWidth = 150;
        private int _textRegionHeight = 30;
        private bool _disposed;
        // OCR 当前为单独问题，先彻底关掉，避免影响模板匹配链路调试
        private readonly bool _enableOcr = false;

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
                DetectionLog.Write($"[Detect] 红模板匹配开始");
                var matches = FindTemplateMatches(screenshot, _redTemplate, region, ColorType.Red);
                if (_enableOcr && _ocrRecognizer != null && matches.Count > 0)
                {
                    await RecognizeTextForMatches(screenshot, matches);
                }
                result.RedMatches.AddRange(matches);
                DetectionLog.Write($"[Detect] 红模板匹配完成, matches={matches.Count}");
            }

            if (_orangeTemplate != null)
            {
                DetectionLog.Write($"[Detect] 橙模板匹配开始");
                var matches = FindTemplateMatches(screenshot, _orangeTemplate, region, ColorType.Orange);
                if (_enableOcr && _ocrRecognizer != null && matches.Count > 0)
                {
                    await RecognizeTextForMatches(screenshot, matches);
                }
                result.OrangeMatches.AddRange(matches);
                DetectionLog.Write($"[Detect] 橙模板匹配完成, matches={matches.Count}");
            }

            if (_whiteTemplate != null)
            {
                DetectionLog.Write($"[Detect] 白模板匹配开始");
                var matches = FindTemplateMatches(screenshot, _whiteTemplate, region, ColorType.White);
                if (_enableOcr && _ocrRecognizer != null && matches.Count > 0)
                {
                    await RecognizeTextForMatches(screenshot, matches);
                }
                result.WhiteMatches.AddRange(matches);
                DetectionLog.Write($"[Detect] 白模板匹配完成, matches={matches.Count}");
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
            int endX = Math.Min(source.Width, searchRegion.X + searchRegion.Width);
            int endY = Math.Min(source.Height, searchRegion.Y + searchRegion.Height);

            int regionWidth = endX - startX;
            int regionHeight = endY - startY;
            if (regionWidth <= templateWidth || regionHeight <= templateHeight) return matches;

            // OpenCV 模板匹配：统一转灰度后使用归一化相关系数 (CCoeffNormed)
            using var sourceMat = BitmapConverter.ToMat(source);
            using var templateMat = BitmapConverter.ToMat(template);
            using var sourceGray = new Mat();
            using var templateGray = new Mat();
            Cv2.CvtColor(sourceMat, sourceGray, ColorConversionCodes.BGR2GRAY);
            Cv2.CvtColor(templateMat, templateGray, ColorConversionCodes.BGR2GRAY);

            using var searchMat = new Mat(sourceGray, new Rect(startX, startY, regionWidth, regionHeight));
            using var result = new Mat();
            Cv2.MatchTemplate(searchMat, templateGray, result, TemplateMatchModes.CCoeffNormed);

            Cv2.MinMaxLoc(result, out double minVal, out double maxVal, out CvPoint minLoc, out CvPoint maxLoc);
            SDPoint bestPoint = new SDPoint(maxLoc.X + startX, maxLoc.Y + startY);
            double bestSimilarity = maxVal;

            // 网格化非极大值抑制：每个 cell 内只保留最佳匹配点
            int suppressStep = Math.Max(templateWidth, templateHeight) / 2;
            if (suppressStep < 1) suppressStep = 1;
            float threshold = (float)_matchThreshold;

            for (int y = 0; y + suppressStep <= result.Rows; y += suppressStep)
            {
                for (int x = 0; x + suppressStep <= result.Cols; x += suppressStep)
                {
                    double cellBest = double.MinValue;
                    CvPoint cellBestLoc = new CvPoint(0, 0);
                    bool hit = false;

                    int xMax = Math.Min(result.Cols, x + suppressStep);
                    int yMax = Math.Min(result.Rows, y + suppressStep);
                    for (int yy = y; yy < yMax; yy++)
                    {
                        for (int xx = x; xx < xMax; xx++)
                        {
                            double v = result.At<float>(yy, xx);
                            if (v >= threshold && v > cellBest)
                            {
                                cellBest = v;
                                cellBestLoc = new CvPoint(xx, yy);
                                hit = true;
                            }
                        }
                    }

                    if (hit)
                    {
                        matches.Add(new TemplateMatch
                        {
                            Location = new SDPoint(cellBestLoc.X + startX, cellBestLoc.Y + startY),
                            Similarity = cellBest,
                            ColorType = colorType
                        });
                    }
                }
            }

            DetectionLog.Write(
                $"[Template:{colorType}] best={bestSimilarity:F3} at ({bestPoint.X},{bestPoint.Y}), threshold={_matchThreshold:F3}, template={templateWidth}x{templateHeight}, region={searchRegion}, hits={matches.Count}"
            );

            return matches;
        }

        private static unsafe double CalculateSimilarity(byte* sourcePtr, byte* templatePtr,
            int offsetX, int offsetY, int templateWidth, int templateHeight,
            int sourceStride, int templateStride)
        {
            // 已改用 OpenCV 的归一化相关系数匹配 (CCoeffNormed)，保留此方法仅为兼容旧调用方。
            return 0.0;
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
