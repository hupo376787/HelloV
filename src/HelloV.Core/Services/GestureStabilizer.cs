using HelloV.Models;

namespace HelloV.Services;

/// <summary>
/// Suppresses one-frame false positives and prevents a held gesture from repeatedly firing.
/// The gesture must disappear for several inference frames before it can trigger again.
/// </summary>
public sealed class GestureStabilizer
{
    private readonly int _requiredHits;
    private readonly int _requiredMissesToRelease;
    private readonly bool _addExtraHitForCommonGestures;
    private ReactionKind _candidate;
    private int _hits;
    private int _candidateMisses;
    private int _releaseMisses;
    private ReactionKind _latched;

    public GestureStabilizer(
        int requiredHits = 3,
        int requiredMissesToRelease = 5,
        bool addExtraHitForCommonGestures = true)
    {
        _requiredHits = Math.Max(1, requiredHits);
        _requiredMissesToRelease = Math.Max(1, requiredMissesToRelease);
        _addExtraHitForCommonGestures = addExtraHitForCommonGestures;
    }

    public ReactionKind LatchedKind => _latched;

    public ReactionDetection? Push(ReactionDetection? detection, bool interruptMode = false)
    {
        var kind = detection?.Kind ?? ReactionKind.None;

        if (interruptMode)
            return PushInterruptible(detection, kind);

        if (_latched != ReactionKind.None)
        {
            if (kind == _latched)
            {
                _releaseMisses = 0;
                return null;
            }

            if (++_releaseMisses >= _requiredMissesToRelease)
            {
                _latched = ReactionKind.None;
                _releaseMisses = 0;
            }

            return null;
        }

        if (kind == ReactionKind.None)
        {
            // A single missed inference should not destroy an otherwise stable candidate.
            if (++_candidateMisses >= 2)
            {
                _candidate = ReactionKind.None;
                _hits = 0;
                _candidateMisses = 0;
            }
            return null;
        }

        _candidateMisses = 0;
        if (kind != _candidate)
        {
            _candidate = kind;
            _hits = 1;
        }
        else
        {
            _hits++;
        }

        if (_hits < RequiredHitsFor(kind))
            return null;

        _latched = kind;
        _candidate = ReactionKind.None;
        _hits = 0;
        return detection;
    }

    private ReactionDetection? PushInterruptible(ReactionDetection? detection, ReactionKind kind)
    {
        // Interrupt mode keeps one-frame switching, but only for a strong and reasonably sized
        // detection. Borderline detections must repeat once, preventing an isolated false positive
        // from replacing the current effect while the scene contains no deliberate gesture.
        if (kind == ReactionKind.None || detection is null)
        {
            _candidate = ReactionKind.None;
            _hits = 0;
            _candidateMisses = 0;

            if (_latched != ReactionKind.None && ++_releaseMisses >= _requiredMissesToRelease)
            {
                _latched = ReactionKind.None;
                _releaseMisses = 0;
            }

            return null;
        }

        _releaseMisses = 0;
        _candidateMisses = 0;

        if (kind == _latched)
        {
            _candidate = ReactionKind.None;
            _hits = 0;
            return null;
        }

        if (CanTriggerInterruptInOneFrame(detection))
        {
            _latched = kind;
            _candidate = ReactionKind.None;
            _hits = 0;
            return detection;
        }

        if (kind != _candidate)
        {
            _candidate = kind;
            _hits = 1;
            return null;
        }

        if (++_hits < 2)
            return null;

        _latched = kind;
        _candidate = ReactionKind.None;
        _hits = 0;
        return detection;
    }

    private static bool CanTriggerInterruptInOneFrame(ReactionDetection detection)
    {
        var minimumConfidence = detection.Kind switch
        {
            // Two-hand combinations are already selective because both hands must match.
            ReactionKind.Fireworks or ReactionKind.Rain or ReactionKind.Confetti or
                ReactionKind.Lasers or ReactionKind.HeartBurst or ReactionKind.HeartPulse => 0.58f,

            // These common poses are the easiest to hallucinate from ordinary hand movement.
            ReactionKind.ThumbsUp or ReactionKind.ThumbsDown or ReactionKind.Balloons or
                ReactionKind.RibbonCannon or ReactionKind.RockPulse or ReactionKind.PointArrow or
                ReactionKind.PalmShield => 0.66f,

            _ => 0.62f
        };

        return detection.Confidence >= minimumConfidence && detection.Bounds.Area >= 0.008f;
    }

    private int RequiredHitsFor(ReactionKind kind) => kind switch
    {
        // Common hand poses are easy to produce accidentally, so hold them one extra frame.
        ReactionKind.ThumbsUp or ReactionKind.ThumbsDown or ReactionKind.Balloons or
        ReactionKind.RibbonCannon or ReactionKind.RockPulse or ReactionKind.PointArrow or
        ReactionKind.PalmShield => _requiredHits + (_addExtraHitForCommonGestures ? 1 : 0),

        // Explicit two-hand combinations are already selective.
        ReactionKind.Fireworks or ReactionKind.Rain or ReactionKind.Confetti or
        ReactionKind.Lasers or ReactionKind.HeartBurst or ReactionKind.HeartPulse => _requiredHits,

        _ => _requiredHits
    };
}
