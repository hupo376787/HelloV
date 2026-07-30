using HelloV.Localization;
using HelloV.Models;

namespace HelloV.Services;

/// <summary>
/// HaGRIDv2 gesture metadata. The class order matches the official 34-class
/// YOLOv10 gesture detector (33 gestures plus no_gesture at class 33).
/// </summary>
public static class GestureCatalog
{
    public static GestureKind FromClassId(int classId) => classId switch
    {
        0 => GestureKind.Grabbing,
        1 => GestureKind.Grip,
        2 => GestureKind.Holy,
        3 => GestureKind.Point,
        4 => GestureKind.Call,
        5 => GestureKind.Three3,
        6 => GestureKind.Timeout,
        7 => GestureKind.XSign,
        8 => GestureKind.HandHeart,
        9 => GestureKind.HandHeart2,
        10 => GestureKind.LittleFinger,
        11 => GestureKind.MiddleFinger,
        12 => GestureKind.TakePicture,
        13 => GestureKind.Dislike,
        14 => GestureKind.Fist,
        15 => GestureKind.Four,
        16 => GestureKind.Like,
        17 => GestureKind.Mute,
        18 => GestureKind.Ok,
        19 => GestureKind.One,
        20 => GestureKind.Palm,
        21 => GestureKind.Peace,
        22 => GestureKind.PeaceInverted,
        23 => GestureKind.Rock,
        24 => GestureKind.Stop,
        25 => GestureKind.StopInverted,
        26 => GestureKind.Three,
        27 => GestureKind.Three2,
        28 => GestureKind.TwoUp,
        29 => GestureKind.TwoUpInverted,
        30 => GestureKind.ThreeGun,
        31 => GestureKind.ThumbIndex,
        32 => GestureKind.ThumbIndex2,
        _ => GestureKind.None
    };

    public static string DisplayName(GestureKind kind, LocalizationManager localization) =>
        localization[GestureKey(kind)];

    public static string GestureKey(GestureKind kind) => kind switch
    {
        GestureKind.Grabbing => "GestureGrabbing",
        GestureKind.Grip => "GestureGrip",
        GestureKind.Holy => "GestureHoly",
        GestureKind.Point => "GesturePoint",
        GestureKind.Call => "GestureCall",
        GestureKind.Three3 => "GestureThree3",
        GestureKind.Timeout => "GestureTimeout",
        GestureKind.XSign => "GestureXSign",
        GestureKind.HandHeart => "GestureHandHeart",
        GestureKind.HandHeart2 => "GestureHandHeart2",
        GestureKind.LittleFinger => "GestureLittleFinger",
        GestureKind.MiddleFinger => "GestureMiddleFinger",
        GestureKind.TakePicture => "GestureTakePicture",
        GestureKind.Dislike => "GestureDislike",
        GestureKind.Fist => "GestureFist",
        GestureKind.Four => "GestureFour",
        GestureKind.Like => "GestureLike",
        GestureKind.Mute => "GestureMute",
        GestureKind.Ok => "GestureOk",
        GestureKind.One => "GestureOne",
        GestureKind.Palm => "GesturePalm",
        GestureKind.Peace => "GesturePeace",
        GestureKind.PeaceInverted => "GesturePeaceInverted",
        GestureKind.Rock => "GestureRock",
        GestureKind.Stop => "GestureStop",
        GestureKind.StopInverted => "GestureStopInverted",
        GestureKind.Three => "GestureThree",
        GestureKind.Three2 => "GestureThree2",
        GestureKind.TwoUp => "GestureTwoUp",
        GestureKind.TwoUpInverted => "GestureTwoUpInverted",
        GestureKind.ThreeGun => "GestureThreeGun",
        GestureKind.ThumbIndex => "GestureThumbIndex",
        GestureKind.ThumbIndex2 => "GestureThumbIndex2",
        _ => "WaitingGesture"
    };

    public static ReactionKind ReactionFor(GestureKind kind) => kind switch
    {
        GestureKind.Grabbing => ReactionKind.GrabMagnet,
        GestureKind.Grip => ReactionKind.GripPulse,
        GestureKind.Holy => ReactionKind.HolyHalo,
        GestureKind.Point => ReactionKind.PointArrow,
        GestureKind.Call => ReactionKind.PhoneWave,
        GestureKind.Three3 => ReactionKind.TripleStars,
        GestureKind.Timeout => ReactionKind.TimeoutRing,
        GestureKind.XSign => ReactionKind.XSlash,
        GestureKind.HandHeart => ReactionKind.HeartBurst,
        GestureKind.HandHeart2 => ReactionKind.HeartPulse,
        GestureKind.LittleFinger => ReactionKind.PinkSparkles,
        GestureKind.MiddleFinger => ReactionKind.PurpleArc,
        GestureKind.TakePicture => ReactionKind.CameraFlash,
        GestureKind.Dislike => ReactionKind.ThumbsDown,
        GestureKind.Fist => ReactionKind.ImpactShockwave,
        GestureKind.Four => ReactionKind.FourStarPattern,
        GestureKind.Like => ReactionKind.ThumbsUp,
        GestureKind.Mute => ReactionKind.MuteWave,
        GestureKind.Ok => ReactionKind.OkRing,
        GestureKind.One => ReactionKind.SpotlightOne,
        GestureKind.Palm => ReactionKind.PalmShield,
        GestureKind.Peace => ReactionKind.Balloons,
        GestureKind.PeaceInverted => ReactionKind.RibbonCannon,
        GestureKind.Rock => ReactionKind.RockPulse,
        GestureKind.Stop => ReactionKind.StopWall,
        GestureKind.StopInverted => ReactionKind.WarningRing,
        GestureKind.Three => ReactionKind.BubbleTrail,
        GestureKind.Three2 => ReactionKind.TripleRipple,
        GestureKind.TwoUp => ReactionKind.TwinLightBeams,
        GestureKind.TwoUpInverted => ReactionKind.ReverseMeteor,
        GestureKind.ThreeGun => ReactionKind.PewShot,
        GestureKind.ThumbIndex => ReactionKind.PinchSpark,
        GestureKind.ThumbIndex2 => ReactionKind.PortalGate,
        _ => ReactionKind.None
    };

    public static float ConfidenceThreshold(GestureKind kind) => kind switch
    {
        GestureKind.HandHeart or GestureKind.HandHeart2 => 0.38f,
        GestureKind.Like or GestureKind.Dislike => 0.48f,
        GestureKind.Peace or GestureKind.PeaceInverted or GestureKind.Rock => 0.46f,
        GestureKind.MiddleFinger or GestureKind.LittleFinger or GestureKind.ThumbIndex => 0.50f,
        GestureKind.ThumbIndex2 or GestureKind.TakePicture or GestureKind.XSign => 0.42f,
        _ => 0.44f
    };

    public static IReadOnlyList<GestureEffectDemoItem> CreateDemoItems(
        LocalizationManager localization)
    {
        GestureEffectDemoItem Demo(ReactionKind kind, string key) =>
            new(kind, localization[key]);

        return
        [
            Demo(ReactionKind.GrabMagnet, "EffectGrabMagnet"),
            Demo(ReactionKind.GripPulse, "EffectGripPulse"),
            Demo(ReactionKind.HolyHalo, "EffectHolyHalo"),
            Demo(ReactionKind.PointArrow, "EffectPointArrow"),
            Demo(ReactionKind.PhoneWave, "EffectPhoneWave"),
            Demo(ReactionKind.TripleStars, "EffectTripleStars"),
            Demo(ReactionKind.TimeoutRing, "EffectTimeoutRing"),
            Demo(ReactionKind.XSlash, "EffectXSlash"),
            Demo(ReactionKind.HeartBurst, "EffectHeartBurst"),
            Demo(ReactionKind.HeartPulse, "EffectHeartPulse"),
            Demo(ReactionKind.PinkSparkles, "EffectPinkSparkles"),
            Demo(ReactionKind.PurpleArc, "EffectPurpleArc"),
            Demo(ReactionKind.CameraFlash, "EffectCameraFlash"),
            Demo(ReactionKind.ThumbsDown, "EffectThumbsDown"),
            Demo(ReactionKind.ImpactShockwave, "EffectImpactShockwave"),
            Demo(ReactionKind.FourStarPattern, "EffectFourStarPattern"),
            Demo(ReactionKind.ThumbsUp, "EffectThumbsUp"),
            Demo(ReactionKind.MuteWave, "EffectMuteWave"),
            Demo(ReactionKind.OkRing, "EffectOkRing"),
            Demo(ReactionKind.SpotlightOne, "EffectSpotlightOne"),
            Demo(ReactionKind.PalmShield, "EffectPalmShield"),
            Demo(ReactionKind.Balloons, "EffectBalloons"),
            Demo(ReactionKind.RibbonCannon, "EffectRibbonCannon"),
            Demo(ReactionKind.RockPulse, "EffectRockPulse"),
            Demo(ReactionKind.StopWall, "EffectStopWall"),
            Demo(ReactionKind.WarningRing, "EffectWarningRing"),
            Demo(ReactionKind.BubbleTrail, "EffectBubbleTrail"),
            Demo(ReactionKind.TripleRipple, "EffectTripleRipple"),
            Demo(ReactionKind.TwinLightBeams, "EffectTwinLightBeams"),
            Demo(ReactionKind.ReverseMeteor, "EffectReverseMeteor"),
            Demo(ReactionKind.PewShot, "EffectPewShot"),
            Demo(ReactionKind.PinchSpark, "EffectPinchSpark"),
            Demo(ReactionKind.PortalGate, "EffectPortalGate"),
            Demo(ReactionKind.Fireworks, "EffectFireworks"),
            Demo(ReactionKind.Rain, "EffectRain"),
            Demo(ReactionKind.Confetti, "EffectConfetti"),
            Demo(ReactionKind.Lasers, "EffectLasers")
        ];
    }

    public static string EffectKey(ReactionKind kind) => kind switch
    {
        ReactionKind.GrabMagnet => "EffectGrabMagnet",
        ReactionKind.GripPulse => "EffectGripPulse",
        ReactionKind.HolyHalo => "EffectHolyHalo",
        ReactionKind.PointArrow => "EffectPointArrow",
        ReactionKind.PhoneWave => "EffectPhoneWave",
        ReactionKind.TripleStars => "EffectTripleStars",
        ReactionKind.TimeoutRing => "EffectTimeoutRing",
        ReactionKind.XSlash => "EffectXSlash",
        ReactionKind.HeartBurst => "EffectHeartBurst",
        ReactionKind.HeartPulse => "EffectHeartPulse",
        ReactionKind.PinkSparkles => "EffectPinkSparkles",
        ReactionKind.PurpleArc => "EffectPurpleArc",
        ReactionKind.CameraFlash => "EffectCameraFlash",
        ReactionKind.ThumbsDown => "EffectThumbsDown",
        ReactionKind.ImpactShockwave => "EffectImpactShockwave",
        ReactionKind.FourStarPattern => "EffectFourStarPattern",
        ReactionKind.ThumbsUp => "EffectThumbsUp",
        ReactionKind.MuteWave => "EffectMuteWave",
        ReactionKind.OkRing => "EffectOkRing",
        ReactionKind.SpotlightOne => "EffectSpotlightOne",
        ReactionKind.PalmShield => "EffectPalmShield",
        ReactionKind.Balloons => "EffectBalloons",
        ReactionKind.RibbonCannon => "EffectRibbonCannon",
        ReactionKind.RockPulse => "EffectRockPulse",
        ReactionKind.StopWall => "EffectStopWall",
        ReactionKind.WarningRing => "EffectWarningRing",
        ReactionKind.BubbleTrail => "EffectBubbleTrail",
        ReactionKind.TripleRipple => "EffectTripleRipple",
        ReactionKind.TwinLightBeams => "EffectTwinLightBeams",
        ReactionKind.ReverseMeteor => "EffectReverseMeteor",
        ReactionKind.PewShot => "EffectPewShot",
        ReactionKind.PinchSpark => "EffectPinchSpark",
        ReactionKind.PortalGate => "EffectPortalGate",
        ReactionKind.Fireworks => "EffectFireworks",
        ReactionKind.Rain => "EffectRain",
        ReactionKind.Confetti => "EffectConfetti",
        ReactionKind.Lasers => "EffectLasers",
        _ => "WaitingGesture"
    };
}
