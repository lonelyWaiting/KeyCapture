using Microsoft.EntityFrameworkCore;
using KeyCapture.Data;

namespace KeyCapture.Services;

public sealed class AnalyticsService
{
    // --- DTOs ---

    public record HeatmapEntry(string KeyCombo, int Count, double Percentage);
    public record AppStatEntry(string AppName, int KeyCount, double DurationMinutes);
    public record Suggestion(string Icon, string Title, string Description);

    // --- Heatmap ---

    public async Task<List<HeatmapEntry>> GetHotkeyHeatmapAsync(DateTime from, DateTime to, int topN = 20)
    {
        using var db = new AnalyticsDbContext();
        var events = await db.KeyEvents
            .AsNoTracking()
            .Where(e => e.Timestamp >= from && e.Timestamp <= to)
            .GroupBy(e => e.KeyDisplayText)
            .Select(g => new { KeyCombo = g.Key, Count = g.Count() })
            .OrderByDescending(x => x.Count)
            .Take(topN)
            .ToListAsync();

        var total = events.Sum(e => e.Count);
        return events.Select(e => new HeatmapEntry(
            e.KeyCombo,
            e.Count,
            total > 0 ? Math.Round(e.Count * 100.0 / total, 1) : 0
        )).ToList();
    }

    // --- App Statistics ---

    public async Task<List<AppStatEntry>> GetAppStatisticsAsync(DateTime from, DateTime to, int topN = 10)
    {
        using var db = new AnalyticsDbContext();
        var keyCountsByApp = await db.KeyEvents
            .AsNoTracking()
            .Where(e => e.Timestamp >= from && e.Timestamp <= to)
            .GroupBy(e => e.ApplicationName)
            .Select(g => new { AppName = g.Key, KeyCount = g.Count() })
            .OrderByDescending(x => x.KeyCount)
            .Take(topN)
            .ToListAsync();

        if (keyCountsByApp.Count == 0)
            return [];

        // Only the apps that actually made the top-N list need their session durations.
        var topApps = keyCountsByApp.Select(k => k.AppName).ToArray();
        var sessions = await db.AppSessions
            .AsNoTracking()
            .Where(s => s.StartTime >= from && s.LastActiveTime <= to && topApps.Contains(s.ApplicationName))
            .Select(s => new { s.ApplicationName, s.StartTime, s.LastActiveTime })
            .ToListAsync();

        var durationByApp = sessions
            .GroupBy(s => s.ApplicationName)
            .ToDictionary(
                g => g.Key,
                g => Math.Round(g.Sum(s => (s.LastActiveTime - s.StartTime).TotalMinutes), 1));

        return keyCountsByApp
            .Select(k => new AppStatEntry(
                k.AppName,
                k.KeyCount,
                durationByApp.GetValueOrDefault(k.AppName, 0)))
            .ToList();
    }

    // --- Get all distinct app names (for the ComboBox in UI) ---

    public async Task<List<string>> GetAllAppNamesAsync(DateTime from, DateTime to)
    {
        using var db = new AnalyticsDbContext();
        return await db.KeyEvents
            .AsNoTracking()
            .Where(e => e.Timestamp >= from && e.Timestamp <= to)
            .Select(e => e.ApplicationName)
            .Distinct()
            .OrderBy(n => n)
            .ToListAsync();
    }

    // --- Efficiency Suggestions ---

    /// <summary>
    /// Each rule is evaluated as a grouped SQL query. Materialising every key event in the
    /// range first (as this used to do) made memory use grow with the retention window.
    /// </summary>
    public async Task<List<Suggestion>> GetEfficiencySuggestionsAsync(DateTime from, DateTime to)
    {
        using var db = new AnalyticsDbContext();
        var suggestions = new List<Suggestion>();

        var events = db.KeyEvents.AsNoTracking().Where(e => e.Timestamp >= from && e.Timestamp <= to);

        // Rule 1: High copy/paste usage
        var clipboardHeavyApps = await events
            .Where(e => e.KeyDisplayText == "Ctrl+C" || e.KeyDisplayText == "Ctrl+V")
            .GroupBy(e => e.ApplicationName)
            .Select(g => new { App = g.Key, Count = g.Count() })
            .Where(x => x.Count > 20)
            .ToListAsync();

        foreach (var app in clipboardHeavyApps)
        {
            suggestions.Add(new Suggestion(
                "📋",
                $"Heavy clipboard use in {app.App}",
                $"You used Ctrl+C/V {app.Count} times. Consider using clipboard history (Win+V) or multi-cursor editing."));
        }

        // Rule 2: High key count but few shortcut combos
        var appKeyStats = await events
            .GroupBy(e => e.ApplicationName)
            .Select(g => new
            {
                App = g.Key,
                Total = g.Count(),
                Combos = g.Count(e => e.IsCombo)
            })
            // Integer form of "combos < 10% of total" so the comparison stays in SQL.
            .Where(x => x.Total > 50 && x.Combos * 10 < x.Total)
            .ToListAsync();

        foreach (var stat in appKeyStats)
        {
            suggestions.Add(new Suggestion(
                "⌨️",
                $"Low shortcut usage in {stat.App}",
                $"Only {stat.Combos} of {stat.Total} keystrokes were shortcuts. Learning {stat.App} keyboard shortcuts could boost your productivity."));
        }

        // Rule 3: Repeated modifier combos in same app
        var repeatedCombos = await events
            .Where(e => e.IsCombo)
            .GroupBy(e => new { e.ApplicationName, e.KeyDisplayText })
            .Select(g => new { g.Key.ApplicationName, g.Key.KeyDisplayText, Count = g.Count() })
            .Where(x => x.Count > 15)
            .OrderByDescending(x => x.Count)
            .Take(5)
            .ToListAsync();

        foreach (var combo in repeatedCombos)
        {
            suggestions.Add(new Suggestion(
                "🔁",
                $"Frequent: {combo.KeyDisplayText} in {combo.ApplicationName}",
                $"Used {combo.Count} times. If this action has a quicker alternative or macro, it could save significant time."));
        }

        return suggestions;
    }
}
