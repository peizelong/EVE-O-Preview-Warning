using System.Drawing;
using System.Drawing.Imaging;
using System.Reflection;

namespace EVEAutoWarning.Core;

public class ImageTemplateDetector
{
    private Bitmap _whiteTemplate;
    private Bitmap _redTemplate;
    private Bitmap _orangeTemplate;
    private double _matchThreshold = 0.85;

    public double MatchThreshold
    {
        get => _matchThreshold;
        set => _matchThreshold = Math.Clamp(value, 0.5, 1.0);
    }

    public ImageTemplateDetector()
    {
        LoadEmbeddedTemplates();
    }

    private void LoadEmbeddedTemplates()
    {
        var assembly = Assembly.GetExecutingAssembly();
        
        _whiteTemplate = LoadTemplateFromResource(assembly, "EVEAutoWarning.Templates.白.png");
        _redTemplate = LoadTemplateFromResource(assembly, "EVEAutoWarning.Templates.红.png");
        _orangeTemplate = LoadTemplateFromResource(assembly, "EVEAutoWarning.Templates.橙.png");
    }

    private Bitmap LoadTemplateFromResource(Assembly assembly, string resourceName)
    {
        try
        {
            using var stream = assembly.GetManifestResourceStream(resourceName);
            if (stream != null)
            {
                return new Bitmap(stream);
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"加载嵌入资源失败 {resourceName}: {ex.Message}");
        }
        return null;
    }

    public void LoadTemplates(string whitePath, string redPath, string orangePath)
    {
        if (File.Exists(whitePath))
            _whiteTemplate = new Bitmap(whitePath);
        if (File.Exists(redPath))
            _redTemplate = new Bitmap(redPath);
        if (File.Exists(orangePath))
            _orangeTemplate = new Bitmap(orangePath);
    }

    public void LoadTemplatesFromDirectory(string directory)
    {
        string whitePath = Path.Combine(directory, "白.png");
        string redPath = Path.Combine(directory, "红.png");
        string orangePath = Path.Combine(directory, "橙.png");

        LoadTemplates(whitePath, redPath, orangePath);
    }

    public bool HasTemplatesLoaded()
    {
        return _whiteTemplate != null || _redTemplate != null || _orangeTemplate != null;
    }

    public TemplateMatchResult DetectInRegion(Bitmap screenshot, Rectangle region)
    {
        var result = new TemplateMatchResult();

        if (_redTemplate != null)
        {
            var matches = FindTemplateMatches(screenshot, _redTemplate, region, ColorType.Red);
            result.RedMatches.AddRange(matches);
        }

        if (_orangeTemplate != null)
        {
            var matches = FindTemplateMatches(screenshot, _orangeTemplate, region, ColorType.Orange);
            result.OrangeMatches.AddRange(matches);
        }

        if (_whiteTemplate != null)
        {
            var matches = FindTemplateMatches(screenshot, _whiteTemplate, region, ColorType.White);
            result.WhiteMatches.AddRange(matches);
        }

        result.HasAlert = result.RedMatches.Count > 0 || result.OrangeMatches.Count > 0 || result.WhiteMatches.Count > 0;

        return result;
    }

    private List<TemplateMatch> FindTemplateMatches(Bitmap source, Bitmap template, Rectangle searchRegion, ColorType colorType)
    {
        var matches = new List<TemplateMatch>();

        int templateWidth = template.Width;
        int templateHeight = template.Height;

        int startX = Math.Max(0, searchRegion.X);
        int startY = Math.Max(0, searchRegion.Y);
        int endX = Math.Min(source.Width - templateWidth, searchRegion.X + searchRegion.Width - templateWidth);
        int endY = Math.Min(source.Height - templateHeight, searchRegion.Y + searchRegion.Height - templateHeight);

        if (endX <= startX || endY <= startY)
            return matches;

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

    private unsafe double CalculateSimilarity(byte* sourcePtr, byte* templatePtr,
        int offsetX, int offsetY, int templateWidth, int templateHeight,
        int sourceStride, int templateStride)
    {
        long totalDiff = 0;
        int pixelCount = templateWidth * templateHeight;

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
        double similarity = 1.0 - (avgDiff / maxDiff);

        return similarity;
    }

    public void Dispose()
    {
        _whiteTemplate?.Dispose();
        _redTemplate?.Dispose();
        _orangeTemplate?.Dispose();
    }
}

public class TemplateMatchResult
{
    public List<TemplateMatch> RedMatches { get; set; } = new();
    public List<TemplateMatch> OrangeMatches { get; set; } = new();
    public List<TemplateMatch> WhiteMatches { get; set; } = new();
    public bool HasAlert { get; set; }

    public int TotalAlertCount => RedMatches.Count + OrangeMatches.Count + WhiteMatches.Count;
}

public class TemplateMatch
{
    public Point Location { get; set; }
    public double Similarity { get; set; }
    public ColorType ColorType { get; set; }
}

public enum ColorType
{
    White,
    Red,
    Orange
}
