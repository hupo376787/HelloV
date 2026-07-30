namespace HelloV.Localization;

internal sealed record LanguagePack(
    string Code,
    string DisplayName,
    IReadOnlyDictionary<string, string> Strings,
    string? FilePath,
    bool IsBuiltIn);
