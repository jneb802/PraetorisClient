using System;
using System.Collections.Generic;
using System.Linq;
using PraetorisClient.ServerGuideFeature;

List<GuidePage> pages = GuideDocument.Parse("# Welcome\r\nHello\r\n\r\n# Rules\r\nFirst rule\r\nSecond rule\r\n");
Check(pages.Count == 2 && pages[0].Title == "Welcome" && pages[1].Title == "Rules", "page order");
Check(pages[1].Body.Replace("\r\n", "\n") == "First rule\nSecond rule", "line breaks");
Check(GuideDocument.Parse(" \n").Count == 0, "empty guide disables pages");
Check(GuideDocument.Parse("# 日本語\nWelcome 🛡")[0].Body == "Welcome 🛡", "Unicode");
Check(GuideDocument.Parse("# Intro\n## Section\nBody")[0].Body.Contains("## Section"), "only top-level headings split pages");
Reject("Missing heading");
Reject("# \nBody");
Reject("# One\n# one\n");
Reject("# <b>Title</b>\nBody");
Reject("# $token\nBody");
Reject("# " + new string('a', 81));
Reject("# Long\n" + new string('a', 16001));
Reject(string.Join("\n", Enumerable.Range(0, 65).Select(i => "# Page " + i)));
Reject(new string('界', 50000));
Check(GuideDocument.Parse(string.Join("\n", Enumerable.Range(0, 64).Select(i => "# Page " + i))).Count == 64, "maximum page count");
GuideDocument.Parse("# Welcome\nRead [[Rules|the rules]].\n# Rules\nReturn to [[welcome]].");
Reject("# Welcome\n[[Missing page]]");
Reject("# Welcome\n[[Welcome| ]]");
Reject("# A|B\nTitle uses a reserved character");
Reject("# Welcome\n![Map](../outside.png)");
Reject("# Welcome\n![Map](https://example.com/map.png)");
Reject("# Welcome\n![Map](map.jpg)");
Reject("# Welcome\n" + string.Join("\n", Enumerable.Repeat("![Map](map.png)", 17)));
List<GuideBlock> blocks = GuideMarkup.Parse("First [[Rules|<b>rules</b>]]\n\n![Our map](map.png)\n\nLast paragraph");
Check(blocks.Count == 3 && blocks[1].Image == "map.png" && blocks[1].Text == "Our map", "image block order and caption");
Check(blocks[0].Links.Single() == "Rules" && blocks[0].Text.Contains("＜b＞rules＜/b＞"), "safe custom link label");
Check(GuideMarkup.Images(GuideDocument.Parse("# One\n![Map](map.png)\n# Two\n![Map](map.png)")).Count() == 1, "shared image deduplication");

GuideHistory history = new GuideHistory();
history.Visit("Welcome", 1f);
Check(!history.CanBack && !history.CanForward, "initial history");
history.Visit("Rules", 0.4f);
Check(history.Move(-1, 0.7f)?.Title == "Welcome" && history.Current?.Scroll == 0.4f, "back restores scroll");
Check(history.Move(1, 1f)?.Title == "Rules" && history.Current?.Scroll == 0.7f, "forward restores scroll");
history.Move(-1, 1f);
history.Visit("Building", 1f);
Check(!history.CanForward && history.CanBack, "new visit discards forward branch");
history.Visit("building", 0.5f);
Check(history.Move(-1, 1f)?.Title == "Welcome", "same-page visit does not duplicate history");
for (int index = 0; index < 150; index++) history.Visit(index.ToString(), 1f);
int steps = 0;
while (history.Move(-1, 1f) != null) steps++;
Check(steps == 99, "bounded history");
history.Clear();
Check(history.Current == null && !history.CanBack && !history.CanForward, "history cleanup");
history.Visit("Welcome", 1f);
history.Visit("Rules", 0.5f);
history.Prune(new[] { "Rules" });
Check(history.Current?.Title == "Rules" && !history.CanBack, "removed pages leave history");
history.Prune(Array.Empty<string>());
Check(history.Current == null, "removed current page clears history");

byte[] png = Convert.FromBase64String("iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAIAAACQd1PeAAAADElEQVR4nGP4z8AAAAMBAQDJ/pLvAAAAAElFTkSuQmCC");
GuideImageData.Validate(png);
RejectImage(png.Take(33).ToArray());
byte[] corrupt = (byte[])png.Clone();
corrupt[45] ^= 1;
RejectImage(corrupt);
byte[] oversized = (byte[])png.Clone();
oversized[16] = 1;
RejectImage(oversized);
RejectImage(new byte[GuideImageData.MaximumBytes + 1]);
Check(GuideImageData.Digest(png) != GuideImageData.Digest(corrupt), "image digest detects changes");
string searchText = GuideSearch.Text(new GuidePage("Building rules", "Use <b>stone</b>. Read [[Welcome|the introduction]].\n![日本語 map](private-file.png)"));
Check(GuideSearch.Matches(searchText, "BUILDING stone"), "search combines title and body without case sensitivity");
Check(GuideSearch.Matches(searchText, "  introduction\t日本語  "), "search covers link labels, captions, and Unicode");
Check(GuideSearch.Matches(searchText, " \t "), "empty search includes every page");
Check(!GuideSearch.Matches(searchText, "stone missing"), "every search word must match");
Check(!GuideSearch.Matches(searchText, "private-file") && !GuideSearch.Matches(searchText, "<b>"), "search excludes image filenames and rich-text tags");
Dictionary<string, object> settings = new Dictionary<string, object>
{
    ["StarLevelSystem.EnemyDamageLevelMultiplier"] = 0.125f,
    ["Mod.Enabled"] = true,
    ["Mod.Text"] = "# Injected\n<b>text</b> [[Rules]]",
    ["Mod.Huge"] = new string('x', 513)
};
string configSource = "# Rules\nDamage: {{StarLevelSystem.EnemyDamageLevelMultiplier|percent}} ({{StarLevelSystem.EnemyDamageLevelMultiplier}}). {{Mod.Enabled}}";
string configText = GuideConfigText.Expand(configSource, key => settings[key]);
Check(configText.Contains("12.5% (0.125). True"), "config values and percent formatting");
settings["StarLevelSystem.EnemyDamageLevelMultiplier"] = 0.2f;
Check(GuideConfigText.Expand(configSource, key => settings[key]).Contains("20%"), "config changes update unchanged source");
string safeConfig = GuideConfigText.Expand("# Rules\n{{Mod.Text}}", key => settings[key]);
Check(GuideDocument.Parse(safeConfig).Count == 1 && !safeConfig.Contains("<b>") && !safeConfig.Contains("[["), "config values cannot add pages, links, or rich text");
RejectConfig("# {{Mod.Enabled}}\nBody", settings);
RejectConfig("# Rules\n{{Mod.Enabled|percent}}", settings);
RejectConfig("# Rules\n{{Mod.Enabled|unknown}}", settings);
RejectConfig("# Rules\n{{Mod.Huge}}", settings);
settings["Mod.Number"] = double.MaxValue;
RejectConfig("# Rules\n{{Mod.Number|percent}}", settings);
settings["Mod.Null"] = null!;
RejectConfig("# Rules\n{{Mod.Null|percent}}", settings);
string previousCulture = System.Globalization.CultureInfo.CurrentCulture.Name;
try
{
    System.Globalization.CultureInfo.CurrentCulture = System.Globalization.CultureInfo.GetCultureInfo("fr-FR");
    Check(GuideConfigText.Expand(configSource, key => settings[key]).Contains("20% (0.2)"), "config numbers use a stable decimal separator");
}
finally { System.Globalization.CultureInfo.CurrentCulture = System.Globalization.CultureInfo.GetCultureInfo(previousCulture); }
Check(GuideSearch.Matches(GuideSearch.Text(GuideDocument.Parse(configText)[0]), "12.5%"), "search includes resolved config values");
string labelConfig = GuideConfigText.Expand("# Rules\n[[Rules|Damage {{StarLevelSystem.EnemyDamageLevelMultiplier|percent}}]]\n![Rate {{StarLevelSystem.EnemyDamageLevelMultiplier|percent}}](map.png)", key => settings[key]);
List<GuideBlock> labelBlocks = GuideMarkup.Parse(GuideDocument.Parse(labelConfig)[0].Body);
Check(labelBlocks[0].Text.Contains("Damage 20%") && labelBlocks[1].Text == "Rate 20%", "config references in link labels and image captions");
Console.WriteLine("PASS: document boundaries, links, image blocks, PNG integrity, navigation history, page search, and config references.");

static void RejectConfig(string text, Dictionary<string, object> settings)
{
    try { GuideConfigText.Expand(text, key => settings[key]); }
    catch (FormatException) { return; }
    throw new Exception("Expected invalid config reference to fail");
}

static void Check(bool result, string label)
{
    if (!result) throw new Exception("FAIL: " + label);
}

static void Reject(string text)
{
    try { GuideDocument.Parse(text); }
    catch (FormatException) { return; }
    throw new Exception("FAIL: invalid guide accepted");
}

static void RejectImage(byte[] bytes)
{
    try { GuideImageData.Validate(bytes); }
    catch (FormatException) { return; }
    throw new Exception("FAIL: invalid image accepted");
}
