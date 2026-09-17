using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace PraetorisClient.ServerGuideFeature
{
    internal sealed class GuidePage
    {
        internal readonly string Title;
        internal readonly string Body;
        internal readonly string Section;

        internal GuidePage(string title, string body, string section = "")
        {
            Title = title;
            Body = body;
            Section = section;
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
            HashSet<string> sections = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            string[] lines = text.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n');
            bool grouped = lines.Any(line => line.StartsWith("## ", StringComparison.Ordinal));
            string section = "";
            string? title = null;
            StringBuilder body = new StringBuilder();
            foreach (string line in lines)
            {
                if (grouped && line.StartsWith("# ", StringComparison.Ordinal))
                {
                    if (title != null) AddPage(pages, title, body.ToString(), section);
                    else if (section.Length > 0) throw new FormatException("Each section must contain at least one page.");
                    section = line.Substring(2).Trim();
                    ValidateTitle(section);
                    if (!sections.Add(section)) throw new FormatException("Section names must be unique.");
                    title = null;
                    body.Clear();
                }
                else if (line.StartsWith(grouped ? "## " : "# ", StringComparison.Ordinal))
                {
                    if (grouped && section.Length == 0) throw new FormatException("Start each section with # followed by its name.");
                    if (title != null) AddPage(pages, title, body.ToString(), section);
                    title = line.Substring(grouped ? 3 : 2).Trim();
                    ValidateTitle(title);
                    if (!titles.Add(title))
                        throw new FormatException("Page titles must be unique.");
                    body.Clear();
                }
                else if (title != null)
                    body.AppendLine(line);
                else if (!string.IsNullOrWhiteSpace(line))
                    throw new FormatException("Start each page with a heading. Inside a section, use ## followed by the page title.");
            }
            if (title != null)
                AddPage(pages, title, body.ToString(), section);
            else if (section.Length > 0) throw new FormatException("Each section must contain at least one page.");
            GuideMarkup.ValidateLinks(pages);
            return pages;
        }

        private static void ValidateTitle(string title)
        {
            if (title.Length == 0 || title.Length > 80 || title.IndexOfAny(new[] { '<', '>', '$', '[', ']', '|' }) >= 0)
                throw new FormatException("Page and section titles must contain 1–80 characters and cannot contain <, >, $, [, ] or |.");
        }

        private static void AddPage(List<GuidePage> pages, string title, string body, string section)
        {
            if (pages.Count >= MaximumPages)
                throw new FormatException("The guide can contain at most 64 pages.");
            if (body.Length > 16000)
                throw new FormatException("Each page body must contain at most 16000 characters.");
            pages.Add(new GuidePage(title, body.Trim(), section));
        }
    }
}
