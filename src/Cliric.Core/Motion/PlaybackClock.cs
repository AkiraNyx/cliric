using System.Diagnostics;

namespace Cliric.Core;

public sealed class PlaybackClock(Func<double>? monotonicMs = null)
{
    private readonly Func<double> _now = monotonicMs ?? (() => Stopwatch.GetTimestamp() * 1000.0 / Stopwatch.Frequency);
    private double _anchor;
    private double _position;
    public double DurationMs
    {
        get; private set;
    }
    public bool IsPlaying
    {
        get; private set;
    }
    public long Generation
    {
        get; private set;
    }
    public double PositionMs => Math.Clamp(_position + (IsPlaying ? _now() - _anchor : 0), 0, DurationMs);

    public void Load(double durationMs)
    {
        if (!double.IsFinite(durationMs) || durationMs < 0)
            throw new ArgumentOutOfRangeException(nameof(durationMs));
        DurationMs = durationMs;
        _position = 0;
        _anchor = _now();
        IsPlaying = false;
        Generation++;
    }
    public void Play()
    {
        if (IsPlaying)
            return;
        _anchor = _now();
        IsPlaying = true;
    }
    public void Pause()
    {
        _position = PositionMs;
        IsPlaying = false;
    }
    public void Seek(double position)
    {
        if (!double.IsFinite(position))
            throw new ArgumentOutOfRangeException(nameof(position));
        _position = Math.Clamp(position, 0, DurationMs);
        _anchor = _now();
        Generation++;
    }
}
