using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

namespace PraetorisClient.ServerGuideFeature
{
    internal sealed class GuideBlock
    {
        internal string Text = "";
        internal string Image = "";
        internal readonly List<string> Links = new List<string>();
    }

    internal static class GuideMarkup
    {
        private static readonly Regex ImageLine = new Regex(@"^!\[([^\]]*)\]\(([a-zA-Z0-9_-][a-zA-Z0-9_.-]*\.png)\)$", RegexOptions.IgnoreCase);
        private static readonly Regex PageLink = new Regex(@"\[\[([^\]\r\n]+)\]\]");

        internal static List<GuideBlock> Parse(string body)
        {
            List<GuideBlock> blocks = new List<GuideBlock>();
            StringBuilder paragraph = new StringBuilder();
            foreach (string line in body.Replace("\r\n", "\n").Split('\n'))
            {
                Match match = ImageLine.Match(line.Trim());
                if (match.Success)
                {
                    Flush();
                    string name = match.Groups[2].Value;
                    if (name.Length > 100 || name.Contains(".."))
                        throw new FormatException("Use a PNG filename without folders or .. in image references.");
                    blocks.Add(new GuideBlock { Text = match.Groups[1].Value, Image = name });
                }
                else if (line.TrimStart().StartsWith("![", StringComparison.Ordinal))
                    throw new FormatException("Put each image on its own line: ![Caption](image.png).");
                else
                    paragraph.AppendLine(line);
            }
            Flush();
            if (blocks.Count(block => block.Image.Length > 0) > 16)
                throw new FormatException("Each page can contain at most 16 image blocks.");
            return blocks;

            void Flush()
            {
                string text = paragraph.ToString().Trim();
                paragraph.Clear();
                if (text.Length == 0) return;
                GuideBlock block = new GuideBlock();
                block.Text = PageLink.Replace(text, match =>
                {
                    string[] parts = match.Groups[1].Value.Split(new[] { '|' }, 2);
                    string target = parts[0].Trim();
                    string label = parts.Length > 1 ? parts[1].Trim() : target;
                    if (target.Length == 0 || label.Length == 0)
                        throw new FormatException("Page links need a title and a nonempty label.");
                    int index = block.Links.Count;
                    block.Links.Add(target);
                    return $"<link=\"{index}\"><color=#F4BF62><u>{Escape(label)}</u></color></link>";
                });
                blocks.Add(block);
            }
        }

        internal static IEnumerable<string> Images(IEnumerable<GuidePage> pages) =>
            pages.SelectMany(page => Parse(page.Body)).Where(block => block.Image.Length > 0)
                .Select(block => block.Image).Distinct(StringComparer.Ordinal);

        internal static void ValidateLinks(List<GuidePage> pages)
        {
            HashSet<string> titles = new HashSet<string>(pages.Select(page => page.Title), StringComparer.OrdinalIgnoreCase);
            foreach (GuidePage page in pages)
                foreach (string link in Parse(page.Body).SelectMany(block => block.Links))
                    if (!titles.Contains(link))
                        throw new FormatException($"Page '{page.Title}' links to an unknown page: {link}.");
        }

        internal static string Escape(string text) => text.Replace("<", "＜").Replace(">", "＞");
    }
}
