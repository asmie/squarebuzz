// Checks every translation file against the neutral culture, as docs/translation-status.md
// describes but nothing previously enforced.
//
//   dotnet run tools/check-strings.cs
//
// For each AppStrings.<code>.resx under src/Squarebuzz.App/Resources/Strings, asserts:
//   - the key set is identical to AppStrings.resx (a missing key leaks the raw key into the UI,
//     because LocalizationService returns the key on a miss)
//   - the keys appear in the neutral culture's order (the documented invariant that keeps the
//     39 files diffable side by side)
//   - no value is empty
//   - every value carries exactly the same {0}/{1}/... placeholders as the neutral value
//   - no Cyrillic characters outside the Cyrillic-script languages (a paste-slip detector)

using System.Text.RegularExpressions;
using System.Xml.Linq;

var directory = Path.Combine("src", "Squarebuzz.App", "Resources", "Strings");

if (!Directory.Exists(directory))
{
    Console.Error.WriteLine($"Strings directory not found: {directory} (run from the repository root).");
    return 1;
}

var neutralPath = Path.Combine(directory, "AppStrings.resx");
var neutral = ReadResx(neutralPath);
var neutralKeys = neutral.Select(p => p.Key).ToList();
var neutralByKey = neutral.ToDictionary(p => p.Key, p => p.Value, StringComparer.Ordinal);

// Languages genuinely written in Cyrillic; anywhere else a Cyrillic letter is a paste slip.
string[] cyrillicLanguages = ["bg", "ru", "uk"];

var failures = 0;

void Fail(string file, string message)
{
    failures++;
    Console.Error.WriteLine($"FAIL {file}: {message}");
}

Console.WriteLine($"{Path.GetFileName(neutralPath)}: {neutralKeys.Count} keys (neutral).");

foreach (var path in Directory.EnumerateFiles(directory, "AppStrings.*.resx").OrderBy(p => p, StringComparer.Ordinal))
{
    var file = Path.GetFileName(path);
    var code = file["AppStrings.".Length..^".resx".Length];
    var entries = ReadResx(path);
    var keys = entries.Select(p => p.Key).ToList();

    Console.WriteLine($"{file}: {keys.Count} keys.");

    var keySet = new HashSet<string>(keys, StringComparer.Ordinal);

    foreach (var missing in neutralKeys.Where(k => !keySet.Contains(k)))
    {
        Fail(file, $"missing key '{missing}'.");
    }

    foreach (var extra in keys.Where(k => !neutralByKey.ContainsKey(k)))
    {
        Fail(file, $"extra key '{extra}' not in the neutral culture.");
    }

    if (keySet.SetEquals(neutralKeys) && !keys.SequenceEqual(neutralKeys, StringComparer.Ordinal))
    {
        var divergence = keys.Zip(neutralKeys).First(pair => pair.First != pair.Second);
        Fail(file, $"keys are out of the neutral order (first divergence: '{divergence.First}' where '{divergence.Second}' was expected).");
    }

    foreach (var (key, value) in entries)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            Fail(file, $"'{key}' has an empty value.");
        }

        if (neutralByKey.TryGetValue(key, out var neutralValue)
            && !Placeholders(value).SetEquals(Placeholders(neutralValue)))
        {
            Fail(file, $"'{key}' placeholders differ from the neutral value ('{value}' vs '{neutralValue}').");
        }

        if (!cyrillicLanguages.Contains(code)
            && value.Any(c => c >= 'Ѐ' && c <= 'ӿ'))
        {
            Fail(file, $"'{key}' contains Cyrillic characters in a non-Cyrillic language.");
        }
    }
}

if (failures > 0)
{
    Console.Error.WriteLine($"{failures} failure(s).");
    return 1;
}

Console.WriteLine("All translation files are consistent with the neutral culture.");
return 0;

static List<KeyValuePair<string, string>> ReadResx(string path) =>
    [.. XDocument.Load(path).Root!
        .Elements("data")
        .Select(d => new KeyValuePair<string, string>(
            d.Attribute("name")!.Value,
            d.Element("value")?.Value ?? string.Empty))];

static HashSet<string> Placeholders(string value) =>
    [.. Regex.Matches(value, @"\{\d+\}").Select(m => m.Value)];
