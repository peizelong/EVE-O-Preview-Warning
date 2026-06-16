using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Threading.Tasks;
using Tesseract;
using SysImgFmt = System.Drawing.Imaging.ImageFormat;

namespace EveOPreview.Services.Detection
{
    public class OcrRecognizer : IDisposable
    {
        private TesseractEngine _engine;
        private bool _disposed;
        private bool _initialized;
        private string _tessDataPath;
        private string _language;

        public bool IsInitialized => _initialized;

        public OcrRecognizer(string tessDataPath = null, string language = "eng")
        {
            _tessDataPath = tessDataPath;
            _language = language;
            InitializeEngine();
        }

        private void InitializeEngine()
        {
            try
            {
                string tessPath = _tessDataPath;
                if (string.IsNullOrEmpty(tessPath))
                {
                    string exeDir = AppDomain.CurrentDomain.BaseDirectory;
                    tessPath = Path.Combine(exeDir, "tessdata");
                    if (!Directory.Exists(tessPath)) tessPath = exeDir;
                }

                if (Directory.Exists(tessPath))
                {
                    string langFile = Path.Combine(tessPath, $"{_language}.traineddata");
                    if (File.Exists(langFile))
                    {
                        _engine = new TesseractEngine(tessPath, _language, EngineMode.Default);
                        _initialized = true;
                        System.Console.WriteLine($"[OCR] Tesseract 初始化成功,语言: {_language}");
                    }
                    else
                    {
                        System.Console.WriteLine($"[OCR] 未找到语言文件: {langFile}");
                        _initialized = false;
                    }
                }
                else
                {
                    System.Console.WriteLine($"[OCR] tessdata 目录不存在: {tessPath}");
                    _initialized = false;
                }
            }
            catch (Exception ex)
            {
                System.Console.WriteLine($"[OCR] 初始化失败: {ex.Message}");
                _initialized = false;
            }
        }

        public async Task<string> RecognizeTextAsync(Bitmap bitmap, Rectangle region)
        {
            if (!_initialized || _engine == null || bitmap == null) return string.Empty;
            return await Task.Run(() =>
            {
                try
                {
                    using var regionBitmap = CropBitmap(bitmap, region);
                    if (regionBitmap == null) return string.Empty;
                    string tempFile = Path.Combine(Path.GetTempPath(), $"ocr_temp_{Guid.NewGuid()}.png");
                    try
                    {
                        regionBitmap.Save(tempFile, SysImgFmt.Png);
                        using var img = Pix.LoadFromFile(tempFile);
                        using var page = _engine.Process(img);
                        return page.GetText()?.Trim() ?? string.Empty;
                    }
                    finally
                    {
                        if (File.Exists(tempFile))
                        {
                            try { File.Delete(tempFile); } catch { /* ignore */ }
                        }
                    }
                }
                catch (Exception ex)
                {
                    System.Console.WriteLine($"[OCR] 识别失败: {ex.Message}");
                    return string.Empty;
                }
            });
        }

        public async Task<List<OcrTextResult>> RecognizeTextsWithPositionsAsync(Bitmap bitmap, Rectangle searchRegion)
        {
            var results = new List<OcrTextResult>();
            if (!_initialized || _engine == null || bitmap == null) return results;
            await Task.Run(() =>
            {
                try
                {
                    using var regionBitmap = CropBitmap(bitmap, searchRegion);
                    if (regionBitmap == null) return;
                    string tempFile = Path.Combine(Path.GetTempPath(), $"ocr_temp_{Guid.NewGuid()}.png");
                    try
                    {
                        regionBitmap.Save(tempFile, SysImgFmt.Png);
                        using var img = Pix.LoadFromFile(tempFile);
                        using var page = _engine.Process(img);
                        using var iterator = page.GetIterator();
                        iterator.Begin();
                        do
                        {
                            if (iterator.TryGetBoundingBox(PageIteratorLevel.TextLine, out var bounds))
                            {
                                string text = iterator.GetText(PageIteratorLevel.TextLine);
                                if (!string.IsNullOrWhiteSpace(text))
                                {
                                    results.Add(new OcrTextResult
                                    {
                                        Text = text.Trim(),
                                        BoundingBox = new Rectangle(
                                            searchRegion.X + bounds.X1,
                                            searchRegion.Y + bounds.Y1,
                                            bounds.X2 - bounds.X1,
                                            bounds.Y2 - bounds.Y1)
                                    });
                                }
                            }
                        } while (iterator.Next(PageIteratorLevel.TextLine));
                    }
                    finally
                    {
                        if (File.Exists(tempFile))
                        {
                            try { File.Delete(tempFile); } catch { /* ignore */ }
                        }
                    }
                }
                catch (Exception ex)
                {
                    System.Console.WriteLine($"[OCR] 批量识别失败: {ex.Message}");
                }
            });
            return results;
        }

        private static Bitmap CropBitmap(Bitmap source, Rectangle region)
        {
            if (region.Width <= 0 || region.Height <= 0) return null;
            region.X = Math.Max(0, region.X);
            region.Y = Math.Max(0, region.Y);
            region.Width = Math.Min(region.Width, source.Width - region.X);
            region.Height = Math.Min(region.Height, source.Height - region.Y);
            if (region.Width <= 0 || region.Height <= 0) return null;
            var cropped = new Bitmap(region.Width, region.Height);
            using (var g = Graphics.FromImage(cropped))
            {
                g.DrawImage(source, new Rectangle(0, 0, region.Width, region.Height), region, GraphicsUnit.Pixel);
            }
            return cropped;
        }

        public void Dispose()
        {
            if (_disposed) return;
            _engine?.Dispose();
            _disposed = true;
            GC.SuppressFinalize(this);
        }
    }

    public class OcrTextResult
    {
        public string Text { get; set; }
        public Rectangle BoundingBox { get; set; }
    }
}
