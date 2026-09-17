using System;
using System.Globalization;
using System.Text.RegularExpressions;

namespace PraetorisClient.ServerGuideFeature
{
    internal static class GuideConfigText
    {
        private static readonly Regex Reference = new Regex(@"\{\{([^{}\r\n]+)\}\}");

        internal static string Expand(string source, Func<string, object> resolve)
        {
            string[] lines = source.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n');
            bool grouped = GuideDocument.SectionContentStart(lines) > 0;
            foreach (string line in lines)
                if ((line.StartsWith("# ", StringComparison.Ordinal) || (grouped && line.StartsWith("## ", StringComparison.Ordinal))) && Reference.IsMatch(line))
                    throw new FormatException("Use config references in page bodies, not page titles.");

            return Reference.Replace(source, match =>
            {
                string[] parts = match.Groups[1].Value.Split('|');
                string key = parts[0].Trim();
                string format = parts.Length == 2 ? parts[1].Trim() : "";
                if (key.Length == 0 || parts.Length > 2 || (format != "" && format != "percent"))
                    throw new FormatException("Use {{Mod.Setting}} or {{Mod.Setting|percent}} for config references.");
                object value = resolve(key) ?? throw new FormatException($"Config reference '{key}' has no value.");
                string text;
                if (format == "percent")
                {
                    TypeCode type = Type.GetTypeCode(value.GetType());
                    if (type < TypeCode.SByte || type > TypeCode.Decimal)
                        throw new FormatException($"Config reference '{key}' needs a numeric value for percent formatting.");
                    double number = Convert.ToDouble(value, CultureInfo.InvariantCulture);
                    number *= 100;
                    if (double.IsNaN(number) || double.IsInfinity(number))
                        throw new FormatException($"Config reference '{key}' is not a finite number.");
                    text = number.ToString("0.##", CultureInfo.InvariantCulture) + "%";
                }
                else text = Convert.ToString(value, CultureInfo.InvariantCulture) ?? "";
                if (text.Length > 512)
                    throw new FormatException($"Config reference '{key}' exceeds 512 characters.");
                // Values are plain text, never guide links, page headings, or rich text.
                return GuideMarkup.Escape(text).Replace('[', '［').Replace(']', '］').Replace('#', '＃')
                    .Replace('\r', ' ').Replace('\n', ' ');
            });
        }
    }
}
