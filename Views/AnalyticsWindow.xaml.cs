using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Media;
using KeyCapture.Services;

namespace KeyCapture.Views;

/// <summary>
/// Converts AlternationIndex (0-based) to a 1-based rank string for display.
/// </summary>
[ValueConversion(typeof(int), typeof(string))]
public sealed class AlternationRankConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        => value is int idx ? (idx + 1).ToString() : string.Empty;

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotSupportedException();
}

/// <summary>
/// Converts row index to a background brush for heatmap frequency coloring.
/// Higher rank (lower index) = brighter accent color.
/// </summary>
[ValueConversion(typeof(int), typeof(Brush))]
public sealed class HeatmapBrushConverter : IValueConverter
{
    // Accent gradient: rank 1 = vivid purple-blue, rank 20 = very dim
    private static readonly Color AccentHigh = Color.FromRgb(0x7B, 0x8C, 0xDE);   // #7B8CDE
    private static readonly Color AccentLow  = Color.FromRgb(0x2A, 0x2A, 0x3E);   // #2A2A3E (same as row bg)

    // There are only 20 distinct ranks, so the brushes are built once and frozen: an unfrozen
    // brush per row costs an extra WPF change-notification subscription for every refresh.
    private static readonly SolidColorBrush[] RankBrushes = BuildRankBrushes();

    private static SolidColorBrush[] BuildRankBrushes()
    {
        var brushes = new SolidColorBrush[20];
        for (int index = 0; index < brushes.Length; index++)
        {
            double t = 1.0 - index / 19.0;
            byte r = (byte)(AccentLow.R + t * (AccentHigh.R - AccentLow.R));
            byte g = (byte)(AccentLow.G + t * (AccentHigh.G - AccentLow.G));
            byte b = (byte)(AccentLow.B + t * (AccentHigh.B - AccentLow.B));
            // Blend alpha: top ranks get more opaque overlay
            byte a = (byte)(30 + t * 80);
            var brush = new SolidColorBrush(Color.FromArgb(a, r, g, b));
            brush.Freeze();
            brushes[index] = brush;
        }
        return brushes;
    }

    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is int rank && rank >= 1)
            return RankBrushes[Math.Clamp(rank - 1, 0, RankBrushes.Length - 1)];

        return Brushes.Transparent;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotSupportedException();
}

/// <summary>
/// Statistics dashboard window — shows heatmap, app stats, and efficiency suggestions.
/// </summary>
public partial class AnalyticsWindow : Window
{
    private readonly AnalyticsService _analytics;

    public AnalyticsWindow(AnalyticsService analytics)
    {
        InitializeComponent();
        _analytics = analytics;
        Loaded += async (_, _) => await LoadDataAsync();
    }

    // ─────────────────────────── Data loading ────────────────────────────

    private (DateTime from, DateTime to) GetDateRange()
    {
        var to = DateTime.UtcNow;
        var from = to.Date; // default: Today

        if (RangeSelector.SelectedIndex == 1) from = to.AddDays(-7);
        else if (RangeSelector.SelectedIndex == 2) from = to.AddDays(-30);

        return (from, to);
    }

    private async Task LoadDataAsync()
    {
        try
        {
            var (from, to) = GetDateRange();

            // Kept on a worker thread: the SQLite provider's async methods execute
            // synchronously, so awaiting them directly would block the UI thread.
            HeatmapGrid.ItemsSource = await Task.Run(() => _analytics.GetHotkeyHeatmapAsync(from, to));
            AppStatsGrid.ItemsSource = await Task.Run(() => _analytics.GetAppStatisticsAsync(from, to));

            var suggestions = await Task.Run(() => _analytics.GetEfficiencySuggestionsAsync(from, to));
            SuggestionsList.ItemsSource = suggestions;

            // Show/hide empty state
            EmptyState.Visibility    = suggestions.Count == 0 ? Visibility.Visible   : Visibility.Collapsed;
            SuggestionsList.Visibility = suggestions.Count >  0 ? Visibility.Visible : Visibility.Collapsed;
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[AnalyticsWindow] LoadData failed: {ex.Message}");
        }
    }

    // ──────────────────────────── Event handlers ─────────────────────────

    private async void OnRefreshClick(object sender, RoutedEventArgs e)
        => await LoadDataAsync();

    private async void OnRangeChanged(object sender, SelectionChangedEventArgs e)
    {
        if (IsLoaded) await LoadDataAsync();
    }

    // ──────────────── Heatmap row coloring via LoadingRow ────────────────

    private static readonly HeatmapBrushConverter _brushConverter = new();

    private void OnHeatmapLoadingRow(object sender, DataGridRowEventArgs e)
        => ApplyRankBackground(e.Row);

    private void OnAppStatsLoadingRow(object sender, DataGridRowEventArgs e)
        => ApplyRankBackground(e.Row);

    private static void ApplyRankBackground(DataGridRow row)
    {
        int rank = row.GetIndex() + 1; // 1-based
        if (_brushConverter.Convert(rank, typeof(Brush), parameter: null!, CultureInfo.InvariantCulture)
            is Brush brush)
        {
            row.Background = brush;
        }
    }
}
