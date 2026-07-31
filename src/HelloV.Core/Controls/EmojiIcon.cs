using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using HelloV.Services;

namespace HelloV.Controls;

/// <summary>
/// Small emoji icon that uses the platform emoji image provider when available. Browser builds
/// therefore render native color emoji instead of CanvasKit missing-glyph boxes.
/// </summary>
public sealed class EmojiIcon : Control
{
    public static readonly StyledProperty<string> EmojiProperty =
        AvaloniaProperty.Register<EmojiIcon, string>(nameof(Emoji), string.Empty);

    private static readonly Typeface FallbackTypeface = new(
        "Segoe UI Emoji, Apple Color Emoji, Noto Color Emoji, sans-serif",
        FontStyle.Normal,
        FontWeight.Normal);

    static EmojiIcon() => AffectsRender<EmojiIcon>(EmojiProperty);

    public string Emoji
    {
        get => GetValue(EmojiProperty);
        set => SetValue(EmojiProperty, value);
    }

    public override void Render(DrawingContext context)
    {
        base.Render(context);
        if (Bounds.Width <= 0 || Bounds.Height <= 0 || string.IsNullOrWhiteSpace(Emoji))
            return;

        var logicalSize = Math.Min(Bounds.Width, Bounds.Height);
        var image = AppServices.EmojiImageProvider?.GetEmojiImage(
            Emoji,
            Math.Clamp((int)Math.Ceiling(logicalSize * 1.8), 20, 256));

        if (image is not null)
        {
            var source = new Rect(0, 0, image.Size.Width, image.Size.Height);
            var targetSize = Math.Min(Bounds.Width, Bounds.Height);
            var destination = new Rect(
                (Bounds.Width - targetSize) / 2,
                (Bounds.Height - targetSize) / 2,
                targetSize,
                targetSize);
            context.DrawImage(image, source, destination);
            return;
        }

        var text = new FormattedText(
            Emoji,
            CultureInfo.CurrentCulture,
            FlowDirection.LeftToRight,
            FallbackTypeface,
            logicalSize * 0.82,
            Brushes.White);
        context.DrawText(text, new Point(
            (Bounds.Width - text.Width) / 2,
            (Bounds.Height - text.Height) / 2));
    }
}
