using System;
using System.Linq;
using System.Text.RegularExpressions;

namespace PraetorisClient.ServerGuideFeature
{
    internal static class GuideSearch
    {
        private static readonly Regex Tags = new Regex("<[^>]*>");

        internal static string Text(GuidePage page) => page.Section + "\n" + page.Title + "\n" +
            string.Join("\n", GuideMarkup.Parse(page.Body).Select(block => Tags.Replace(block.Text, "")));

        internal static bool Matches(string text, string query) =>
            query.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries)
                .All(word => text.IndexOf(word, StringComparison.OrdinalIgnoreCase) >= 0);
    }
}
