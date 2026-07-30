namespace HelloV.Models;

public enum GestureKind
{
    None,
    Grabbing,
    Grip,
    Holy,
    Point,
    Call,
    Three3,
    Timeout,
    XSign,
    HandHeart,
    HandHeart2,
    LittleFinger,
    MiddleFinger,
    TakePicture,
    Dislike,
    Fist,
    Four,
    Like,
    Mute,
    Ok,
    One,
    Palm,
    Peace,
    PeaceInverted,
    Rock,
    Stop,
    StopInverted,
    Three,
    Three2,
    TwoUp,
    TwoUpInverted,
    ThreeGun,
    ThumbIndex,
    ThumbIndex2
}

public readonly record struct NormalizedRect(float X, float Y, float Width, float Height)
{
    public float Right => X + Width;
    public float Bottom => Y + Height;
    public float Area => Math.Max(0, Width) * Math.Max(0, Height);
    public float CenterX => X + Width / 2f;
    public float CenterY => Y + Height / 2f;

    public static NormalizedRect FromCorners(float x1, float y1, float x2, float y2)
    {
        x1 = Math.Clamp(x1, 0f, 1f);
        y1 = Math.Clamp(y1, 0f, 1f);
        x2 = Math.Clamp(x2, 0f, 1f);
        y2 = Math.Clamp(y2, 0f, 1f);
        return new NormalizedRect(
            Math.Min(x1, x2),
            Math.Min(y1, y2),
            Math.Abs(x2 - x1),
            Math.Abs(y2 - y1));
    }

    public float IntersectionOverUnion(NormalizedRect other)
    {
        var left = Math.Max(X, other.X);
        var top = Math.Max(Y, other.Y);
        var right = Math.Min(Right, other.Right);
        var bottom = Math.Min(Bottom, other.Bottom);
        var intersection = Math.Max(0, right - left) * Math.Max(0, bottom - top);
        var union = Area + other.Area - intersection;
        return union <= 0 ? 0 : intersection / union;
    }

    public static NormalizedRect Union(NormalizedRect first, NormalizedRect second)
    {
        var left = Math.Min(first.X, second.X);
        var top = Math.Min(first.Y, second.Y);
        var right = Math.Max(first.Right, second.Right);
        var bottom = Math.Max(first.Bottom, second.Bottom);
        return FromCorners(left, top, right, bottom);
    }
}

public sealed record GestureDetection(
    GestureKind Kind,
    float Confidence,
    NormalizedRect Bounds);

public sealed record GestureFrameResult(IReadOnlyList<GestureDetection> Detections)
{
    public static GestureFrameResult Empty { get; } = new(Array.Empty<GestureDetection>());
}

public enum ReactionKind
{
    None,

    // Apple-style combinations.
    Fireworks,
    Rain,
    Confetti,
    Lasers,

    // One animation for each HaGRIDv2 gesture class.
    GrabMagnet,
    GripPulse,
    HolyHalo,
    PointArrow,
    PhoneWave,
    TripleStars,
    TimeoutRing,
    XSlash,
    HeartBurst,
    HeartPulse,
    PinkSparkles,
    PurpleArc,
    CameraFlash,
    ThumbsDown,
    ImpactShockwave,
    FourStarPattern,
    ThumbsUp,
    MuteWave,
    OkRing,
    SpotlightOne,
    PalmShield,
    Balloons,
    RibbonCannon,
    RockPulse,
    StopWall,
    WarningRing,
    BubbleTrail,
    TripleRipple,
    TwinLightBeams,
    ReverseMeteor,
    PewShot,
    PinchSpark,
    PortalGate
}

public sealed record ReactionDetection(
    ReactionKind Kind,
    float Confidence,
    string DisplayText,
    NormalizedRect Bounds);

public sealed record GestureEffectDemoItem(
    ReactionKind Kind,
    string DisplayName)
{
    public override string ToString() => DisplayName;
}
