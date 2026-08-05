using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.Runtime.CompilerServices;
using System.Text.Json;

namespace HelloV.Localization;

/// <summary>
/// Loads editable JSON language packs from the Languages directory beside the executable.
/// Invalid files are ignored and missing keys always fall back to Simplified Chinese.
/// </summary>
public sealed class LocalizationManager : INotifyPropertyChanged
{
    private const string FallbackCode = "zh-CN";
    private readonly Dictionary<string, LanguagePack> _packs =
        new(StringComparer.OrdinalIgnoreCase);
    private LanguagePack _fallbackPack = BuiltInLanguagePacks.ZhCn;
    private LanguagePack _currentPack = BuiltInLanguagePacks.ZhCn;
    private LanguageOption? _selectedLanguage;

    public LocalizationManager()
    {
        LanguagesDirectory = Path.Combine(AppContext.BaseDirectory, "Languages");
        RescanCore(loadSavedSelection: true);
    }

    public string LanguagesDirectory { get; }

    public ObservableCollection<LanguageOption> Languages { get; } = [];

    public LanguageOption? SelectedLanguage
    {
        get => _selectedLanguage;
        set
        {
            if (value is null ||
                string.Equals(_selectedLanguage?.Code, value.Code, StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            ApplyLanguage(value.Code, persist: true, raiseChanged: true);
        }
    }

    public string CurrentLanguageCode => _currentPack.Code;

    public CultureInfo CurrentCulture
    {
        get
        {
            try
            {
                return CultureInfo.GetCultureInfo(_currentPack.Code);
            }
            catch (CultureNotFoundException)
            {
                return CultureInfo.InvariantCulture;
            }
        }
    }

    public string this[string key] => Get(key);

    public string AppTitle => Get("AppTitle");
    public string SettingsToolTip => Get("SettingsToolTip");
    public string SwitchCamera => Get("SwitchCamera");
    public string SettingsTitle => Get("SettingsTitle");
    public string SettingsSubtitle => Get("SettingsSubtitle");
    public string CameraDescription => Get("CameraDescription");
    public string FixMirror => Get("FixMirror");
    public string InterruptModeTitle => Get("InterruptModeTitle");
    public string InterruptModeDescription => Get("InterruptModeDescription");
    public string LanguageTitle => Get("LanguageTitle");
    public string LanguageDescription => Get("LanguageDescription");
    public string RescanLanguages => Get("RescanLanguages");
    public string LanguageFolderHint => Get("LanguageFolderHint");
    public string AnimationTestTitle => Get("AnimationTestTitle");
    public string AnimationTestDescription => Get("AnimationTestDescription");
    public string PlaySelectedAnimation => Get("PlaySelectedAnimation");
    public string PlayNextAnimation => Get("PlayNextAnimation");
    public string GestureAnimationsTitle => Get("GestureAnimationsTitle");
    public string GestureAnimationsDescription => Get("GestureAnimationsDescription");
    public string HeartSummary => Get("HeartSummary");
    public string LikeSummary => Get("LikeSummary");
    public string DislikeSummary => Get("DislikeSummary");
    public string PeaceSummary => Get("PeaceSummary");
    public string RockSummary => Get("RockSummary");
    public string OtherGesturesSummary => Get("OtherGesturesSummary");
    public string TriggerRuleSummary => Get("TriggerRuleSummary");

    public event EventHandler? LanguageChanged;
    public event PropertyChangedEventHandler? PropertyChanged;

    public string Get(string key)
    {
        if (string.IsNullOrWhiteSpace(key))
            return string.Empty;

        if (_currentPack.Strings.TryGetValue(key, out var translated) &&
            !string.IsNullOrWhiteSpace(translated))
        {
            return translated;
        }

        if (_fallbackPack.Strings.TryGetValue(key, out var fallback) &&
            !string.IsNullOrWhiteSpace(fallback))
        {
            return fallback;
        }

        if (BuiltInLanguagePacks.ZhCn.Strings.TryGetValue(key, out var builtInFallback) &&
            !string.IsNullOrWhiteSpace(builtInFallback))
        {
            return builtInFallback;
        }

        return key;
    }

    public string Format(string key, params object?[] arguments)
    {
        var format = Get(key);
        try
        {
            return string.Format(CurrentCulture, format, arguments);
        }
        catch (FormatException ex)
        {
            Debug.WriteLine($"Language format error ({_currentPack.Code}/{key}): {ex.Message}");

            if (_fallbackPack.Strings.TryGetValue(key, out var fallbackFormat))
            {
                try
                {
                    return string.Format(CultureInfo.GetCultureInfo(FallbackCode), fallbackFormat, arguments);
                }
                catch (FormatException)
                {
                    // Continue to the compiled Simplified Chinese fallback below.
                }
            }

            if (BuiltInLanguagePacks.ZhCn.Strings.TryGetValue(key, out var builtInFormat))
            {
                try
                {
                    return string.Format(CultureInfo.GetCultureInfo(FallbackCode), builtInFormat, arguments);
                }
                catch (FormatException)
                {
                    // Compiled resources are validated during development; keep startup safe anyway.
                }
            }

            return format;
        }
    }

    public void Rescan() => RescanCore(loadSavedSelection: false);

    private void RescanCore(bool loadSavedSelection)
    {
        var requestedCode = loadSavedSelection
            ? LoadSavedLanguageCode()
            : _selectedLanguage?.Code;

        _packs.Clear();
        AddOrReplace(BuiltInLanguagePacks.ZhCn);
        AddOrReplace(BuiltInLanguagePacks.EnUs);

        try
        {
            Directory.CreateDirectory(LanguagesDirectory);
            foreach (var file in Directory.EnumerateFiles(LanguagesDirectory, "*.json", SearchOption.TopDirectoryOnly)
                         .OrderBy(x => x, StringComparer.OrdinalIgnoreCase))
            {
                try
                {
                    var pack = ReadPack(file);
                    if (pack is not null)
                        AddOrReplace(pack);
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
                {
                    Debug.WriteLine($"Ignored invalid language pack '{file}': {ex.Message}");
                }
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Debug.WriteLine($"Unable to scan language directory '{LanguagesDirectory}': {ex.Message}");
        }

        _fallbackPack = _packs.TryGetValue(FallbackCode, out var fallback)
            ? fallback
            : BuiltInLanguagePacks.ZhCn;

        RebuildLanguageOptions();

        requestedCode ??= ResolveSystemLanguageCode();
        ApplyLanguage(requestedCode, persist: false, raiseChanged: true);
    }

    private void RebuildLanguageOptions()
    {
        Languages.Clear();

        foreach (var pack in _packs.Values
                     .OrderBy(LanguageSortOrder)
                     .ThenBy(x => x.DisplayName, StringComparer.CurrentCultureIgnoreCase))
        {
            Languages.Add(new LanguageOption(
                pack.Code,
                pack.DisplayName,
                pack.FilePath,
                pack.IsBuiltIn));
        }

        static int LanguageSortOrder(LanguagePack pack) => pack.Code.ToLowerInvariant() switch
        {
            "zh-cn" => 0,
            "en-us" => 1,
            _ => 10
        };
    }

    private void ApplyLanguage(string? requestedCode, bool persist, bool raiseChanged)
    {
        var pack = FindBestPack(requestedCode) ?? _fallbackPack;
        _currentPack = pack;
        _selectedLanguage = Languages.FirstOrDefault(x =>
                                string.Equals(x.Code, pack.Code, StringComparison.OrdinalIgnoreCase))
                            ?? new LanguageOption(pack.Code, pack.DisplayName, pack.FilePath, pack.IsBuiltIn);

        if (persist)
            SaveLanguageCode(pack.Code);

        OnPropertyChanged(nameof(SelectedLanguage));
        OnPropertyChanged(nameof(CurrentLanguageCode));
        OnPropertyChanged(nameof(CurrentCulture));
        OnPropertyChanged("Item");
        OnPropertyChanged("Item[]");
        OnPropertyChanged(null);

        if (raiseChanged)
            LanguageChanged?.Invoke(this, EventArgs.Empty);
    }

    private LanguagePack? FindBestPack(string? code)
    {
        if (!string.IsNullOrWhiteSpace(code))
        {
            if (_packs.TryGetValue(code, out var exact))
                return exact;

            var languagePrefix = code.Split('-', '_')[0];
            var prefixMatch = _packs.Values.FirstOrDefault(x =>
                x.Code.StartsWith(languagePrefix + "-", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(x.Code, languagePrefix, StringComparison.OrdinalIgnoreCase));
            if (prefixMatch is not null)
                return prefixMatch;
        }

        return _packs.TryGetValue(FallbackCode, out var fallback) ? fallback : null;
    }

    private string ResolveSystemLanguageCode()
    {
        var systemCode = CultureInfo.CurrentUICulture.Name;
        return FindBestPack(systemCode)?.Code ?? FallbackCode;
    }

    private void AddOrReplace(LanguagePack pack) => _packs[pack.Code] = pack;

    private static LanguagePack? ReadPack(string file)
    {
        using var stream = File.OpenRead(file);
        using var document = JsonDocument.Parse(stream, new JsonDocumentOptions
        {
            AllowTrailingCommas = true,
            CommentHandling = JsonCommentHandling.Skip
        });

        if (document.RootElement.ValueKind != JsonValueKind.Object)
            return null;

        var root = document.RootElement;
        var code = GetString(root, "code")
                   ?? GetString(root, "languageCode")
                   ?? Path.GetFileNameWithoutExtension(file);
        var name = GetString(root, "name")
                   ?? GetString(root, "displayName")
                   ?? code;

        var strings = new Dictionary<string, string>(StringComparer.Ordinal);
        if (TryGetProperty(root, "strings", out var stringsElement) &&
            stringsElement.ValueKind == JsonValueKind.Object)
        {
            AddStringProperties(stringsElement, strings);
        }
        else if (TryGetProperty(root, "translations", out var translationsElement) &&
                 translationsElement.ValueKind == JsonValueKind.Object)
        {
            AddStringProperties(translationsElement, strings);
        }
        else
        {
            // Also accept a flat JSON object so third-party packs are easy to author.
            foreach (var property in root.EnumerateObject())
            {
                if (property.Name.Equals("code", StringComparison.OrdinalIgnoreCase) ||
                    property.Name.Equals("languageCode", StringComparison.OrdinalIgnoreCase) ||
                    property.Name.Equals("name", StringComparison.OrdinalIgnoreCase) ||
                    property.Name.Equals("displayName", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                if (property.Value.ValueKind == JsonValueKind.String)
                    strings[property.Name] = property.Value.GetString() ?? string.Empty;
            }
        }

        if (string.IsNullOrWhiteSpace(code) || strings.Count == 0)
            return null;

        return new LanguagePack(code.Trim(), name.Trim(), strings, file, IsBuiltIn: false);
    }

    private static void AddStringProperties(
        JsonElement element,
        IDictionary<string, string> destination)
    {
        foreach (var property in element.EnumerateObject())
        {
            if (property.Value.ValueKind == JsonValueKind.String)
                destination[property.Name] = property.Value.GetString() ?? string.Empty;
        }
    }

    private static string? GetString(JsonElement element, string name) =>
        TryGetProperty(element, name, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;

    private static bool TryGetProperty(JsonElement element, string name, out JsonElement value)
    {
        foreach (var property in element.EnumerateObject())
        {
            if (!property.Name.Equals(name, StringComparison.OrdinalIgnoreCase))
                continue;

            value = property.Value;
            return true;
        }

        value = default;
        return false;
    }

    private static string SettingsFilePath
    {
        get
        {
            var root = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            if (string.IsNullOrWhiteSpace(root))
                root = Path.GetTempPath();

            return Path.Combine(root, "HelloV", "language.json");
        }
    }

    private static string? LoadSavedLanguageCode()
    {
        try
        {
            if (!File.Exists(SettingsFilePath))
                return null;

            using var document = JsonDocument.Parse(File.ReadAllText(SettingsFilePath));
            return GetString(document.RootElement, "language");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException or InvalidOperationException)
        {
            Debug.WriteLine($"Unable to read language setting: {ex.Message}");
            return null;
        }
    }

    private static void SaveLanguageCode(string code)
    {
        try
        {
            var directory = Path.GetDirectoryName(SettingsFilePath)!;
            Directory.CreateDirectory(directory);
            File.WriteAllText(
                SettingsFilePath,
                JsonSerializer.Serialize(new { language = code }, new JsonSerializerOptions
                {
                    WriteIndented = true
                }));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Debug.WriteLine($"Unable to save language setting: {ex.Message}");
        }
    }

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
}
