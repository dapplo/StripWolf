using Avalonia.Threading;

namespace StripWolf.Services;

internal static class UiProgressThrottle
{
    public static IProgress<double> Create(
        Action<double> apply,
        int minIntervalMilliseconds = 125,
        double minDelta = 0.02)
    {
        ArgumentNullException.ThrowIfNull(apply);

        var gate = new object();
        var lastValue = double.NaN;
        long lastTick = 0;

        return new Progress<double>(value =>
        {
            value = Math.Clamp(value, 0, 1);

            lock (gate)
            {
                var now = Environment.TickCount64;
                var delta = double.IsNaN(lastValue) ? double.MaxValue : Math.Abs(value - lastValue);
                var elapsed = now - lastTick;

                var shouldReport = double.IsNaN(lastValue) ||
                                   value <= 0 ||
                                   value >= 1 ||
                                   delta >= minDelta ||
                                   elapsed >= minIntervalMilliseconds;

                if (!shouldReport)
                {
                    return;
                }

                lastValue = value;
                lastTick = now;
            }

            if (Dispatcher.UIThread.CheckAccess())
            {
                apply(value);
            }
            else
            {
                Dispatcher.UIThread.Post(() => apply(value), DispatcherPriority.Background);
            }
        });
    }
}
