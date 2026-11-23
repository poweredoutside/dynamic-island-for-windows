using System.Diagnostics;
using DynamicIslandWin.Services;

namespace DynamicIslandWin;

public partial class MainWindow
{
    private sealed record PerformanceResult(double ProcessorPercent, double WorkingMegabytes, double PrivateMegabytes,
        double MaximumDelay, int DelayedSamples, double? MemoryDelta);

    private PerformanceResult? _performanceResult;
    private bool _performanceChecking, _performanceFailed;

    private void BuildPerformanceDiagnostics()
    {
        var card = FeatureCard("Стабильность и ресурсы", "Замер длится около двух секунд и измеряет только процесс островка.", parent: SettingsFeatureContent);
        var run = FeatureButton("Запустить тесты", () => _ = RunPerformanceDiagnosticsAsync());
        run.IsEnabled = !_performanceChecking;
        card.Children.Add(run);
        if (_performanceChecking || _performanceFailed)
            card.Children.Add(FeatureText(_performanceChecking ? "Выполняются замеры…" : "Не удалось выполнить замеры.", 12, "ThemeMutedBrush"));
        else
            foreach (string line in PerformanceResultLines()) card.Children.Add(FeatureText(line, 12));
    }

    private IEnumerable<string> PerformanceResultLines()
    {
        if (_performanceResult is not { } result) yield break;
        yield return string.Format(L("Задержка интерфейса до {0:N0} мс · задержек от 50 мс: {1}"), result.MaximumDelay, result.DelayedSamples);
        yield return string.Format(L("Средняя загрузка CPU · {0:N2}%"), result.ProcessorPercent);
        yield return string.Format(L("ОЗУ · {0:N1} МБ в рабочем наборе · {1:N1} МБ частной памяти"), result.WorkingMegabytes, result.PrivateMegabytes);
        if (result.MemoryDelta is double delta)
            yield return string.Format(L("Изменение рабочего набора с прошлого замера · {0:+0.0;-0.0;0.0} МБ"), delta);
    }

    private async Task RunPerformanceDiagnosticsAsync()
    {
        if (_performanceChecking || _windowClosed) return;
        _performanceChecking = true;
        _performanceFailed = false;
        RefreshPage();
        try
        {
            const int sampleCount = 16, intervalMs = 125;
            const double megabyte = 1024 * 1024;
            using var process = Process.GetCurrentProcess();
            var initialCpu = process.TotalProcessorTime;
            long start = Stopwatch.GetTimestamp();
            double worst = 0;
            int delayed = 0;
            for (int sample = 0; sample < sampleCount; sample++)
            {
                long sent = Stopwatch.GetTimestamp();
                await Task.Delay(intervalMs);
                if (_windowClosed) return;
                double delay = Math.Max(0, Stopwatch.GetElapsedTime(sent).TotalMilliseconds - intervalMs);
                worst = Math.Max(worst, delay);
                if (delay >= 50) delayed++;
            }
            double duration = Stopwatch.GetElapsedTime(start).TotalMilliseconds;
            process.Refresh();
            double cpu = Math.Clamp(100 * (process.TotalProcessorTime - initialCpu).TotalMilliseconds / (duration * Environment.ProcessorCount), 0, 100);
            double working = process.WorkingSet64 / megabyte;
            _performanceResult = new(cpu, working, process.PrivateMemorySize64 / megabyte, worst, delayed,
                _performanceResult is { } previous ? working - previous.WorkingMegabytes : null);
        }
        catch (Exception ex)
        {
            AppLog.Error("Measure island performance", ex);
            _performanceFailed = true;
        }
        finally { _performanceChecking = false; RefreshPage(); }

        void RefreshPage()
        {
            if (!_windowClosed && _currentState == IslandState.Settings && _settingsFeaturePage == "diagnostics")
                BuildSettingsFeaturePage("diagnostics");
        }
    }
}
