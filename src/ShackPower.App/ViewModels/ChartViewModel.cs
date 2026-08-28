using Avalonia.Threading;
using ShackPower.App.Services;
using ShackPower.Core;

namespace ShackPower.App.ViewModels;

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

        LoadDay(DateOnly.FromDateTime(DateTime.Now));
    }

    public RelayCommand PrevDayCommand { get; }
    public RelayCommand NextDayCommand { get; }
    public RelayCommand Window1HCommand { get; }
    public RelayCommand Window6HCommand { get; }
    public RelayCommand Window24HCommand { get; }

    private DateOnly _day;
    public bool IsToday => _day == DateOnly.FromDateTime(DateTime.Now);
    public string DayText => IsToday ? "Today" : _day.ToString("ddd yyyy-MM-dd");

    private bool _combined;
    /// <summary>All three channels overlaid on one tall plot (each on its own scale) instead of
    /// three stacked strips. A presentation toggle only — the decimated data is shared.</summary>
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

    private double _liveWindowSeconds = 3600;

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
        _liveWindowSeconds = seconds;
        Recompute();
    }

    private void LoadDay(DateOnly day)
    {
        _day = day;
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
            // A past day is always the whole day; the window presets apply to the live view.
            seconds = 24 * 3600;
            start = _day.ToDateTime(TimeOnly.MinValue);
            entries = _dayFile;
        }

        WindowStart = start;
        WindowSeconds = seconds;
        OnPropertyChanged(nameof(GapSeconds));
        VoltSamples = ChartSampler.Decimate(entries, e => e.Volts, start, seconds, Buckets);
        AmpSamples = ChartSampler.Decimate(entries, e => e.Amps, start, seconds, Buckets);
        WattSamples = ChartSampler.Decimate(entries, e => e.Watts, start, seconds, Buckets);
    }

    public void Dispose() => _history.ReadingTick -= OnReadingTick;
}
