using Avalonia.Media;
using Avalonia.Threading;
using ShackPower.App.Services;
using ShackPower.Core;

namespace ShackPower.App.ViewModels;

/// <summary>A chartable channel — the combined view overlays exactly two of these.</summary>
public enum ChartChannel
{
    Volts,
    Amps,
    Watts,
}

/// <summary>
/// The Chart window's model: a selected day (today = live, following the ring) and a window
/// width. Day files load off-thread with a generation counter so a stale load can't clobber a
/// faster flip to another day; today's view merges the one-shot file prefix with the in-memory
/// ring (duplicate rows are harmless — min/max decimation is idempotent over repeats).
/// </summary>
public sealed class ChartViewModel : ViewModelBase, IDisposable
{
    private const int Buckets = 800;

    private readonly ChartHistoryService _history;
    private IReadOnlyList<PowerLogEntry> _dayFile = [];
    private int _loadGeneration;

    public ChartViewModel(ChartHistoryService history)
    {
        _history = history;
        _history.ReadingTick += OnReadingTick;

        PrevDayCommand = new RelayCommand(() => Step(-1));
        NextDayCommand = new RelayCommand(() => Step(+1), () => !IsToday);
        Window1HCommand = new RelayCommand(() => SetWindow(3600));
        Window6HCommand = new RelayCommand(() => SetWindow(6 * 3600));
        Window24HCommand = new RelayCommand(() => SetWindow(24 * 3600));
        // Chunkier steps than the wheel's 1.3 — a button click is a deliberate act. Centered
        // anchor: browse zooms around the middle of the view; live ignores it (tail-pinned).
        ZoomInCommand = new RelayCommand(() => ZoomAt(0.5, 1 / 1.5));
        ZoomOutCommand = new RelayCommand(() => ZoomAt(0.5, 1.5));

        LoadDay(DateOnly.FromDateTime(DateTime.Now));
    }

    public RelayCommand PrevDayCommand { get; }
    public RelayCommand NextDayCommand { get; }
    public RelayCommand Window1HCommand { get; }
    public RelayCommand Window6HCommand { get; }
    public RelayCommand Window24HCommand { get; }
    public RelayCommand ZoomInCommand { get; }
    public RelayCommand ZoomOutCommand { get; }

    private DateOnly _day;
    public bool IsToday => _day == DateOnly.FromDateTime(DateTime.Now);
    public string DayText => IsToday ? "Today" : _day.ToString("ddd yyyy-MM-dd");

    private bool _combined;
    /// <summary>Two chosen channels overlaid full-height with dual color-matched axes (the
    /// VictronConnect Trends arrangement) instead of three stacked strips. A presentation
    /// toggle only — the decimated data is shared.</summary>
    public bool Combined
    {
        get => _combined;
        set
        {
            if (SetProperty(ref _combined, value)) OnPropertyChanged(nameof(Split));
        }
    }

    /// <summary>Inverse of <see cref="Combined"/> for the stacked panel's visibility binding.</summary>
    public bool Split => !_combined;

    // ---- combined view's two channel pickers (Trends-style) ----

    public static ChartChannel[] Channels { get; } = Enum.GetValues<ChartChannel>();

    private ChartChannel _primaryChannel = ChartChannel.Amps;      // the reference plot's pairing
    public ChartChannel PrimaryChannel
    {
        get => _primaryChannel;
        set { if (SetProperty(ref _primaryChannel, value)) RaiseChannelViews(); }
    }

    private ChartChannel _secondaryChannel = ChartChannel.Volts;
    public ChartChannel SecondaryChannel
    {
        get => _secondaryChannel;
        set { if (SetProperty(ref _secondaryChannel, value)) RaiseChannelViews(); }
    }

    public IReadOnlyList<ChartSample> PrimarySamples => SamplesFor(_primaryChannel);
    public IReadOnlyList<ChartSample> SecondarySamples => SamplesFor(_secondaryChannel);
    public IBrush PrimaryBrush => BrushFor(_primaryChannel);
    public IBrush SecondaryBrush => BrushFor(_secondaryChannel);
    public string PrimaryUnit => UnitFor(_primaryChannel);
    public string SecondaryUnit => UnitFor(_secondaryChannel);

    private IReadOnlyList<ChartSample> SamplesFor(ChartChannel c) => c switch
    {
        ChartChannel.Volts => VoltSamples,
        ChartChannel.Amps => AmpSamples,
        _ => WattSamples,
    };

    /// <summary>Channel colors match the split view's strips, so a trace means the same thing
    /// in both presentations.</summary>
    private static IBrush BrushFor(ChartChannel c) => c switch
    {
        ChartChannel.Volts => Palette.BlueBrush,
        ChartChannel.Amps => Palette.OrangeDeepBrush,
        _ => Palette.GreenBrush,
    };

    private static string UnitFor(ChartChannel c) => c switch
    {
        ChartChannel.Volts => "V",
        ChartChannel.Amps => "A",
        _ => "W",
    };

    private void RaiseChannelViews()
    {
        OnPropertyChanged(nameof(PrimarySamples));
        OnPropertyChanged(nameof(SecondarySamples));
        OnPropertyChanged(nameof(PrimaryBrush));
        OnPropertyChanged(nameof(SecondaryBrush));
        OnPropertyChanged(nameof(PrimaryUnit));
        OnPropertyChanged(nameof(SecondaryUnit));
    }

    /// <summary>Wheel-zoom bounds: one minute (60 real samples at 1 Hz) up to a full day.</summary>
    private const double MinWindowSeconds = 60;
    private const double MaxWindowSeconds = 24 * 3600;

    private double _liveWindowSeconds = 3600;

    // Browse-mode viewport within a past day, in seconds from that day's midnight.
    private double _browseStartSeconds;
    private double _browseSeconds = MaxWindowSeconds;

    private DateTime _windowStart;
    public DateTime WindowStart { get => _windowStart; private set => SetProperty(ref _windowStart, value); }

    private double _windowSeconds = 3600;
    public double WindowSeconds { get => _windowSeconds; private set => SetProperty(ref _windowSeconds, value); }

    public double GapSeconds => Math.Max(5.0, 3.0 * WindowSeconds / Buckets);

    private IReadOnlyList<ChartSample> _voltSamples = [];
    public IReadOnlyList<ChartSample> VoltSamples { get => _voltSamples; private set => SetProperty(ref _voltSamples, value); }

    private IReadOnlyList<ChartSample> _ampSamples = [];
    public IReadOnlyList<ChartSample> AmpSamples { get => _ampSamples; private set => SetProperty(ref _ampSamples, value); }

    private IReadOnlyList<ChartSample> _wattSamples = [];
    public IReadOnlyList<ChartSample> WattSamples { get => _wattSamples; private set => SetProperty(ref _wattSamples, value); }

    private void Step(int days)
    {
        var target = _day.AddDays(days);
        var today = DateOnly.FromDateTime(DateTime.Now);
        if (target > today) target = today;
        LoadDay(target);
    }

    private void SetWindow(double seconds)
    {
        if (IsToday)
        {
            _liveWindowSeconds = seconds;
        }
        else
        {
            // Keep the viewport's start where it was, clamped so the window stays inside the day.
            _browseSeconds = Math.Clamp(seconds, MinWindowSeconds, MaxWindowSeconds);
            _browseStartSeconds = Math.Clamp(_browseStartSeconds, 0, MaxWindowSeconds - _browseSeconds);
        }
        Recompute();
    }

    /// <summary>
    /// Progressive wheel zoom from the chart controls. <paramref name="anchorFraction"/> is the
    /// cursor's horizontal position in the plot (0..1): browsing a past day, the moment under
    /// the cursor stays put while the window shrinks or grows around it. The live view is a
    /// tail pinned to now, so the anchor is ignored and only the tail length changes.
    /// </summary>
    public void ZoomAt(double anchorFraction, double factor)
    {
        if (IsToday)
        {
            _liveWindowSeconds = Math.Clamp(_liveWindowSeconds * factor, MinWindowSeconds, MaxWindowSeconds);
        }
        else
        {
            var newSeconds = Math.Clamp(_browseSeconds * factor, MinWindowSeconds, MaxWindowSeconds);
            var anchorTime = _browseStartSeconds + Math.Clamp(anchorFraction, 0, 1) * _browseSeconds;
            _browseStartSeconds = Math.Clamp(anchorTime - Math.Clamp(anchorFraction, 0, 1) * newSeconds,
                0, MaxWindowSeconds - newSeconds);
            _browseSeconds = newSeconds;
        }
        Recompute();
    }

    /// <summary>Drag-pan, as a fraction of the current window. Browse mode only — the live
    /// view stays pinned to now (flip to a past day to wander).</summary>
    public void PanBy(double windowFraction)
    {
        if (IsToday) return;
        _browseStartSeconds = Math.Clamp(_browseStartSeconds + windowFraction * _browseSeconds,
            0, MaxWindowSeconds - _browseSeconds);
        Recompute();
    }

    private void LoadDay(DateOnly day)
    {
        _day = day;
        _browseStartSeconds = 0;
        _browseSeconds = MaxWindowSeconds;   // a fresh day opens at the full 24 h
        OnPropertyChanged(nameof(DayText));
        OnPropertyChanged(nameof(IsToday));
        NextDayCommand.RaiseCanExecuteChanged();

        var generation = ++_loadGeneration;
        _dayFile = [];
        Recompute();   // show the ring (or emptiness) immediately while the file loads

        _ = _history.LoadDayAsync(day).ContinueWith(t =>
        {
            if (t.IsFaulted) return;   // a bad file shows as "no data", not a crash
            Dispatcher.UIThread.Post(() =>
            {
                if (generation != _loadGeneration) return;   // user already flipped on
                _dayFile = t.Result;
                Recompute();
            });
        });
    }

    private void OnReadingTick()
    {
        if (IsToday) Recompute();   // one recompute per second, and only while following live
    }

    private void Recompute()
    {
        DateTime start;
        double seconds;
        IReadOnlyList<PowerLogEntry> entries;

        if (IsToday)
        {
            seconds = _liveWindowSeconds;
            start = DateTime.Now.AddSeconds(-seconds);
            var ring = _history.Ring.Snapshot();
            var merged = new List<PowerLogEntry>(_dayFile.Count + ring.Length);
            merged.AddRange(_dayFile);
            merged.AddRange(ring);
            entries = merged;
        }
        else
        {
            // A past day opens at the full 24 h; wheel/drag then dives anywhere inside it.
            seconds = _browseSeconds;
            start = _day.ToDateTime(TimeOnly.MinValue).AddSeconds(_browseStartSeconds);
            entries = _dayFile;
        }

        WindowStart = start;
        WindowSeconds = seconds;
        OnPropertyChanged(nameof(GapSeconds));
        VoltSamples = ChartSampler.Decimate(entries, e => e.Volts, start, seconds, Buckets);
        AmpSamples = ChartSampler.Decimate(entries, e => e.Amps, start, seconds, Buckets);
        WattSamples = ChartSampler.Decimate(entries, e => e.Watts, start, seconds, Buckets);
        OnPropertyChanged(nameof(PrimarySamples));
        OnPropertyChanged(nameof(SecondarySamples));
    }

    public void Dispose() => _history.ReadingTick -= OnReadingTick;
}
