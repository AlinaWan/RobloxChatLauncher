// Usage:
//     dotnet run .github/scripts/AuditLoc.cs

// Tiny dummy package directive because C# has a weird quirk where
// Directory.GetCurrentDirectory() actually resolves to either the script
// dir or somewhere in AppData\Local\Temp\dotnet\runfile depending on how you run it,
// UNLESS you add a package directive which forces it to resolve to the real cwd.
#:package Empty@1.0.0

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Xml.Linq;

// 1. Setup Paths
var cwd = Directory.GetCurrentDirectory();
var localizationFolder = Path.Combine(cwd, "client", "Localization");
var targetFile = Path.Combine(cwd, "README.md");

// 2. Validate Source
if (!File.Exists(targetFile))
{
    throw new FileNotFoundException($"Oops! I couldn't find the file at {targetFile}... Check the folder for me babe? 🎀");
}

// 3. Logic Execution
try
{
    var auditor = new LocalizationAuditor(localizationFolder);
    var results = auditor.CalculateCoverage();

    var updater = new ReadmeUpdater(targetFile);
    updater.InjectTable(results);

    Console.WriteLine($"Generated {results.Count} localization entries at {targetFile}! 💝");
}
catch (Exception ex)
{
    Console.WriteLine($"Error: {ex.Message}");
}

// --- Reusable Classes ---

public record LocalizationResult(string Locale, int TranslatedCount, int TotalCount, double Percentage);

public class LocalizationAuditor
{
    private readonly string _dir;
    private const string NeutralFileName = "Strings.resx";

    public LocalizationAuditor(string directory) => _dir = directory;

    public List<LocalizationResult> CalculateCoverage()
    {
        string neutralPath = Path.Combine(_dir, NeutralFileName);
        if (!File.Exists(neutralPath))
            throw new FileNotFoundException("Neutral Strings.resx not found.");

        var neutralKeys = GetKeys(neutralPath);
        int total = neutralKeys.Count;

        var results = new List<LocalizationResult>();

        // Match ALL .resx files in the folder
        var allResxFiles = Directory.GetFiles(_dir, "*.resx");

        string neutralFileLang = "en"; // Strings.resx is the neutral language, this is the assumed locale for it in the output table.

        foreach (var file in allResxFiles)
        {
            var fileName = Path.GetFileName(file);

            // Determine the locale
            // Strings.resx -> neutralFileLang (e.g. "en")
            // Strings.fr.resx -> "fr"
            string locale;
            if (fileName.Equals("Strings.resx", StringComparison.OrdinalIgnoreCase))
            {
                locale = neutralFileLang; // use the neutral language variable
            }
            else
            {
                // Extract the part between "Strings." and ".resx"
                locale = fileName.Substring(
                    "Strings.".Length,
                    fileName.Length - "Strings.".Length - ".resx".Length
                );
            }

            var localKeys = GetKeys(file);
            int count = localKeys.Count(k => neutralKeys.ContainsKey(k.Key) && !string.IsNullOrWhiteSpace(k.Value));

            double percent = total > 0 ? (double)count / total * 100 : 0;

            // Avoid adding duplicates
            if (!results.Any(r => r.Locale.Equals(locale, StringComparison.OrdinalIgnoreCase)))
            {
                results.Add(new LocalizationResult(locale, count, total, percent));
            }
        }

        return results
            .OrderBy(r => !r.Locale.Equals(neutralFileLang, StringComparison.OrdinalIgnoreCase)) // neutral language first
            .ThenByDescending(r => r.Percentage)
            .ThenBy(r => r.Locale)
            .ToList();
    }


    private Dictionary<string, string> GetKeys(string path)
    {
        return XDocument.Load(path).Root?.Elements("data")
            .ToDictionary(
                e => e.Attribute("name")?.Value ?? string.Empty,
                e => e.Element("value")?.Value ?? string.Empty
            ) ?? new Dictionary<string, string>();
    }
}

public class ReadmeUpdater
{
    private readonly string _filePath;
    private const string StartTag = "<!-- START CI GENERATED LOCALIZATION STATUS TABLE; DO NOT REMOVE COMMENT -->";
    private const string EndTag = "<!-- END CI GENERATED LOCALIZATION STATUS TABLE; DO NOT REMOVE COMMENT -->";

    public ReadmeUpdater(string filePath) => _filePath = filePath;

    public void InjectTable(List<LocalizationResult> data)
    {
        var sb = new StringBuilder();
        sb.AppendLine(StartTag);
        sb.AppendLine();
        sb.AppendLine("<div align=\"center\">");
        sb.AppendLine();
        sb.AppendLine("| Language | Status |");
        sb.AppendLine("| :--- | :---: |");

        foreach (var item in data)
        {
            string progress = $"{item.Percentage:F1}%";
            string status = $"![{progress}](https://geps.dev/progress/{Math.Round(item.Percentage)})";

            sb.AppendLine($"| **{item.Locale}** | {status} |");
        }

        sb.AppendLine();
        sb.AppendLine("</div>");
        sb.AppendLine();
        sb.Append(EndTag);

        string content = File.ReadAllText(_filePath);
        int startIndex = content.IndexOf(StartTag);
        int endIndex = content.IndexOf(EndTag);

        if (startIndex == -1 || endIndex == -1)
        {
            throw new Exception("Could not find the injection comments in {_filePath}");
        }

        // Replace everything between the tags with our new table
        string newContent = content.Substring(0, startIndex) +
                            sb.ToString() +
                            content.Substring(endIndex + EndTag.Length);

        File.WriteAllText(_filePath, newContent);
    }
}
