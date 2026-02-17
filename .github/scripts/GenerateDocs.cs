#:package Microsoft.CodeAnalysis.CSharp@4.12.0

using System;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using System.Text;

try
{
    var cwd = Directory.GetCurrentDirectory();
    var sourceFile = Path.Combine(cwd, "client", "ChatForm.Commands.cs");
    var outputFile = Path.Combine(cwd, "assets", "docs", "COMMANDS.md");

    if (!File.Exists(sourceFile))
        throw new FileNotFoundException($"Oops! I couldn't find the file at {sourceFile}... Check the folder for me babe? 🎀 ");

    var code = File.ReadAllText(sourceFile);
    var tree = CSharpSyntaxTree.ParseText(code);
    var root = tree.GetRoot();

    var commands = root.DescendantNodes()
        .OfType<SwitchStatementSyntax>()
        .SelectMany(s => s.Sections)
        .Select(section =>
        {
            var caseLabels = section.Labels
                .OfType<CaseSwitchLabelSyntax>()
                .Select(l => l.Value.ToString().Trim('"'))
                .ToList();

            if (!caseLabels.Any())
                return null;

            var actionLines = section.Statements
                .Select(stmt => stmt.ToString().Trim())
                .Where(text => !text.Contains("return true"))
                .Select(text =>
                {
                    string p = text;
                    p = p.Replace("await ", "").TrimEnd(';');

                    // --- HANDLE CONCATENATED STRINGS & PRINT ---
                    if (p.Contains("chatBox.AppendText"))
                    {
                        // 1. Get all content between quotes
                        var matches = Regex.Matches(p, @"""(.*?)""");
                        if (matches.Count > 0)
                        {
                            // 2. Combine them into one string
                            var combined = string.Join("", matches.Cast<Match>().Select(m => m.Groups[1].Value));

                            // 3. Split by the C# newline marker "\r\n"
                            var lines = combined.Split(new[] { "\\r\\n" }, StringSplitOptions.RemoveEmptyEntries);

                            // 4. Clean each line and wrap in <code> tags
                            var formattedLines = lines.Select(line =>
                            {
                                var cleanLine = line.Trim().Replace("{", "&#123;").Replace("}", "&#125;");
                                return $"<code>Print: \"{cleanLine}\"</code>";
                            });

                            // 5. Join these lines with <br> so they actually stack
                            return string.Join("<br>", formattedLines);
                        }
                    }

                    // --- FLATTEN WHITESPACE ---
                    p = Regex.Replace(p, @"\s+", " ").Trim();

                    // --- ESCAPE BRACES & CLEAN SYMBOLS ---
                    p = p.Replace("{", "&#123;").Replace("}", "&#125;");
                    p = p.Replace("$\"", "\"");

                    return $"<code>{p}</code>";
                })
                .ToList();

            return new
            {
                Command = $"/{caseLabels.First().TrimStart('/')}",
                Aliases = caseLabels.Skip(1).Any()
                    ? string.Join(", ", caseLabels.Skip(1).Select(c => $"`/{c.TrimStart('/')}`"))
                    : "None",
                Action = string.Join("<br>", actionLines)
            };
        })
        .Where(c => c != null)
        .ToList();

    var sb = new StringBuilder();
    sb.Append("# Command Documentation\r\n\r\n");
    sb.Append("| Command | Aliases | Action / Function |\r\n");
    sb.Append("| :--- | :--- | :--- |\r\n");

    foreach (var cmd in commands!)
        sb.Append($"| **{cmd.Command}** | {cmd.Aliases} | {cmd.Action} |\r\n");

    var outputDir = Path.GetDirectoryName(outputFile) ?? cwd;
    Directory.CreateDirectory(outputDir);
    File.WriteAllText(outputFile, sb.ToString(), Encoding.UTF8);

    Console.WriteLine($"Documentation generated at {outputFile}. You're doing amazing, sweetie! 💝");
}
catch (Exception ex)
{
    Console.WriteLine($"{ex.Message}");
}