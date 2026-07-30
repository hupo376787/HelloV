namespace HelloV.Localization;

public sealed record LanguageOption(
    string Code,
    string DisplayName,
    string? FilePath = null,
    bool IsBuiltIn = false)
{
    public override string ToString() => DisplayName;
}
