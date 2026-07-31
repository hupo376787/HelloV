using System.Collections.Concurrent;
using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Threading;
using HelloV.Models;
using HelloV.Services;

namespace HelloV.Controls;

/// <summary>
/// Particle animation layer. Emoji are supplied by the platform-native emoji renderer and cached,
/// while geometric particles remain independent from the camera preview pipeline.
/// </summary>
public sealed class ReactionOverlay : Control
{
    public static readonly StyledProperty<ReactionKind> ReactionProperty =
        AvaloniaProperty.Register<ReactionOverlay, ReactionKind>(nameof(Reaction));

    public static readonly StyledProperty<int> SequenceProperty =
        AvaloniaProperty.Register<ReactionOverlay, int>(nameof(Sequence));

    public static readonly StyledProperty<double> AnchorXProperty =
        AvaloniaProperty.Register<ReactionOverlay, double>(nameof(AnchorX), 0.5);

    public static readonly StyledProperty<double> AnchorYProperty =
        AvaloniaProperty.Register<ReactionOverlay, double>(nameof(AnchorY), 0.5);

    private static readonly Typeface LabelTypeface =
        new("Inter, Segoe UI, Arial", FontStyle.Normal, FontWeight.Bold);

    private static readonly Typeface EmojiTypeface =
        new("Segoe UI Emoji, Apple Color Emoji, Noto Color Emoji, sans-serif",
            FontStyle.Normal,
            FontWeight.Normal);

    private const double UnifiedEffectScale = 1.5;
    private const double DesktopParticleScale = 1.24;
    private const double DesktopEmojiScale = 1.16;
    private const double EffectStrokeScale = 1.45;
    private const double LaserStrokeScale = 1.90;

    // Use one enhanced effect profile on every target so desktop, mobile and browser render the
    // same particle density, decorations and dimensions. UnifiedEffectScale is applied on top of
    // the previous desktop sizing requested by the UI.
    private static readonly bool UseUnifiedLargeEffectProfile = true;

    private static readonly ConcurrentDictionary<string, IBrush> BrushCache = new();

    private static readonly IBrush[] AccentBrushes =
    [
        B("#FFD60A"), B("#FF375F"), B("#64D2FF"), B("#30D158"),
        B("#FF9F0A"), B("#BF5AF2"), B("#5E5CE6"), B("#FFFFFF")
    ];

    private readonly DispatcherTimer _timer;
    private readonly Random _random = new();
    private readonly List<Particle> _particles = [];
    private DateTimeOffset _started;
    private ReactionKind _active;
    private double _anchorX = 0.5;
    private double _anchorY = 0.5;

    public ReactionOverlay()
    {
        IsHitTestVisible = false;
        _timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(16) };
        _timer.Tick += (_, _) => Tick();
        AffectsRender<ReactionOverlay>(ReactionProperty, SequenceProperty, AnchorXProperty, AnchorYProperty);
    }

    public ReactionKind Reaction
    {
        get => GetValue(ReactionProperty);
        set => SetValue(ReactionProperty, value);
    }

    public int Sequence
    {
        get => GetValue(SequenceProperty);
        set => SetValue(SequenceProperty, value);
    }

    public double AnchorX
    {
        get => GetValue(AnchorXProperty);
        set => SetValue(AnchorXProperty, value);
    }

    public double AnchorY
    {
        get => GetValue(AnchorYProperty);
        set => SetValue(AnchorYProperty, value);
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == SequenceProperty && Sequence > 0)
            Start(Reaction);
    }

    private void Start(ReactionKind kind)
    {
        if (kind == ReactionKind.None)
            return;

        _active = kind;
        _anchorX = Math.Clamp(AnchorX, 0.08, 0.92);
        _anchorY = Math.Clamp(AnchorY, 0.10, 0.90);
        _started = DateTimeOffset.UtcNow;
        _particles.Clear();

        switch (kind)
        {
            case ReactionKind.HeartBurst:
            case ReactionKind.HeartPulse:
                CreateFloatingParticles(EffectParticleCount(50, 34),
                    Math.Max(0.04, _anchorX - 0.22), Math.Min(0.96, _anchorX + 0.22),
                    Math.Min(0.95, _anchorY + 0.05), Math.Min(1.12, _anchorY + 0.38),
                    -0.10, 0.10, -0.46, -0.17,
                    UseUnifiedLargeEffectProfile ? 18 : 15, UseUnifiedLargeEffectProfile ? 44 : 34);
                break;
            case ReactionKind.Balloons:
                CreateFloatingParticles(EffectParticleCount(38, 16), 0.01, 0.99, 0.90, 1.34, -0.08, 0.08, -0.48, -0.18,
                    UseUnifiedLargeEffectProfile ? 30 : 22, UseUnifiedLargeEffectProfile ? 70 : 46);
                break;
            case ReactionKind.Rain:
                CreateRainParticles(EffectParticleCount(90, 56));
                break;
            case ReactionKind.Confetti:
            case ReactionKind.RibbonCannon:
                CreateBurstParticles(EffectParticleCount(180, 72), 0.13, 0.72, -1.12, -0.38,
                    UseUnifiedLargeEffectProfile ? 7 : 5, UseUnifiedLargeEffectProfile ? 17 : 12);
                break;
            case ReactionKind.PinkSparkles:
            case ReactionKind.PinchSpark:
                CreateBurstParticles(EffectParticleCount(44, 30), 0.08, 0.44, -0.30, 0.30, 4, 11);
                break;
            case ReactionKind.BubbleTrail:
                CreateFloatingParticles(EffectParticleCount(20, 14), Math.Max(0.08, _anchorX - 0.12), Math.Min(0.92, _anchorX + 0.12),
                    Math.Min(0.88, _anchorY + 0.04), Math.Min(1.02, _anchorY + 0.32),
                    -0.10, 0.10, -0.38, -0.16, 12, 30);
                break;
            case ReactionKind.ReverseMeteor:
                CreateMeteorParticles(EffectParticleCount(28, 20));
                break;
            case ReactionKind.GrabMagnet:
            case ReactionKind.PortalGate:
                CreateOrbitParticles(EffectParticleCount(kind == ReactionKind.PortalGate ? 42 : 28,
                    kind == ReactionKind.PortalGate ? 28 : 20));
                break;
            case ReactionKind.Fireworks:
                CreateBurstParticles(EffectParticleCount(120, 52), 0.10, 0.66, -0.48, 0.48,
                    UseUnifiedLargeEffectProfile ? 5 : 3, UseUnifiedLargeEffectProfile ? 13 : 9);
                break;
        }

        _timer.Start();
        InvalidateVisual();
    }


    private static int EffectParticleCount(int desktopCount, int mobileCount)
    {
        _ = mobileCount;
        return desktopCount;
    }

    // Particle coordinates are normalized and therefore do not use Unit. Scale their pixel sizes
    // explicitly by 1.5 on top of the former desktop profile.
    private static double ParticleSize(double size) =>
        size * DesktopParticleScale * UnifiedEffectScale;

    // Most non-particle emoji receive a Unit-based size, and Unit already contains the new 1.5x
    // factor. Retain the former desktop emoji multiplier here so all platforms match desktop.
    private static double EmojiSize(double size) =>
        size * DesktopEmojiScale;

    // Constant pixel-width strokes do not automatically grow with Unit. Increase geometric effect
    // outlines on every platform so lasers, ripples, rain, meteors and similar lines remain visible
    // over bright camera frames.
    private static Pen EffectPen(IBrush brush, double thickness) =>
        new(brush, Math.Max(thickness + 1.0, thickness * EffectStrokeScale));

    private static Pen LaserPen(IBrush brush, double thickness) =>
        new(brush, Math.Max(thickness + 2.0, thickness * LaserStrokeScale));

    private void CreateFloatingParticles(
        int count,
        double minX,
        double maxX,
        double minY,
        double maxY,
        double minVx,
        double maxVx,
        double minVy,
        double maxVy,
        double minSize,
        double maxSize)
    {
        for (var i = 0; i < count; i++)
        {
            _particles.Add(new Particle(
                Lerp(minX, maxX, _random.NextDouble()),
                Lerp(minY, maxY, _random.NextDouble()),
                Lerp(minVx, maxVx, _random.NextDouble()),
                Lerp(minVy, maxVy, _random.NextDouble()),
                ParticleSize(Lerp(minSize, maxSize, _random.NextDouble())),
                _random.Next(AccentBrushes.Length),
                _random.NextDouble() * Math.PI * 2));
        }
    }

    private void CreateRainParticles(int count)
    {
        for (var i = 0; i < count; i++)
        {
            _particles.Add(new Particle(
                _random.NextDouble(),
                -_random.NextDouble() * 1.2,
                -0.06 - _random.NextDouble() * 0.05,
                0.72 + _random.NextDouble() * 0.65,
                ParticleSize(12 + _random.NextDouble() * 22),
                i % AccentBrushes.Length,
                _random.NextDouble() * Math.PI * 2));
        }
    }

    private void CreateBurstParticles(
        int count,
        double minSpeed,
        double maxSpeed,
        double minVy,
        double maxVy,
        double minSize,
        double maxSize)
    {
        for (var i = 0; i < count; i++)
        {
            var angle = _random.NextDouble() * Math.PI * 2;
            var speed = Lerp(minSpeed, maxSpeed, _random.NextDouble());
            _particles.Add(new Particle(
                _anchorX,
                _anchorY,
                Math.Cos(angle) * speed,
                Lerp(minVy, maxVy, _random.NextDouble()) + Math.Sin(angle) * speed * 0.35,
                ParticleSize(Lerp(minSize, maxSize, _random.NextDouble())),
                _random.Next(AccentBrushes.Length),
                _random.NextDouble() * Math.PI * 2));
        }
    }

    private void CreateMeteorParticles(int count)
    {
        for (var i = 0; i < count; i++)
        {
            _particles.Add(new Particle(
                0.15 + _random.NextDouble() * 0.70,
                -0.35 - _random.NextDouble() * 0.75,
                -0.10 + _random.NextDouble() * 0.20,
                0.55 + _random.NextDouble() * 0.45,
                ParticleSize(8 + _random.NextDouble() * 14),
                _random.Next(AccentBrushes.Length),
                _random.NextDouble() * Math.PI * 2));
        }
    }

    private void CreateOrbitParticles(int count)
    {
        for (var i = 0; i < count; i++)
        {
            var angle = i * Math.PI * 2 / count;
            var radius = 0.10 + _random.NextDouble() * 0.24;
            _particles.Add(new Particle(
                _anchorX + Math.Cos(angle) * radius,
                _anchorY + Math.Sin(angle) * radius,
                0,
                0,
                ParticleSize(4 + _random.NextDouble() * 8),
                _random.Next(AccentBrushes.Length),
                angle));
        }
    }

    private void Tick()
    {
        var elapsed = (DateTimeOffset.UtcNow - _started).TotalSeconds;
        if (elapsed > DurationFor(_active))
        {
            _timer.Stop();
            _active = ReactionKind.None;
        }

        InvalidateVisual();
    }

    public override void Render(DrawingContext context)
    {
        base.Render(context);
        if (_active == ReactionKind.None || Bounds.Width <= 0 || Bounds.Height <= 0)
            return;

        var t = (DateTimeOffset.UtcNow - _started).TotalSeconds;
        switch (_active)
        {
            case ReactionKind.Fireworks: DrawFireworks(context, t); break;
            case ReactionKind.Rain: DrawRain(context, t); break;
            case ReactionKind.Confetti: DrawConfetti(context, t, false); break;
            case ReactionKind.Lasers: DrawLasers(context, t, true); break;
            case ReactionKind.GrabMagnet: DrawGrabMagnet(context, t); break;
            case ReactionKind.GripPulse: DrawGripPulse(context, t); break;
            case ReactionKind.HolyHalo: DrawHolyHalo(context, t); break;
            case ReactionKind.PointArrow: DrawPointArrow(context, t); break;
            case ReactionKind.PhoneWave: DrawPhoneWave(context, t); break;
            case ReactionKind.TripleStars: DrawTripleStars(context, t); break;
            case ReactionKind.TimeoutRing: DrawTimeoutRing(context, t); break;
            case ReactionKind.XSlash: DrawXSlash(context, t); break;
            case ReactionKind.HeartBurst: DrawHeart(context, t, false); break;
            case ReactionKind.HeartPulse: DrawHeart(context, t, true); break;
            case ReactionKind.PinkSparkles: DrawSparkles(context, t, true); break;
            case ReactionKind.PurpleArc: DrawPurpleArc(context, t); break;
            case ReactionKind.CameraFlash: DrawCameraFlash(context, t); break;
            case ReactionKind.ThumbsDown: DrawThumb(context, t, false); break;
            case ReactionKind.ImpactShockwave: DrawImpact(context, t); break;
            case ReactionKind.FourStarPattern: DrawFourStars(context, t); break;
            case ReactionKind.ThumbsUp: DrawThumb(context, t, true); break;
            case ReactionKind.MuteWave: DrawMute(context, t); break;
            case ReactionKind.OkRing: DrawOk(context, t); break;
            case ReactionKind.SpotlightOne: DrawSpotlight(context, t); break;
            case ReactionKind.PalmShield: DrawShield(context, t); break;
            case ReactionKind.Balloons: DrawBalloons(context, t); break;
            case ReactionKind.RibbonCannon: DrawConfetti(context, t, true); break;
            case ReactionKind.RockPulse: DrawLasers(context, t, false); break;
            case ReactionKind.StopWall: DrawStopWall(context, t); break;
            case ReactionKind.WarningRing: DrawWarningRing(context, t); break;
            case ReactionKind.BubbleTrail: DrawBubbles(context, t); break;
            case ReactionKind.TripleRipple: DrawTripleRipple(context, t); break;
            case ReactionKind.TwinLightBeams: DrawTwinBeams(context, t); break;
            case ReactionKind.ReverseMeteor: DrawMeteors(context, t); break;
            case ReactionKind.PewShot: DrawPew(context, t); break;
            case ReactionKind.PinchSpark: DrawSparkles(context, t, false); break;
            case ReactionKind.PortalGate: DrawPortal(context, t); break;
        }
    }

    private Point Anchor => new(Bounds.Width * _anchorX, Bounds.Height * _anchorY);
    private double Unit => Math.Min(Bounds.Width, Bounds.Height) * UnifiedEffectScale;

    private void DrawGrabMagnet(DrawingContext context, double t)
    {
        var a = Anchor;
        var fade = Fade(t, 2.6);
        using (context.PushOpacity(fade))
        {
            for (var ring = 0; ring < 4; ring++)
            {
                var progress = (t * 0.85 + ring * 0.24) % 1.0;
                var radius = Unit * (0.30 * (1 - progress) + 0.03);
                context.DrawEllipse(null, EffectPen(AccentBrushes[(ring + 2) % 6], 3), a, radius, radius);
            }

            foreach (var p in _particles)
            {
                var progress = Math.Clamp(t / 1.8, 0, 1);
                var x = Lerp(p.X * Bounds.Width, a.X, EaseOutCubic(progress));
                var y = Lerp(p.Y * Bounds.Height, a.Y, EaseOutCubic(progress));
                context.DrawEllipse(AccentBrushes[p.BrushIndex], null, new Point(x, y), p.Size / 2, p.Size / 2);
            }

            DrawEmoji(context, "🧲", a, Unit * 0.16 * PopScale(t));
        }
    }

    private void DrawGripPulse(DrawingContext context, double t)
    {
        var a = Anchor;
        using (context.PushOpacity(Fade(t, 2.5)))
        {
            for (var i = 0; i < 3; i++)
            {
                var progress = Math.Clamp((t - i * 0.16) / 0.9, 0, 1);
                var rx = Unit * (0.06 + 0.22 * progress);
                var ry = rx * (0.62 + 0.24 * Math.Sin(t * 7));
                context.DrawEllipse(null, EffectPen(B("#7064D2FF"), 4 - i), a, rx, ry);
            }
            DrawEmoji(context, "✊", a, Unit * 0.19 * (1 - 0.08 * Math.Sin(t * 10)));
        }
    }

    private void DrawHolyHalo(DrawingContext context, double t)
    {
        var a = Anchor;
        using (context.PushOpacity(Fade(t, 3.0)))
        {
            var halo = new Point(a.X, a.Y - Unit * 0.17);
            context.DrawEllipse(null, EffectPen(B("#FFFFD86B"), 7), halo, Unit * 0.16, Unit * 0.045);
            context.DrawEllipse(null, EffectPen(B("#80FFF3B0"), 15), halo, Unit * 0.17, Unit * 0.055);
            DrawRadialBurst(context, a, Unit * 0.10, Unit * (0.20 + 0.03 * Math.Sin(t * 5)), 16, B("#FFFFE69A"), 3);
            for (var i = 0; i < 12; i++)
            {
                var x = a.X + Math.Sin(i * 1.7 + t) * Unit * 0.30;
                var y = a.Y - Unit * 0.30 + ((t * 0.15 + i * 0.11) % 0.55) * Unit;
                DrawEmoji(context, "✦", new Point(x, y), Unit * 0.035);
            }
            DrawEmoji(context, "🙏", a, Unit * 0.19 * PopScale(t));
        }
    }

    private void DrawPointArrow(DrawingContext context, double t)
    {
        var a = Anchor;
        var direction = _anchorX > 0.62 ? -1 : 1;
        var progress = EaseOutCubic(Math.Clamp(t / 0.65, 0, 1));
        var end = new Point(a.X + direction * Unit * 0.38 * progress, a.Y - Unit * 0.10 * progress);
        using (context.PushOpacity(Fade(t, 2.3)))
        {
            context.DrawLine(EffectPen(B("#FF64D2FF"), 9), a, end);
            context.DrawLine(EffectPen(B("#8064D2FF"), 18), a, end);
            var head = Unit * 0.045;
            context.DrawLine(EffectPen(B("#FFFFFFFF"), 6), end,
                new Point(end.X - direction * head, end.Y - head));
            context.DrawLine(EffectPen(B("#FFFFFFFF"), 6), end,
                new Point(end.X - direction * head, end.Y + head));
            DrawEmoji(context, "👉", a, Unit * 0.16 * PopScale(t));
        }
    }

    private void DrawPhoneWave(DrawingContext context, double t)
    {
        var a = Anchor;
        using (context.PushOpacity(Fade(t, 2.7)))
        {
            DrawEmoji(context, "☎️", a, Unit * 0.17 * PopScale(t));
            for (var side = -1; side <= 1; side += 2)
            {
                for (var i = 0; i < 3; i++)
                {
                    var p = Math.Clamp((t - i * 0.16) / 0.8, 0, 1);
                    var x = a.X + side * Unit * (0.12 + p * 0.18);
                    var length = Unit * (0.06 + p * 0.10);
                    context.DrawLine(EffectPen(B("#FF30D158"), 4),
                        new Point(x, a.Y - length), new Point(x, a.Y + length));
                }
            }
        }
    }

    private void DrawTripleStars(DrawingContext context, double t)
    {
        var a = Anchor;
        using (context.PushOpacity(Fade(t, 2.7)))
        {
            for (var i = 0; i < 3; i++)
            {
                var angle = -Math.PI / 2 + i * Math.PI * 2 / 3 + t * 0.9;
                var radius = Unit * (0.16 + 0.025 * Math.Sin(t * 5 + i));
                var p = new Point(a.X + Math.Cos(angle) * radius, a.Y + Math.Sin(angle) * radius);
                DrawEmoji(context, i == 1 ? "🌟" : "⭐", p, Unit * (0.08 + i * 0.012));
            }
            DrawLabel(context, "3", a, Unit * 0.15, B("#FFFFFFFF"));
        }
    }

    private void DrawTimeoutRing(DrawingContext context, double t)
    {
        var a = Anchor;
        using (context.PushOpacity(Fade(t, 2.8)))
        {
            var radius = Unit * 0.20;
            DrawArc(context, a, radius, -Math.PI / 2, -Math.PI / 2 + Math.Min(t / 1.4, 1) * Math.PI * 2,
                B("#FFFF9F0A"), 8, 48);
            context.DrawEllipse(B("#5A11131B"), EffectPen(B("#90FFFFFF"), 2), a, radius * 0.75, radius * 0.75);
            DrawLabel(context, "TIME", a, Unit * 0.065, B("#FFFFFFFF"));
            var hand = new Point(a.X + Math.Cos(t * 4 - Math.PI / 2) * radius * 0.55,
                a.Y + Math.Sin(t * 4 - Math.PI / 2) * radius * 0.55);
            context.DrawLine(EffectPen(B("#FFFFFFFF"), 4), a, hand);
        }
    }

    private void DrawXSlash(DrawingContext context, double t)
    {
        var a = Anchor;
        var p = EaseOutCubic(Math.Clamp(t / 0.45, 0, 1));
        var r = Unit * 0.24 * p;
        using (context.PushOpacity(Fade(t, 2.4)))
        {
            context.DrawLine(EffectPen(B("#80FF453A"), 18), new Point(a.X - r, a.Y - r), new Point(a.X + r, a.Y + r));
            context.DrawLine(EffectPen(B("#FFFF453A"), 7), new Point(a.X - r, a.Y - r), new Point(a.X + r, a.Y + r));
            context.DrawLine(EffectPen(B("#80FF453A"), 18), new Point(a.X + r, a.Y - r), new Point(a.X - r, a.Y + r));
            context.DrawLine(EffectPen(B("#FFFF453A"), 7), new Point(a.X + r, a.Y - r), new Point(a.X - r, a.Y + r));
        }
    }

    private void DrawHeart(DrawingContext context, double t, bool doublePulse)
    {
        var a = Anchor;
        using (context.PushOpacity(Fade(t, 3.0)))
        {
            foreach (var p in _particles)
            {
                var x = (p.X + p.Vx * t + Math.Sin(t * 2.2 + p.Phase) * 0.018) * Bounds.Width;
                var y = (p.Y + p.Vy * t) * Bounds.Height;
                DrawEmoji(context, p.BrushIndex % 3 == 0 ? "💖" : "❤", new Point(x, y), p.Size);
            }

            var pulse = 1 + Math.Sin(t * (doublePulse ? 10 : 6)) * (doublePulse ? 0.11 : 0.07);

            if (UseUnifiedLargeEffectProfile)
            {
                var ringRadius = Unit * (0.21 + Math.Sin(t * 3.2) * 0.018);
                for (var i = 0; i < 8; i++)
                {
                    var angle = i * Math.PI * 2 / 8 + t * 0.55;
                    var heart = new Point(a.X + Math.Cos(angle) * ringRadius,
                        a.Y + Math.Sin(angle) * ringRadius * 0.72);
                    DrawEmoji(context, i % 2 == 0 ? "💕" : "✨", heart, Unit * 0.052);
                }
                context.DrawEllipse(null, EffectPen(B("#50FF73B9"), 14), a,
                    Unit * 0.28 * pulse, Unit * 0.23 * pulse);
            }

            if (doublePulse)
            {
                context.DrawEllipse(null, EffectPen(B("#80FF2D55"), 8), a, Unit * 0.24 * pulse, Unit * 0.20 * pulse);
                DrawEmoji(context, "💖", a, Unit * 0.24 * pulse);
            }
            else
            {
                DrawEmoji(context, "❤", a, Unit * 0.25 * pulse);
            }
        }
    }

    private void DrawSparkles(DrawingContext context, double t, bool pink)
    {
        var a = Anchor;
        using (context.PushOpacity(Fade(t, 2.5)))
        {
            foreach (var p in _particles)
            {
                var x = (p.X + p.Vx * t) * Bounds.Width;
                var y = (p.Y + p.Vy * t + 0.10 * t * t) * Bounds.Height;
                var brush = pink ? B("#FFFF73B9") : AccentBrushes[p.BrushIndex];
                DrawStar(context, new Point(x, y), p.Size * (0.6 + Math.Abs(Math.Sin(t * 8 + p.Phase))), brush);
            }
            DrawEmoji(context, pink ? "🩷" : "✨", a, Unit * 0.13 * PopScale(t));
        }
    }

    private void DrawPurpleArc(DrawingContext context, double t)
    {
        var a = Anchor;
        using (context.PushOpacity(Fade(t, 2.4)))
        {
            for (var arc = 0; arc < 4; arc++)
            {
                var start = new Point(a.X - Unit * 0.23, a.Y + (arc - 1.5) * Unit * 0.05);
                var points = new List<Point> { start };
                for (var i = 1; i <= 8; i++)
                {
                    points.Add(new Point(
                        start.X + i * Unit * 0.058,
                        start.Y + Math.Sin(i * 2.1 + t * 12 + arc) * Unit * 0.04));
                }
                DrawPolyline(context, points, EffectPen(arc % 2 == 0 ? B("#FFBF5AF2") : B("#FF64D2FF"), 4));
            }
            DrawEmoji(context, "⚡", a, Unit * 0.15 * PopScale(t));
        }
    }

    private void DrawCameraFlash(DrawingContext context, double t)
    {
        var a = Anchor;
        var flash = Math.Clamp(1 - t / 0.22, 0, 1) * 0.82;
        if (flash > 0)
        {
            using (context.PushOpacity(flash))
                context.DrawRectangle(B("#FFFFFFFF"), null, new Rect(0, 0, Bounds.Width, Bounds.Height));
        }

        using (context.PushOpacity(Fade(t, 2.5)))
        {
            var w = Unit * 0.42;
            var h = Unit * 0.28;
            DrawFocusCorners(context, new Rect(a.X - w / 2, a.Y - h / 2, w, h), B("#FFFFFFFF"), 5);
            DrawEmoji(context, "📸", a, Unit * 0.15 * PopScale(t));
        }
    }

    private void DrawThumb(DrawingContext context, double t, bool up)
    {
        var a = Anchor;
        var appear = Math.Clamp(t / 0.24, 0, 1);
        var scale = 0.45 + 0.55 * EaseOutBack(appear);
        var yOffset = up ? -Unit * 0.03 * Math.Sin(t * 3) : Unit * 0.15 * EaseOutCubic(Math.Clamp(t / 0.8, 0, 1));
        using (context.PushOpacity(appear * Fade(t, 2.5)))
        {
            var center = new Point(a.X, a.Y + yOffset);
            context.DrawEllipse(B("#8011131B"), null, center, Unit * 0.17, Unit * 0.17);
            DrawEmoji(context, up ? "👍🏻" : "👎🏻", center, Unit * 0.27 * scale);
            if (up)
                DrawRadialBurst(context, center, Unit * 0.16, Unit * 0.29, 12, B("#FFFFD60A"), 3);
        }
    }

    private void DrawImpact(DrawingContext context, double t)
    {
        var a = Anchor;
        using (context.PushOpacity(Fade(t, 2.4)))
        {
            for (var i = 0; i < 4; i++)
            {
                var p = Math.Clamp((t - i * 0.10) / 0.8, 0, 1);
                var r = Unit * (0.05 + 0.28 * EaseOutCubic(p));
                context.DrawEllipse(null, EffectPen(AccentBrushes[(i + 4) % 6], 7 - i), a, r, r);
            }
            DrawRadialBurst(context, a, Unit * 0.12, Unit * 0.34, 18, B("#FFFF9F0A"), 5);
            DrawLabel(context, "BAM!", new Point(a.X, a.Y - Unit * 0.04), Unit * 0.10, B("#FFFFFFFF"));
            DrawEmoji(context, "✊", new Point(a.X, a.Y + Unit * 0.12), Unit * 0.15 * PopScale(t));
        }
    }

    private void DrawFourStars(DrawingContext context, double t)
    {
        var a = Anchor;
        using (context.PushOpacity(Fade(t, 2.6)))
        {
            var radius = Unit * (0.15 + 0.03 * Math.Sin(t * 5));
            for (var i = 0; i < 4; i++)
            {
                var angle = Math.PI / 4 + i * Math.PI / 2 + t * 0.4;
                var p = new Point(a.X + Math.Cos(angle) * radius, a.Y + Math.Sin(angle) * radius);
                DrawStar(context, p, Unit * 0.055, AccentBrushes[i]);
            }
            DrawLabel(context, "4", a, Unit * 0.16, B("#FFFFFFFF"));
        }
    }

    private void DrawMute(DrawingContext context, double t)
    {
        var a = Anchor;
        using (context.PushOpacity(Fade(t, 2.5)))
        {
            DrawEmoji(context, "🔊", a, Unit * 0.17 * PopScale(t));
            for (var i = 0; i < 3; i++)
            {
                var x = a.X + Unit * (0.12 + i * 0.06);
                var h = Unit * (0.05 + i * 0.025) * Math.Max(0, 1 - t / 1.2);
                context.DrawLine(EffectPen(B("#FF64D2FF"), 4), new Point(x, a.Y - h), new Point(x, a.Y + h));
            }
            context.DrawLine(EffectPen(B("#FFFF453A"), 9),
                new Point(a.X - Unit * 0.17, a.Y - Unit * 0.17),
                new Point(a.X + Unit * 0.17, a.Y + Unit * 0.17));
        }
    }

    private void DrawOk(DrawingContext context, double t)
    {
        var a = Anchor;
        using (context.PushOpacity(Fade(t, 2.5)))
        {
            var p = EaseOutBack(Math.Clamp(t / 0.45, 0, 1));
            var r = Unit * 0.20 * p;
            context.DrawEllipse(B("#3030D158"), EffectPen(B("#FF30D158"), 8), a, r, r);
            DrawLabel(context, "✓", a, Unit * 0.18 * p, B("#FFFFFFFF"));
            DrawRadialBurst(context, a, r * 1.05, r * 1.35, 12, B("#FF30D158"), 3);
        }
    }

    private void DrawSpotlight(DrawingContext context, double t)
    {
        var a = Anchor;
        using (context.PushOpacity(Fade(t, 2.8)))
        {
            var top = new Point(a.X, 0);
            for (var i = -5; i <= 5; i++)
            {
                var end = new Point(a.X + i * Unit * 0.035, a.Y + Unit * 0.22);
                context.DrawLine(EffectPen(B("#18FFFFFF"), 18), top, end);
            }
            context.DrawEllipse(B("#50FFD60A"), null, new Point(a.X, a.Y + Unit * 0.12), Unit * 0.24, Unit * 0.07);
            DrawLabel(context, "1", a, Unit * 0.22 * PopScale(t), B("#FFFFFFFF"));
        }
    }

    private void DrawShield(DrawingContext context, double t)
    {
        var a = Anchor;
        using (context.PushOpacity(Fade(t, 3.0)))
        {
            for (var i = 0; i < 3; i++)
            {
                var p = Math.Clamp((t - i * 0.12) / 0.7, 0, 1);
                var r = Unit * (0.08 + 0.20 * EaseOutCubic(p));
                context.DrawEllipse(B(i == 0 ? "#1830D1FF" : "#0864D2FF"),
                    EffectPen(i % 2 == 0 ? B("#FF64D2FF") : B("#FF30D158"), 4), a, r, r);
            }
            DrawPolygon(context, a, Unit * 0.20, 6, t * 0.22, null, EffectPen(B("#FFFFFFFF"), 3));
            DrawEmoji(context, "✋", a, Unit * 0.18 * PopScale(t));
        }
    }

    private void DrawBalloons(DrawingContext context, double t)
    {
        using (context.PushOpacity(Fade(t, 3.5)))
        {
            foreach (var p in _particles)
            {
                var x = (p.X + p.Vx * t + Math.Sin(t * 2 + p.Phase) * 0.025) * Bounds.Width;
                var y = (p.Y + p.Vy * t) * Bounds.Height;
                var center = new Point(x, y);
                DrawEmoji(context, "🎈", center, p.Size);

                if (UseUnifiedLargeEffectProfile && ((int)(p.Phase * 10) & 1) == 0)
                {
                    var sparkle = new Point(x + Math.Sin(t * 5 + p.Phase) * p.Size * 0.55,
                        y - p.Size * 0.62);
                    DrawStar(context, sparkle, Math.Max(4, p.Size * 0.10),
                        AccentBrushes[p.BrushIndex]);
                }
            }

            if (UseUnifiedLargeEffectProfile)
            {
                var shimmer = 0.84 + Math.Sin(t * 7) * 0.16;
                DrawRadialBurst(context, Anchor, Unit * 0.12, Unit * 0.30, 18, B("#BFFFFFFF"), 3);
                DrawEmoji(context, "✨", new Point(Anchor.X - Unit * 0.18, Anchor.Y - Unit * 0.13),
                    Unit * 0.10 * shimmer);
                DrawEmoji(context, "✨", new Point(Anchor.X + Unit * 0.18, Anchor.Y - Unit * 0.11),
                    Unit * 0.09 * (1.8 - shimmer));
            }

            DrawEmoji(context, "✌️", Anchor, Unit * 0.18 * PopScale(t));
        }
    }

    private void DrawConfetti(DrawingContext context, double t, bool sideCannons)
    {
        using (context.PushOpacity(Fade(t, 3.3)))
        {
            foreach (var p in _particles)
            {
                var x = (p.X + p.Vx * t) * Bounds.Width;
                var y = (p.Y + p.Vy * t + 0.34 * t * t) * Bounds.Height;
                var width = p.Size * (0.65 + Math.Abs(Math.Sin(t * 8 + p.Phase)) * 0.55);
                context.DrawRectangle(AccentBrushes[p.BrushIndex], null,
                    new Rect(x, y, width, p.Size * 0.55), 2, 2);
            }

            if (UseUnifiedLargeEffectProfile)
            {
                var glow = Math.Clamp(1 - t / 1.8, 0, 1);
                using (context.PushOpacity(glow))
                {
                    DrawRadialBurst(context, Anchor, Unit * 0.06, Unit * 0.32, 24, B("#90FFD60A"), 4);
                    DrawEmoji(context, "✨", new Point(Bounds.Width * 0.32, Bounds.Height * 0.22), Unit * 0.10);
                    DrawEmoji(context, "✨", new Point(Bounds.Width * 0.68, Bounds.Height * 0.20), Unit * 0.10);
                }
            }

            if (sideCannons)
            {
                var cannonSize = Unit * (UseUnifiedLargeEffectProfile ? 0.21 : 0.14);
                DrawEmoji(context, "🎊", new Point(Bounds.Width * 0.14, Bounds.Height * 0.74), cannonSize);
                DrawEmoji(context, "🎊", new Point(Bounds.Width * 0.86, Bounds.Height * 0.74), cannonSize);
            }
            else
            {
                DrawEmoji(context, "🎉", new Point(Bounds.Width / 2, Bounds.Height * 0.25),
                    Unit * (UseUnifiedLargeEffectProfile ? 0.23 : 0.17));
            }
        }
    }

    private void DrawLasers(DrawingContext context, double t, bool fullScreen)
    {
        var center = fullScreen ? new Point(Bounds.Width / 2, Bounds.Height / 2) : Anchor;
        using (context.PushOpacity(Fade(t, fullScreen ? 2.8 : 2.6)))
        {
            context.DrawRectangle(B(fullScreen ? "#2810182A" : "#16101820"), null, new Rect(0, 0, Bounds.Width, Bounds.Height));
            var count = fullScreen ? 18 : 10;
            for (var i = 0; i < count; i++)
            {
                var angle = i * Math.PI * 2 / count + Math.Sin(t * 3 + i) * 0.10;
                var length = Unit * (0.28 + 0.18 * Math.Sin(t * 8 + i));
                var endpoint = new Point(center.X + Math.Cos(angle) * length, center.Y + Math.Sin(angle) * length);
                var brush = AccentBrushes[i % AccentBrushes.Length];
                var thickness = i % 3 == 0 ? 7.0 : 3.0;
                var glowScale = 1.0 + 0.10 * Math.Sin(t * 12 + i * 0.7);

                DrawBeamWithGlow(context, center, endpoint, brush, thickness * glowScale);

                if (fullScreen)
                {
                    var sparkRadius = Unit * (0.014 + 0.010 * Math.Sin(t * 6 + i));
                    context.DrawEllipse(B("#30FFFFFF"), null, endpoint, sparkRadius * 3.0, sparkRadius * 3.0);
                    context.DrawEllipse(B("#B0FFFFFF"), null, endpoint, sparkRadius, sparkRadius);

                    var back = new Point(
                        endpoint.X - Math.Cos(angle) * Unit * 0.020,
                        endpoint.Y - Math.Sin(angle) * Unit * 0.020);
                    DrawRadialBurst(context, endpoint, sparkRadius * 0.8, sparkRadius * 2.6, 6, brush, 1.8);
                    context.DrawLine(EffectPen(B("#60FFFFFF"), 1.4), back, endpoint);
                }
            }

            context.DrawEllipse(B("#18FFFFFF"), null, center, Unit * 0.06, Unit * 0.06);
            context.DrawEllipse(B("#66FFFFFF"), null, center, Unit * 0.018, Unit * 0.018);
            DrawEmoji(context, "🤘", center, Unit * 0.18 * PopScale(t));
        }
    }

    private void DrawStopWall(DrawingContext context, double t)
    {
        var a = Anchor;
        var scale = EaseOutBack(Math.Clamp(t / 0.42, 0, 1));
        using (context.PushOpacity(Fade(t, 2.6)))
        {
            DrawPolygon(context, a, Unit * 0.22 * scale, 8, Math.PI / 8,
                B("#D9FF3B30"), EffectPen(B("#FFFFFFFF"), 6));
            DrawLabel(context, "STOP", a, Unit * 0.085 * scale, B("#FFFFFFFF"));
            DrawRadialBurst(context, a, Unit * 0.24, Unit * 0.34, 12, B("#FFFF453A"), 4);
        }
    }

    private void DrawWarningRing(DrawingContext context, double t)
    {
        var a = Anchor;
        using (context.PushOpacity(Fade(t, 2.7)))
        {
            for (var i = 0; i < 4; i++)
            {
                var p = (t * 0.75 + i * 0.22) % 1;
                var r = Unit * (0.28 * (1 - p) + 0.05);
                context.DrawEllipse(null, EffectPen(i % 2 == 0 ? B("#FFFF453A") : B("#FFFF9F0A"), 5), a, r, r);
            }
            DrawEmoji(context, "⚠️", a, Unit * 0.17 * PopScale(t));
        }
    }

    private void DrawBubbles(DrawingContext context, double t)
    {
        using (context.PushOpacity(Fade(t, 2.8)))
        {
            foreach (var p in _particles)
            {
                var x = (p.X + p.Vx * t + Math.Sin(t * 2 + p.Phase) * 0.018) * Bounds.Width;
                var y = (p.Y + p.Vy * t) * Bounds.Height;
                context.DrawEllipse(B("#2864D2FF"), EffectPen(B("#BFFFFFFF"), 2), new Point(x, y), p.Size / 2, p.Size / 2);
            }
            DrawLabel(context, "3", Anchor, Unit * 0.14, B("#FFFFFFFF"));
        }
    }

    private void DrawTripleRipple(DrawingContext context, double t)
    {
        var a = Anchor;
        using (context.PushOpacity(Fade(t, 2.7)))
        {
            for (var i = 0; i < 3; i++)
            {
                var p = Math.Clamp((t - i * 0.22) / 1.0, 0, 1);
                var r = Unit * (0.04 + 0.25 * EaseOutCubic(p));
                using (context.PushOpacity(1 - p * 0.75))
                    context.DrawEllipse(null, EffectPen(AccentBrushes[(i + 2) % 6], 7 - i), a, r, r);
            }
            DrawLabel(context, "III", a, Unit * 0.10, B("#FFFFFFFF"));
        }
    }

    private void DrawTwinBeams(DrawingContext context, double t)
    {
        var a = Anchor;
        using (context.PushOpacity(Fade(t, 2.8)))
        {
            for (var side = -1; side <= 1; side += 2)
            {
                var x = a.X + side * Unit * 0.10;
                var top = a.Y - Unit * (0.20 + 0.18 * EaseOutCubic(Math.Clamp(t / 0.7, 0, 1)));
                context.DrawLine(EffectPen(B("#4064D2FF"), 26), new Point(x, a.Y + Unit * 0.16), new Point(x, top));
                context.DrawLine(EffectPen(B("#FFFFFFFF"), 5), new Point(x, a.Y + Unit * 0.16), new Point(x, top));
                context.DrawLine(EffectPen(B("#FFFFFFFF"), 5), new Point(x, top), new Point(x - Unit * 0.035, top + Unit * 0.045));
                context.DrawLine(EffectPen(B("#FFFFFFFF"), 5), new Point(x, top), new Point(x + Unit * 0.035, top + Unit * 0.045));
            }
            DrawEmoji(context, "✌️", a, Unit * 0.15 * PopScale(t));
        }
    }

    private void DrawMeteors(DrawingContext context, double t)
    {
        using (context.PushOpacity(Fade(t, 2.8)))
        {
            foreach (var p in _particles)
            {
                var x = (p.X + p.Vx * t) * Bounds.Width;
                var y = (p.Y + p.Vy * t) * Bounds.Height;
                var head = new Point(x, y);
                var tail = new Point(x + p.Size * 2.5, y - p.Size * 3.5);
                DrawBeamWithGlow(context, tail, head, AccentBrushes[p.BrushIndex], 2.4);
                context.DrawEllipse(B("#35FFFFFF"), null, head, p.Size * 0.70, p.Size * 0.70);
                context.DrawEllipse(B("#FFFFFFFF"), null, head, p.Size / 3, p.Size / 3);
            }
            DrawEmoji(context, "☄️", Anchor, Unit * 0.14 * PopScale(t));
        }
    }

    private void DrawPew(DrawingContext context, double t)
    {
        var a = Anchor;
        var direction = _anchorX > 0.62 ? -1 : 1;
        var p = EaseOutCubic(Math.Clamp(t / 0.7, 0, 1));
        var projectile = new Point(a.X + direction * Unit * 0.48 * p, a.Y - Unit * 0.06 * p);
        using (context.PushOpacity(Fade(t, 2.4)))
        {
            context.DrawLine(EffectPen(B("#505E5CE6"), 20), a, projectile);
            context.DrawLine(EffectPen(B("#FFFFFFFF"), 5), a, projectile);
            context.DrawEllipse(B("#FFFFD60A"), null, projectile, Unit * 0.025, Unit * 0.025);
            DrawLabel(context, "PEW!", new Point(projectile.X, projectile.Y - Unit * 0.08), Unit * 0.065, B("#FFFFFFFF"));
            DrawEmoji(context, "👉", a, Unit * 0.14 * PopScale(t));
        }
    }

    private void DrawPortal(DrawingContext context, double t)
    {
        var a = Anchor;
        using (context.PushOpacity(Fade(t, 3.0)))
        {
            for (var i = 0; i < 5; i++)
            {
                var phase = t * (1.5 + i * 0.1) + i * 0.6;
                var rx = Unit * (0.18 + i * 0.018 + Math.Sin(phase) * 0.012);
                var ry = Unit * (0.27 + i * 0.014);
                context.DrawEllipse(null, EffectPen(AccentBrushes[(i + 2) % 6], 5), a, rx, ry);
            }
            foreach (var p in _particles)
            {
                var angle = p.Phase + t * 2.2;
                var radiusX = Unit * (0.20 + 0.02 * Math.Sin(p.Phase * 3));
                var radiusY = Unit * 0.29;
                var q = new Point(a.X + Math.Cos(angle) * radiusX, a.Y + Math.Sin(angle) * radiusY);
                context.DrawEllipse(AccentBrushes[p.BrushIndex], null, q, p.Size / 2, p.Size / 2);
            }
            DrawEmoji(context, "🌀", a, Unit * 0.14 * PopScale(t));
        }
    }

    private void DrawRain(DrawingContext context, double t)
    {
        using (context.PushOpacity(Fade(t, 3.0)))
        {
            context.DrawRectangle(B("#35151C28"), null, new Rect(0, 0, Bounds.Width, Bounds.Height));
            context.DrawRectangle(B("#10000000"), null, new Rect(0, Bounds.Height * 0.72, Bounds.Width, Bounds.Height * 0.28));
            DrawEmoji(context, "🌧️", new Point(Bounds.Width / 2, Bounds.Height * 0.20), Unit * 0.15);

            foreach (var p in _particles)
            {
                var yNormalized = (p.Y + p.Vy * t) % 1.25;
                if (yNormalized < 0)
                    yNormalized += 1.25;

                var x = (p.X + p.Vx * t) * Bounds.Width;
                var y = yNormalized * Bounds.Height;
                var head = new Point(x, y);
                var tail = new Point(x - p.Size * 0.28, y + p.Size * 1.05);
                var streakWidth = Math.Max(1.6, p.Size * 0.10);

                using (context.PushOpacity(0.16))
                    context.DrawLine(EffectPen(B("#78D6FFFF"), streakWidth * 2.4), head, tail);
                context.DrawLine(EffectPen(B("#8AD8FFFF"), streakWidth), head, tail);
                context.DrawLine(EffectPen(B("#E8FFFFFF"), Math.Max(0.9, streakWidth * 0.38)), head, tail);
                context.DrawEllipse(B("#70E6FFFF"), null, head, streakWidth * 0.95, streakWidth * 1.2);
                context.DrawEllipse(B("#F8FFFFFF"), null, new Point(head.X - streakWidth * 0.16, head.Y + streakWidth * 0.12),
                    streakWidth * 0.24, streakWidth * 0.34);

                if (y > Bounds.Height * 0.78 && (((int)(p.Phase * 9) + (int)(t * 14)) % 5 == 0))
                {
                    var splashCenter = new Point(x - p.Size * 0.14, Math.Min(Bounds.Height - 4, y + p.Size * 0.24));
                    DrawRainSplash(context, splashCenter, p.Size * 0.20);
                }
            }
        }
    }

    private void DrawFireworks(DrawingContext context, double t)
    {
        using (context.PushOpacity(Fade(t, 3.4)))
        {
            context.DrawRectangle(B("#30060A18"), null, new Rect(0, 0, Bounds.Width, Bounds.Height));
            DrawFireworkBurst(context, new Point(Bounds.Width * 0.28, Bounds.Height * 0.32), t, 0.00, 0);
            DrawFireworkBurst(context, new Point(Bounds.Width * 0.72, Bounds.Height * 0.28), t, 0.34, 2);
            DrawFireworkBurst(context, new Point(Bounds.Width * 0.52, Bounds.Height * 0.58), t, 0.72, 4);

            if (UseUnifiedLargeEffectProfile)
            {
                DrawFireworkBurst(context, new Point(Bounds.Width * 0.12, Bounds.Height * 0.54), t, 0.52, 1);
                DrawFireworkBurst(context, new Point(Bounds.Width * 0.88, Bounds.Height * 0.52), t, 0.90, 5);
                DrawEmoji(context, "🎆", new Point(Bounds.Width * 0.50, Bounds.Height * 0.18), Unit * 0.18);
            }

            var thumbSize = Unit * (UseUnifiedLargeEffectProfile ? 0.18 : 0.13);
            DrawEmoji(context, "👍🏻", new Point(Bounds.Width * 0.40, Bounds.Height * 0.77), thumbSize);
            DrawEmoji(context, "👍🏻", new Point(Bounds.Width * 0.60, Bounds.Height * 0.77), thumbSize);
        }
    }

    private void DrawFireworkBurst(DrawingContext context, Point center, double t, double delay, int brushOffset)
    {
        var local = t - delay;
        if (local < 0)
            return;

        local %= 1.25;
        var progress = Math.Clamp(local / 1.05, 0, 1);
        var radius = Unit * (0.03 + 0.22 * EaseOutCubic(progress));
        using (context.PushOpacity(1 - progress))
        {
            for (var i = 0; i < 22; i++)
            {
                var angle = i * Math.PI * 2 / 22;
                var start = new Point(center.X + Math.Cos(angle) * radius * 0.52,
                    center.Y + Math.Sin(angle) * radius * 0.52);
                var end = new Point(center.X + Math.Cos(angle) * radius,
                    center.Y + Math.Sin(angle) * radius);
                var brush = AccentBrushes[(i + brushOffset) % AccentBrushes.Length];
                DrawBeamWithGlow(context, start, end, brush, 2.0);
                context.DrawEllipse(B("#E8FFFFFF"), null, end, Unit * 0.006, Unit * 0.006);
            }

            if (progress > 0.18)
            {
                for (var i = 0; i < 12; i++)
                {
                    var angle = i * Math.PI * 2 / 12 + delay;
                    var sparkRadius = radius * (0.35 + progress * 0.55);
                    var spark = new Point(center.X + Math.Cos(angle) * sparkRadius,
                        center.Y + Math.Sin(angle) * sparkRadius);
                    context.DrawEllipse(AccentBrushes[(i + brushOffset + 2) % AccentBrushes.Length], null,
                        spark, Unit * 0.0065, Unit * 0.0065);
                }
            }
        }
    }

    private void DrawRadialBurst(DrawingContext context, Point center, double inner, double outer,
        int count, IBrush brush, double thickness)
    {
        for (var i = 0; i < count; i++)
        {
            var angle = i * Math.PI * 2 / count;
            context.DrawLine(EffectPen(brush, thickness),
                new Point(center.X + Math.Cos(angle) * inner, center.Y + Math.Sin(angle) * inner),
                new Point(center.X + Math.Cos(angle) * outer, center.Y + Math.Sin(angle) * outer));
        }
    }

    private static void DrawBeamWithGlow(DrawingContext context, Point start, Point end, IBrush brush, double thickness)
    {
        using (context.PushOpacity(0.18))
            context.DrawLine(LaserPen(brush, thickness * 2.8), start, end);
        using (context.PushOpacity(0.34))
            context.DrawLine(LaserPen(brush, thickness * 1.65), start, end);
        context.DrawLine(LaserPen(brush, thickness), start, end);
        context.DrawLine(EffectPen(B("#F8FFFFFF"), Math.Max(0.85, thickness * 0.24)), start, end);
    }

    private static void DrawRainSplash(DrawingContext context, Point center, double size)
    {
        DrawArc(context,
            center,
            size,
            Math.PI * 0.08,
            Math.PI * 0.92,
            B("#C8E9FFFF"),
            Math.Max(0.8, size * 0.18),
            10);
        context.DrawEllipse(B("#58E3FFFF"), null, new Point(center.X - size * 0.20, center.Y - size * 0.06),
            size * 0.16, size * 0.10);
        context.DrawEllipse(B("#58E3FFFF"), null, new Point(center.X + size * 0.22, center.Y - size * 0.04),
            size * 0.14, size * 0.10);
    }

    private static void DrawArc(DrawingContext context, Point center, double radius,
        double startAngle, double endAngle, IBrush brush, double thickness, int segments)
    {
        var points = new List<Point>(segments + 1);
        for (var i = 0; i <= segments; i++)
        {
            var angle = Lerp(startAngle, endAngle, i / (double)segments);
            points.Add(new Point(center.X + Math.Cos(angle) * radius, center.Y + Math.Sin(angle) * radius));
        }
        DrawPolyline(context, points, EffectPen(brush, thickness));
    }

    private static void DrawPolyline(DrawingContext context, IReadOnlyList<Point> points, Pen pen)
    {
        for (var i = 1; i < points.Count; i++)
            context.DrawLine(pen, points[i - 1], points[i]);
    }

    private static void DrawPolygon(DrawingContext context, Point center, double radius, int sides,
        double rotation, IBrush? fill, Pen? pen)
    {
        var geometry = new StreamGeometry();
        using (var g = geometry.Open())
        {
            for (var i = 0; i < sides; i++)
            {
                var angle = rotation + i * Math.PI * 2 / sides;
                var p = new Point(center.X + Math.Cos(angle) * radius, center.Y + Math.Sin(angle) * radius);
                if (i == 0)
                    g.BeginFigure(p, fill is not null);
                else
                    g.LineTo(p);
            }
            g.EndFigure(true);
        }
        context.DrawGeometry(fill, pen, geometry);
    }

    private static void DrawStar(DrawingContext context, Point center, double radius, IBrush brush)
    {
        var geometry = new StreamGeometry();
        using (var g = geometry.Open())
        {
            for (var i = 0; i < 10; i++)
            {
                var angle = -Math.PI / 2 + i * Math.PI / 5;
                var r = i % 2 == 0 ? radius : radius * 0.42;
                var p = new Point(center.X + Math.Cos(angle) * r, center.Y + Math.Sin(angle) * r);
                if (i == 0)
                    g.BeginFigure(p, true);
                else
                    g.LineTo(p);
            }
            g.EndFigure(true);
        }
        context.DrawGeometry(brush, null, geometry);
    }

    private static void DrawFocusCorners(DrawingContext context, Rect rect, IBrush brush, double thickness)
    {
        var pen = EffectPen(brush, thickness);
        var length = Math.Min(rect.Width, rect.Height) * 0.22;
        context.DrawLine(pen, rect.TopLeft, new Point(rect.Left + length, rect.Top));
        context.DrawLine(pen, rect.TopLeft, new Point(rect.Left, rect.Top + length));
        context.DrawLine(pen, rect.TopRight, new Point(rect.Right - length, rect.Top));
        context.DrawLine(pen, rect.TopRight, new Point(rect.Right, rect.Top + length));
        context.DrawLine(pen, rect.BottomLeft, new Point(rect.Left + length, rect.Bottom));
        context.DrawLine(pen, rect.BottomLeft, new Point(rect.Left, rect.Bottom - length));
        context.DrawLine(pen, rect.BottomRight, new Point(rect.Right - length, rect.Bottom));
        context.DrawLine(pen, rect.BottomRight, new Point(rect.Right, rect.Bottom - length));
    }

    private static void DrawEmoji(DrawingContext context, string emoji, Point center, double size)
    {
        size = EmojiSize(size);
        var pixelSize = Math.Clamp((int)Math.Round(size), 16, 512);
        var nativeEmoji = AppServices.EmojiImageProvider?.GetEmojiImage(emoji, pixelSize);
        if (nativeEmoji is not null)
        {
            var imageSize = nativeEmoji.Size;
            var drawSize = Math.Max(size * 1.38, 20);
            var aspect = imageSize.Height <= 0 ? 1 : imageSize.Width / imageSize.Height;
            var width = drawSize * aspect;
            var destination = new Rect(
                center.X - width / 2,
                center.Y - drawSize / 2,
                width,
                drawSize);
            context.DrawImage(nativeEmoji, new Rect(0, 0, imageSize.Width, imageSize.Height), destination);
            return;
        }

        // Desktop and Apple platforms normally expose their native color emoji fonts directly to
        // Avalonia. Android uses AndroidEmojiImageProvider above because Skia font fallback can
        // otherwise produce a white missing-glyph square.
        var text = new FormattedText(
            emoji,
            CultureInfo.CurrentCulture,
            FlowDirection.LeftToRight,
            EmojiTypeface,
            size,
            Brushes.White);
        context.DrawText(text, new Point(center.X - text.Width / 2, center.Y - text.Height / 2));
    }

    private static void DrawMagnetSymbol(DrawingContext context, Point c, double s)
    {
        var radius = s * 0.30;
        DrawArc(context, c, radius, Math.PI * 0.10, Math.PI * 0.90, B("#FF453A"), s * 0.18, 18);
        DrawArc(context, c, radius, Math.PI * 1.10, Math.PI * 1.90, B("#0A84FF"), s * 0.18, 18);
        context.DrawRectangle(B("#E5E5EA"), null,
            new Rect(c.X - radius - s * 0.09, c.Y - s * 0.15, s * 0.18, s * 0.30));
        context.DrawRectangle(B("#E5E5EA"), null,
            new Rect(c.X + radius - s * 0.09, c.Y - s * 0.15, s * 0.18, s * 0.30));
    }

    private static void DrawFistSymbol(DrawingContext context, Point c, double s)
    {
        var skin = B("#FFD2A6");
        context.DrawEllipse(skin, new Pen(B("#7A4A2B"), s * 0.025),
            new Point(c.X, c.Y + s * 0.08), s * 0.31, s * 0.27);
        for (var i = 0; i < 4; i++)
        {
            var x = c.X - s * 0.23 + i * s * 0.155;
            context.DrawEllipse(skin, new Pen(B("#7A4A2B"), s * 0.02),
                new Point(x, c.Y - s * 0.19), s * 0.09, s * 0.11);
        }
    }

    private static void DrawPrayerSymbol(DrawingContext context, Point c, double s)
    {
        var skin = B("#FFD2A6");
        var pen = new Pen(skin, s * 0.18);
        context.DrawLine(pen, new Point(c.X - s * 0.18, c.Y + s * 0.28),
            new Point(c.X - s * 0.03, c.Y - s * 0.28));
        context.DrawLine(pen, new Point(c.X + s * 0.18, c.Y + s * 0.28),
            new Point(c.X + s * 0.03, c.Y - s * 0.28));
        context.DrawEllipse(B("#64D2FF"), null, new Point(c.X, c.Y + s * 0.29), s * 0.26, s * 0.08);
    }

    private static void DrawPointSymbol(DrawingContext context, Point c, double s)
    {
        var skin = B("#FFD2A6");
        context.DrawLine(new Pen(skin, s * 0.20),
            new Point(c.X - s * 0.28, c.Y + s * 0.06),
            new Point(c.X + s * 0.23, c.Y + s * 0.06));
        DrawClosedShape(context,
        [
            new Point(c.X + s * 0.18, c.Y - s * 0.12),
            new Point(c.X + s * 0.42, c.Y + s * 0.06),
            new Point(c.X + s * 0.18, c.Y + s * 0.24)
        ], skin, null);
        context.DrawEllipse(skin, null, new Point(c.X - s * 0.25, c.Y + s * 0.16), s * 0.14, s * 0.18);
    }

    private static void DrawPhoneSymbol(DrawingContext context, Point c, double s)
    {
        DrawArc(context, c, s * 0.29, Math.PI * 0.20, Math.PI * 0.80, B("#30D158"), s * 0.14, 14);
        context.DrawEllipse(B("#30D158"), null, new Point(c.X - s * 0.24, c.Y + s * 0.17), s * 0.11, s * 0.08);
        context.DrawEllipse(B("#30D158"), null, new Point(c.X + s * 0.24, c.Y + s * 0.17), s * 0.11, s * 0.08);
    }

    private static void DrawHeartSymbol(DrawingContext context, Point c, double s, IBrush brush)
    {
        context.DrawEllipse(brush, null, new Point(c.X - s * 0.15, c.Y - s * 0.08), s * 0.19, s * 0.19);
        context.DrawEllipse(brush, null, new Point(c.X + s * 0.15, c.Y - s * 0.08), s * 0.19, s * 0.19);
        DrawClosedShape(context,
        [
            new Point(c.X - s * 0.33, c.Y - s * 0.02),
            new Point(c.X + s * 0.33, c.Y - s * 0.02),
            new Point(c.X, c.Y + s * 0.38)
        ], brush, null);
    }

    private static void DrawLightningSymbol(DrawingContext context, Point c, double s)
    {
        DrawClosedShape(context,
        [
            new Point(c.X + s * 0.02, c.Y - s * 0.42),
            new Point(c.X - s * 0.25, c.Y + s * 0.02),
            new Point(c.X - s * 0.03, c.Y + s * 0.02),
            new Point(c.X - s * 0.12, c.Y + s * 0.42),
            new Point(c.X + s * 0.28, c.Y - s * 0.10),
            new Point(c.X + s * 0.05, c.Y - s * 0.10)
        ], B("#FFD60A"), new Pen(B("#FF9F0A"), s * 0.025));
    }

    private static void DrawCameraSymbol(DrawingContext context, Point c, double s)
    {
        var body = new Rect(c.X - s * 0.38, c.Y - s * 0.25, s * 0.76, s * 0.50);
        context.DrawRectangle(B("#E5E5EA"), new Pen(B("#3A3A3C"), s * 0.025), body);
        context.DrawRectangle(B("#8E8E93"), null,
            new Rect(c.X - s * 0.20, c.Y - s * 0.36, s * 0.30, s * 0.12));
        context.DrawEllipse(B("#1C1C1E"), new Pen(B("#64D2FF"), s * 0.04), c, s * 0.18, s * 0.18);
        context.DrawEllipse(B("#FFFFFF"), null, new Point(c.X + s * 0.25, c.Y - s * 0.13), s * 0.045, s * 0.045);
    }

    private static void DrawThumbSymbol(DrawingContext context, Point c, double s, bool up)
    {
        var sign = up ? -1d : 1d;
        var skin = B("#FFD2A6");
        context.DrawRectangle(skin, new Pen(B("#7A4A2B"), s * 0.02),
            new Rect(c.X - s * 0.05, c.Y - s * 0.03, s * 0.38, s * 0.28));
        context.DrawLine(new Pen(skin, s * 0.19),
            new Point(c.X - s * 0.05, c.Y + sign * s * 0.03),
            new Point(c.X - s * 0.23, c.Y + sign * s * 0.31));
        context.DrawEllipse(skin, null,
            new Point(c.X - s * 0.24, c.Y + sign * s * 0.32), s * 0.10, s * 0.12);
    }

    private static void DrawSpeakerSymbol(DrawingContext context, Point c, double s)
    {
        DrawClosedShape(context,
        [
            new Point(c.X - s * 0.34, c.Y - s * 0.14),
            new Point(c.X - s * 0.14, c.Y - s * 0.14),
            new Point(c.X + s * 0.08, c.Y - s * 0.34),
            new Point(c.X + s * 0.08, c.Y + s * 0.34),
            new Point(c.X - s * 0.14, c.Y + s * 0.14),
            new Point(c.X - s * 0.34, c.Y + s * 0.14)
        ], B("#64D2FF"), null);
        DrawArc(context, new Point(c.X + s * 0.08, c.Y), s * 0.22,
            -Math.PI / 3, Math.PI / 3, B("#FFFFFF"), s * 0.04, 10);
        DrawArc(context, new Point(c.X + s * 0.08, c.Y), s * 0.34,
            -Math.PI / 3, Math.PI / 3, B("#FFFFFF"), s * 0.035, 10);
    }

    private static void DrawPalmSymbol(DrawingContext context, Point c, double s)
    {
        var skin = B("#FFD2A6");
        context.DrawEllipse(skin, new Pen(B("#7A4A2B"), s * 0.02),
            new Point(c.X, c.Y + s * 0.15), s * 0.27, s * 0.25);
        for (var i = 0; i < 4; i++)
        {
            var x = c.X - s * 0.20 + i * s * 0.13;
            var top = c.Y - s * (0.34 + (i is 1 or 2 ? 0.07 : 0));
            context.DrawLine(new Pen(skin, s * 0.10), new Point(x, c.Y + s * 0.03), new Point(x, top));
            context.DrawEllipse(skin, null, new Point(x, top), s * 0.05, s * 0.06);
        }
        context.DrawLine(new Pen(skin, s * 0.11),
            new Point(c.X - s * 0.20, c.Y + s * 0.08), new Point(c.X - s * 0.38, c.Y - s * 0.05));
    }

    private static void DrawBalloonSymbol(DrawingContext context, Point c, double s)
    {
        var fill = AccentBrushes[(int)(Math.Abs(c.X + c.Y) / Math.Max(1, s)) % 6];
        context.DrawEllipse(fill, new Pen(B("#FFFFFF"), s * 0.018),
            new Point(c.X, c.Y - s * 0.08), s * 0.27, s * 0.34);
        DrawClosedShape(context,
        [
            new Point(c.X - s * 0.06, c.Y + s * 0.24),
            new Point(c.X + s * 0.06, c.Y + s * 0.24),
            new Point(c.X, c.Y + s * 0.34)
        ], fill, null);
        context.DrawLine(new Pen(B("#FFFFFF"), Math.Max(1, s * 0.015)),
            new Point(c.X, c.Y + s * 0.33), new Point(c.X + s * 0.07, c.Y + s * 0.48));
    }

    private static void DrawPeaceSymbol(DrawingContext context, Point c, double s)
    {
        var skin = B("#FFD2A6");
        context.DrawEllipse(skin, new Pen(B("#7A4A2B"), s * 0.02),
            new Point(c.X, c.Y + s * 0.18), s * 0.25, s * 0.23);

        var fingerPen = new Pen(skin, s * 0.11);
        var leftEnd = new Point(c.X - s * 0.19, c.Y - s * 0.38);
        var rightEnd = new Point(c.X + s * 0.20, c.Y - s * 0.38);
        context.DrawLine(fingerPen, new Point(c.X - s * 0.07, c.Y + s * 0.02), leftEnd);
        context.DrawLine(fingerPen, new Point(c.X + s * 0.06, c.Y + s * 0.02), rightEnd);
        context.DrawEllipse(skin, null, leftEnd, s * 0.055, s * 0.065);
        context.DrawEllipse(skin, null, rightEnd, s * 0.055, s * 0.065);

        context.DrawLine(new Pen(skin, s * 0.10),
            new Point(c.X - s * 0.16, c.Y + s * 0.10), new Point(c.X - s * 0.34, c.Y - s * 0.02));
    }

    private static void DrawPartySymbol(DrawingContext context, Point c, double s)
    {
        DrawClosedShape(context,
        [
            new Point(c.X - s * 0.28, c.Y + s * 0.35),
            new Point(c.X + s * 0.30, c.Y + s * 0.15),
            new Point(c.X - s * 0.10, c.Y - s * 0.30)
        ], B("#FF9F0A"), new Pen(B("#FFFFFF"), s * 0.02));
        DrawStar(context, new Point(c.X + s * 0.26, c.Y - s * 0.24), s * 0.10, B("#FFD60A"));
        context.DrawEllipse(B("#64D2FF"), null, new Point(c.X + s * 0.35, c.Y + s * 0.02), s * 0.05, s * 0.05);
        context.DrawEllipse(B("#FF375F"), null, new Point(c.X + s * 0.05, c.Y - s * 0.38), s * 0.045, s * 0.045);
    }

    private static void DrawRockSymbol(DrawingContext context, Point c, double s)
    {
        var skin = B("#FFD2A6");
        context.DrawEllipse(skin, new Pen(B("#7A4A2B"), s * 0.02),
            new Point(c.X, c.Y + s * 0.17), s * 0.26, s * 0.23);
        context.DrawLine(new Pen(skin, s * 0.11),
            new Point(c.X - s * 0.12, c.Y + s * 0.02), new Point(c.X - s * 0.23, c.Y - s * 0.38));
        context.DrawLine(new Pen(skin, s * 0.11),
            new Point(c.X + s * 0.13, c.Y + s * 0.04), new Point(c.X + s * 0.28, c.Y - s * 0.32));
        context.DrawEllipse(skin, null, new Point(c.X - s * 0.23, c.Y - s * 0.38), s * 0.055, s * 0.065);
        context.DrawEllipse(skin, null, new Point(c.X + s * 0.28, c.Y - s * 0.32), s * 0.055, s * 0.065);
    }

    private static void DrawWarningSymbol(DrawingContext context, Point c, double s)
    {
        DrawClosedShape(context,
        [
            new Point(c.X, c.Y - s * 0.42),
            new Point(c.X - s * 0.40, c.Y + s * 0.33),
            new Point(c.X + s * 0.40, c.Y + s * 0.33)
        ], B("#FFD60A"), new Pen(B("#1C1C1E"), s * 0.03));
        DrawLabel(context, "!", new Point(c.X, c.Y + s * 0.06), s * 0.45, B("#1C1C1E"));
    }

    private static void DrawMeteorSymbol(DrawingContext context, Point c, double s)
    {
        var tail = new Pen(B("#FF9F0A"), s * 0.05);
        context.DrawLine(tail, new Point(c.X - s * 0.42, c.Y - s * 0.32), new Point(c.X - s * 0.05, c.Y + s * 0.05));
        context.DrawLine(new Pen(B("#FFD60A"), s * 0.035),
            new Point(c.X - s * 0.48, c.Y - s * 0.16), new Point(c.X - s * 0.05, c.Y + s * 0.10));
        context.DrawEllipse(B("#FF453A"), new Pen(B("#FFFFFF"), s * 0.025),
            new Point(c.X + s * 0.12, c.Y + s * 0.14), s * 0.22, s * 0.22);
    }

    private static void DrawSwirlSymbol(DrawingContext context, Point c, double s)
    {
        for (var i = 0; i < 4; i++)
        {
            DrawArc(context, c, s * (0.10 + i * 0.08),
                i * 0.45, Math.PI * 1.55 + i * 0.45,
                AccentBrushes[(i + 2) % AccentBrushes.Length], s * 0.035, 18);
        }
    }

    private static void DrawRainCloudSymbol(DrawingContext context, Point c, double s)
    {
        var cloud = B("#D1D1D6");
        context.DrawEllipse(cloud, null, new Point(c.X - s * 0.18, c.Y - s * 0.08), s * 0.22, s * 0.17);
        context.DrawEllipse(cloud, null, new Point(c.X + s * 0.02, c.Y - s * 0.18), s * 0.25, s * 0.22);
        context.DrawEllipse(cloud, null, new Point(c.X + s * 0.23, c.Y - s * 0.07), s * 0.20, s * 0.16);
        context.DrawRectangle(cloud, null, new Rect(c.X - s * 0.36, c.Y - s * 0.08, s * 0.72, s * 0.20));
        var rainPen = new Pen(B("#64D2FF"), s * 0.035);
        for (var i = -1; i <= 1; i++)
        {
            var x = c.X + i * s * 0.20;
            context.DrawLine(rainPen, new Point(x, c.Y + s * 0.16), new Point(x - s * 0.06, c.Y + s * 0.38));
        }
    }

    private static void DrawClosedShape(
        DrawingContext context,
        IReadOnlyList<Point> points,
        IBrush? fill,
        Pen? pen)
    {
        if (points.Count < 3)
            return;

        var geometry = new StreamGeometry();
        using (var g = geometry.Open())
        {
            g.BeginFigure(points[0], fill is not null);
            for (var i = 1; i < points.Count; i++)
                g.LineTo(points[i]);
            g.EndFigure(true);
        }
        context.DrawGeometry(fill, pen, geometry);
    }

    private static void DrawLabel(DrawingContext context, string label, Point center, double size, IBrush brush)
    {
        var text = new FormattedText(label, CultureInfo.CurrentCulture, FlowDirection.LeftToRight,
            LabelTypeface, size, brush);
        context.DrawText(text, new Point(center.X - text.Width / 2, center.Y - text.Height / 2));
    }

    private static double DurationFor(ReactionKind kind) => kind switch
    {
        ReactionKind.Balloons => 3.5,
        ReactionKind.Fireworks => 3.4,
        ReactionKind.Confetti or ReactionKind.RibbonCannon => 3.3,
        ReactionKind.HeartBurst or ReactionKind.HeartPulse or ReactionKind.Rain or
            ReactionKind.PortalGate or ReactionKind.HolyHalo => 3.0,
        ReactionKind.Lasers or ReactionKind.BubbleTrail or ReactionKind.ReverseMeteor or
            ReactionKind.TwinLightBeams or ReactionKind.SpotlightOne => 2.8,
        _ => 2.6
    };

    private static double Fade(double t, double duration) => Math.Clamp((duration - t) / 0.48, 0, 1);
    private static double PopScale(double t) => 0.45 + 0.55 * EaseOutBack(Math.Clamp(t / 0.24, 0, 1));
    private static double Lerp(double a, double b, double amount) => a + (b - a) * amount;
    private static IBrush B(string color) => BrushCache.GetOrAdd(color, static value => new SolidColorBrush(Color.Parse(value)));

    private static double EaseOutBack(double x)
    {
        const double c1 = 1.70158;
        const double c3 = c1 + 1;
        return 1 + c3 * Math.Pow(x - 1, 3) + c1 * Math.Pow(x - 1, 2);
    }

    private static double EaseOutCubic(double x) => 1 - Math.Pow(1 - x, 3);

    private sealed record Particle(double X, double Y, double Vx, double Vy, double Size, int BrushIndex, double Phase);
}
