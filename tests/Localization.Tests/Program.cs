using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;
using AIUsageMonitor.Localization;

var root = FindRepositoryRoot();
var errors = new List<string>();
var catalogs = new Dictionary<string, Dictionary<string, string>>(StringComparer.Ordinal);

foreach (string file in Directory.EnumerateFiles(Path.Combine(root, "Resources", "Strings"), "*.json"))
{
    string language = Path.GetFileNameWithoutExtension(file);
    using JsonDocument document = JsonDocument.Parse(File.ReadAllText(file));
    var catalog = new Dictionary<string, string>(StringComparer.Ordinal);

    if (document.RootElement.ValueKind != JsonValueKind.Object)
    {
        errors.Add($"{file}: Expected a JSON object.");
        continue;
    }

    foreach (JsonProperty property in document.RootElement.EnumerateObject())
    {
        if (string.IsNullOrWhiteSpace(property.Name) || property.Value.ValueKind != JsonValueKind.String)
        {
            errors.Add($"{file}: Invalid entry '{property.Name}'.");
            continue;
        }

        if (string.IsNullOrWhiteSpace(property.Value.GetString()))
            errors.Add($"{file}: Empty translation for '{property.Name}'.");
        if (!catalog.TryAdd(property.Name, property.Value.GetString() ?? string.Empty))
            errors.Add($"{file}: Duplicate key '{property.Name}'.");
    }

    catalogs.Add(language, catalog);
}

if (!catalogs.TryGetValue("en", out var english) || !catalogs.TryGetValue("ko", out var korean))
{
    errors.Add("Both en.json and ko.json are required.");
}
else
{
    CheckParity(english, korean, errors);
    CheckLanguageScriptConsistency(english, korean, errors);
    CheckFormatArguments(english, korean, errors);
    CheckCodexLoginInstructions(english, korean, errors);
    CheckSourceReferences(root, english, errors);
}

if (errors.Count == 0)
{
    LocalizationManager.Initialize("ko-KR");
    if (LocalizationManager.ActiveLanguage != "ko" || LocalizationManager.Text("common.ok") != "확인")
        errors.Add("Korean language selection did not load the expected catalog.");
    if (LocalizationManager.Format("cursor.context_remaining", 12.5) != "12.5% 남음")
        errors.Add("Localized format strings did not resolve correctly.");
    if (LocalizationManager.Text("codex.account_default") != "Codex 계정" ||
        LocalizationManager.Format("codex.anonymous_user", 1) != "사용자 1")
        errors.Add("Localized account labels did not resolve correctly.");

    CultureInfo originalCurrentCulture = CultureInfo.CurrentCulture;
    try
    {
        CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("fr-FR");
        LocalizationManager.Initialize("en");
        if (LocalizationManager.Format("cursor.context_remaining", 12.5) != "12.5% left")
            errors.Add("Number formatting did not follow the selected language.");
    }
    finally
    {
        CultureInfo.CurrentCulture = originalCurrentCulture;
    }

    CultureInfo originalCulture = CultureInfo.CurrentUICulture;
    try
    {
        CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo("ko-KR");
        LocalizationManager.Initialize("system");
        if (LocalizationManager.ActiveLanguage != "ko")
            errors.Add("System language selection did not follow the Korean UI culture.");

        CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo("en-US");
        LocalizationManager.Initialize("system");
        if (LocalizationManager.ActiveLanguage != "en")
            errors.Add("Unsupported system languages must fall back to English.");
    }
    finally
    {
        CultureInfo.CurrentUICulture = originalCulture;
    }

    LocalizationManager.Initialize("unsupported");
    if (LocalizationManager.ActiveLanguage != "en" || LocalizationManager.Text("common.ok") != "OK")
        errors.Add("Unknown languages must use the English fallback.");
}

if (errors.Count > 0)
{
    foreach (string error in errors)
        Console.Error.WriteLine(error);
    return 1;
}

Console.WriteLine($"PASS: {catalogs.Count} languages, {english!.Count} keys per language.");
Console.WriteLine("Catalog parity, translations, format arguments, XAML keys, and C# key references verified.");
return 0;

static string FindRepositoryRoot()
{
    DirectoryInfo? directory = new(AppContext.BaseDirectory);
    while (directory is not null)
    {
        if (File.Exists(Path.Combine(directory.FullName, "Resources", "Strings", "en.json")))
            return directory.FullName;
        directory = directory.Parent;
    }

    throw new DirectoryNotFoundException("Could not locate the repository root.");
}

static void CheckParity(
    IReadOnlyDictionary<string, string> english,
    IReadOnlyDictionary<string, string> korean,
    ICollection<string> errors)
{
    foreach (string key in english.Keys.Except(korean.Keys, StringComparer.Ordinal))
        errors.Add($"ko.json: Missing key '{key}'.");
    foreach (string key in korean.Keys.Except(english.Keys, StringComparer.Ordinal))
        errors.Add($"ko.json: Unknown key '{key}'.");
}

static void CheckLanguageScriptConsistency(
    IReadOnlyDictionary<string, string> english,
    IReadOnlyDictionary<string, string> korean,
    ICollection<string> errors)
{
    var hangulPattern = new Regex(@"[가-힣]", RegexOptions.Compiled);
    var brandOnlyKeys = new HashSet<string>(StringComparer.Ordinal)
    {
        "about.antigravity",
        "about.codex"
    };

    foreach (var (key, value) in english)
    {
        if (hangulPattern.IsMatch(value))
            errors.Add($"en.json: Korean text found in '{key}'.");
        if (korean.TryGetValue(key, out string? translated) &&
            string.Equals(value, translated, StringComparison.Ordinal) && !brandOnlyKeys.Contains(key))
            errors.Add($"ko.json: English text for '{key}' was not translated.");
    }

    foreach (var (key, value) in korean)
    {
        if (!hangulPattern.IsMatch(value) && !brandOnlyKeys.Contains(key))
            errors.Add($"ko.json: No Korean text found in '{key}'.");
    }
}

static void CheckFormatArguments(
    IReadOnlyDictionary<string, string> english,
    IReadOnlyDictionary<string, string> korean,
    ICollection<string> errors)
{
    foreach (string key in english.Keys.Intersect(korean.Keys, StringComparer.Ordinal))
    {
        int[] englishArguments = GetFormatArguments(english[key]);
        int[] koreanArguments = GetFormatArguments(korean[key]);
        if (!englishArguments.SequenceEqual(koreanArguments))
            errors.Add($"ko.json: Key '{key}' has different format arguments.");

        ValidateFormat("en.json", key, english[key], errors);
        ValidateFormat("ko.json", key, korean[key], errors);
    }
}

static void CheckCodexLoginInstructions(
    IReadOnlyDictionary<string, string> english,
    IReadOnlyDictionary<string, string> korean,
    ICollection<string> errors)
{
    foreach (var (language, catalog) in new[] { ("en", english), ("ko", korean) })
    {
        string instructions = catalog["about.codex_steps"];
        foreach (string optionKey in new[] { "login.openai_oauth", "login.openai_web_session" })
        {
            if (!instructions.Contains(catalog[optionKey], StringComparison.Ordinal))
                errors.Add($"{language}.json: Codex login instructions do not match '{optionKey}'.");
        }
    }
}

static int[] GetFormatArguments(string value) =>
    Regex.Matches(value, @"(?<!\{)\{(?<index>\d+)(?:,[^}]*)?(?::[^}]*)?\}(?!\})")
        .Select(match => int.Parse(match.Groups["index"].Value, CultureInfo.InvariantCulture))
        .Distinct()
        .Order()
        .ToArray();

static void ValidateFormat(string language, string key, string value, ICollection<string> errors)
{
    try
    {
        int[] arguments = GetFormatArguments(value);
        object?[] values = arguments.Length == 0 ? [] : new object?[arguments.Max() + 1];
        _ = string.Format(CultureInfo.InvariantCulture, value, values);
    }
    catch (FormatException)
    {
        errors.Add($"{language}: Key '{key}' is not a valid composite format string.");
    }
}

static void CheckSourceReferences(
    string root,
    IReadOnlyDictionary<string, string> english,
    ICollection<string> errors)
{
    var knownKeys = english.Keys.ToHashSet(StringComparer.Ordinal);
    var malformedMarkupPattern = new Regex(@"\{loc:(?!Loc\s+Key=)([^}\s]+)", RegexOptions.Compiled);
    var markupKeyPattern = new Regex(@"\{loc:Loc\s+Key=([^}\s]+)", RegexOptions.Compiled);
    var codeKeyPattern = new Regex(@"LocalizationManager\.(?:Text|Format)\s*\(\s*""([^""]+)""", RegexOptions.Compiled);

    foreach (string file in Directory.EnumerateFiles(root, "*.xaml", SearchOption.AllDirectories)
                 .Where(path => !path.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}")
                     && !path.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}")))
    {
        string source = File.ReadAllText(file);
        foreach (Match match in malformedMarkupPattern.Matches(source))
            errors.Add($"{Path.GetRelativePath(root, file)}: Malformed localization markup '{match.Value}'.");

        foreach (Match match in markupKeyPattern.Matches(source))
        {
            if (!knownKeys.Contains(match.Groups[1].Value))
                errors.Add($"{Path.GetRelativePath(root, file)}: Missing localization key '{match.Groups[1].Value}'.");
        }
    }

    foreach (string file in Directory.EnumerateFiles(root, "*.cs", SearchOption.AllDirectories)
                 .Where(path => !path.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}")
                     && !path.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}")
                     && !path.Contains($"{Path.DirectorySeparatorChar}tests{Path.DirectorySeparatorChar}")))
    {
        string source = File.ReadAllText(file);
        foreach (Match match in codeKeyPattern.Matches(source))
        {
            if (!knownKeys.Contains(match.Groups[1].Value))
                errors.Add($"{Path.GetRelativePath(root, file)}: Missing localization key '{match.Groups[1].Value}'.");
        }
    }
}
