using HelloV.Localization;
using HelloV.Models;

namespace HelloV.Services;

/// <summary>
/// Converts raw HaGRIDv2 detections into animation events. Apple-style two-hand
/// combinations take priority; every remaining gesture maps to its own animation.
/// </summary>
public sealed class GestureEffectMatcher(LocalizationManager localization)
{
    private readonly LocalizationManager _localization = localization;

    public ReactionDetection? Match(GestureFrameResult frame)
    {
        if (frame.Detections.Count == 0)
            return null;

        var likes = Distinct(frame, GestureKind.Like);
        if (likes.Count >= 2)
            return Pair(ReactionKind.Fireworks, "EffectFireworks", likes);

        var dislikes = Distinct(frame, GestureKind.Dislike);
        if (dislikes.Count >= 2)
            return Pair(ReactionKind.Rain, "EffectRain", dislikes);

        var victories = Distinct(frame, GestureKind.Peace, GestureKind.PeaceInverted,
            GestureKind.TwoUp, GestureKind.TwoUpInverted);
        if (victories.Count >= 2)
            return Pair(ReactionKind.Confetti, "EffectConfetti", victories);

        var rocks = Distinct(frame, GestureKind.Rock);
        if (rocks.Count >= 2)
            return Pair(ReactionKind.Lasers, "EffectLasers", rocks);

        // Heart classes use one united bounding box that already contains both hands.
        var heart = frame.Detections
            .Where(x => x.Kind is GestureKind.HandHeart or GestureKind.HandHeart2)
            .OrderByDescending(x => x.Confidence)
            .FirstOrDefault();
        if (heart is not null)
            return Single(heart);

        var strongest = frame.Detections
            .OrderByDescending(x => x.Confidence)
            .FirstOrDefault();
        return strongest is null ? null : Single(strongest);
    }

    private ReactionDetection Single(GestureDetection detection)
    {
        var reaction = GestureCatalog.ReactionFor(detection.Kind);
        if (reaction == ReactionKind.None)
        {
            return new ReactionDetection(
                ReactionKind.None,
                detection.Confidence,
                _localization["WaitingGesture"],
                detection.Bounds);
        }

        return new ReactionDetection(
            reaction,
            detection.Confidence,
            _localization.Format(
                "GestureWithConfidence",
                GestureCatalog.DisplayName(detection.Kind, _localization),
                detection.Confidence),
            detection.Bounds);
    }

    private static List<GestureDetection> Distinct(
        GestureFrameResult frame,
        params GestureKind[] kinds)
    {
        var acceptedKinds = kinds.ToHashSet();
        var result = new List<GestureDetection>(2);
        foreach (var candidate in frame.Detections
                     .Where(x => acceptedKinds.Contains(x.Kind))
                     .OrderByDescending(x => x.Confidence))
        {
            var isDuplicate = result.Any(existing =>
                existing.Bounds.IntersectionOverUnion(candidate.Bounds) > 0.45f ||
                Distance(existing.Bounds, candidate.Bounds) < 0.055f);

            if (isDuplicate)
                continue;

            result.Add(candidate);
            if (result.Count == 2)
                break;
        }

        return result;
    }

    private static float Distance(NormalizedRect a, NormalizedRect b)
    {
        var dx = a.CenterX - b.CenterX;
        var dy = a.CenterY - b.CenterY;
        return MathF.Sqrt(dx * dx + dy * dy);
    }

    private ReactionDetection Pair(
        ReactionKind kind,
        string textKey,
        IReadOnlyList<GestureDetection> detections)
    {
        var confidence = Math.Min(detections[0].Confidence, detections[1].Confidence);
        var bounds = NormalizedRect.Union(detections[0].Bounds, detections[1].Bounds);
        return new ReactionDetection(
            kind,
            confidence,
            _localization.Format(
                "GestureWithConfidence",
                _localization[textKey],
                confidence),
            bounds);
    }
}
