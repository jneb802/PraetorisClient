using System;
using System.Collections.Generic;
using System.Text;

namespace PraetorisClient.ServerGuideFeature
{
    internal sealed class GuidePage
    {
        internal readonly string Title;
        internal readonly string Body;

        internal GuidePage(string title, string body)
        {
            Title = title;
            Body = body;
        }
    }

    internal static class GuideDocument
    {
        internal const int MaximumBytes = 128 * 1024;
        internal const int MaximumPages = 64;

        internal static List<GuidePage> Parse(string text)
        {
            if (Encoding.UTF8.GetByteCount(text) > MaximumBytes)
                throw new FormatException("The guide must be at most 128 KiB.");

            List<GuidePage> pages = new List<GuidePage>();
            HashSet<string> titles = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            string? title = null;
            StringBuilder body = new StringBuilder();
            foreach (string line in text.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n'))
            {
                if (line.StartsWith("# ", StringComparison.Ordinal))
                {
                    if (title != null)
                        AddPage(pages, title, body.ToString());
                    title = line.Substring(2).Trim();
                    if (title.Length == 0 || title.Length > 80 || title.IndexOfAny(new[] { '<', '>', '$', '[', ']', '|' }) >= 0)
                        throw new FormatException("Page titles must contain 1–80 characters and cannot contain <, >, $, [, ] or |.");
                    if (!titles.Add(title))
                        throw new FormatException("Page titles must be unique.");
                    body.Clear();
                }
                else if (title != null)
                    body.AppendLine(line);
                else if (!string.IsNullOrWhiteSpace(line))
                    throw new FormatException("Start each page with # followed by a space and its title.");
            }
            if (title != null)
                AddPage(pages, title, body.ToString());
            GuideMarkup.ValidateLinks(pages);
            return pages;
        }

        private static void AddPage(List<GuidePage> pages, string title, string body)
        {
            if (pages.Count >= MaximumPages)
                throw new FormatException("The guide can contain at most 64 pages.");
            if (body.Length > 16000)
                throw new FormatException("Each page body must contain at most 16000 characters.");
            pages.Add(new GuidePage(title, body.Trim()));
        }
    }
}
