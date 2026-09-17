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
Console.WriteLine("PASS: server guide parsing, boundaries, Unicode, invalid input, and empty guide.");

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
