using System.Collections.Generic;
using System.Drawing;

namespace EveOPreview.Services.Detection
{
    public class TemplateMatchResult
    {
        public List<TemplateMatch> RedMatches { get; set; } = new();
        public List<TemplateMatch> OrangeMatches { get; set; } = new();
        public List<TemplateMatch> WhiteMatches { get; set; } = new();
        public bool HasAlert { get; set; }
        public int TotalAlertCount => RedMatches.Count + OrangeMatches.Count + WhiteMatches.Count;
        public IEnumerable<TemplateMatch> AllMatches
        {
            get
            {
                foreach (var m in RedMatches) yield return m;
                foreach (var m in OrangeMatches) yield return m;
                foreach (var m in WhiteMatches) yield return m;
            }
        }
    }

    public class TemplateMatch
    {
        public Point Location { get; set; }
        public double Similarity { get; set; }
        public ColorType ColorType { get; set; }
        public string RecognizedText { get; set; } = string.Empty;
        public Rectangle TextRegion { get; set; }
    }

    public enum ColorType
    {
        White,
        Red,
        Orange
    }
}
