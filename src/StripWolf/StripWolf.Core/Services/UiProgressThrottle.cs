// StripWolf - an open source comic book reader
// Copyright (C) 2026 Dapplo - Robin Krom
//
// For more information see: https://github.com/dapplo/StripWolf
// The StripWolf project is hosted on GitHub https://github.com/dapplo/StripWolf
//
// This program is free software: you can redistribute it and/or modify
// it under the terms of the GNU General Public License as published by
// the Free Software Foundation, either version 3 of the License, or
// (at your option) any later version.
//
// This program is distributed in the hope that it will be useful,
// but WITHOUT ANY WARRANTY; without even the implied warranty of
// MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.  See the
// GNU General Public License for more details.
// 
// You should have received a copy of the GNU General Public License
// along with this program.  If not, see <https://www.gnu.org/licenses/>.

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

