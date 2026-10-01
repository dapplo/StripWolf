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

namespace StripWolf.Core.Services;

/// <summary>
/// Progress reporters which limit how often the UI is updated.
/// </summary>
/// <remarks>
/// This used to wrap <see cref="Progress{T}"/>: that posts *every* Report call to the captured (UI) synchronization
/// context first, and only then the throttling ran, so the UI thread still received every single report.
/// Now the throttling happens synchronously in Report, on the reporting thread, and only the reports which pass are
/// posted to the UI thread. A skipped report is delivered later (trailing), so the final value is never lost.
/// </remarks>
internal static class UiProgressThrottle
{
    public static IProgress<double> Create(
        Action<double> apply,
        int minIntervalMilliseconds = 125,
        double minDelta = 0.02)
    {
        ArgumentNullException.ThrowIfNull(apply);

        return new ThrottledProgress<double>(
            value => apply(Math.Clamp(value, 0, 1)),
            minIntervalMilliseconds,
            (value, lastReported) =>
            {
                value = Math.Clamp(value, 0, 1);
                return value <= 0 ||
                       value >= 1 ||
                       Math.Abs(value - Math.Clamp(lastReported, 0, 1)) >= minDelta;
            });
    }

    public static IProgress<T> Create<T>(
        Action<T> apply,
        int minIntervalMilliseconds = 125)
    {
        ArgumentNullException.ThrowIfNull(apply);
        return new ThrottledProgress<T>(apply, minIntervalMilliseconds, null);
    }

    private sealed class ThrottledProgress<T> : IProgress<T>
    {
        private readonly Action<T> _apply;
        private readonly int _minIntervalMilliseconds;
        private readonly Func<T, T, bool>? _reportImmediately;
        private readonly Lock _gate = new();

        private bool _hasReported;
        private T _lastReported = default!;
        private long _lastTick;
        private bool _hasPending;
        private T _pending = default!;
        private bool _flushScheduled;
        // Orders the dispatched values: a delayed (trailing) flush can race with an immediate report on another
        // thread, the UI must never apply an older value after a newer one (e.g. 0.98 after the final 1.0).
        private long _sequence;
        // Only accessed on the UI thread
        private long _appliedSequence;

        public ThrottledProgress(Action<T> apply, int minIntervalMilliseconds, Func<T, T, bool>? reportImmediately)
        {
            _apply = apply;
            _minIntervalMilliseconds = Math.Max(0, minIntervalMilliseconds);
            _reportImmediately = reportImmediately;
        }

        public void Report(T value)
        {
            var reportNow = false;
            var flushDelay = 0;
            long sequence = 0;
            lock (_gate)
            {
                var now = Environment.TickCount64;
                var elapsed = now - _lastTick;
                reportNow = !_hasReported ||
                            elapsed >= _minIntervalMilliseconds ||
                            (_reportImmediately?.Invoke(value, _lastReported) ?? false);

                if (reportNow)
                {
                    _hasReported = true;
                    _lastReported = value;
                    _lastTick = now;
                    _hasPending = false;
                    sequence = ++_sequence;
                }
                else
                {
                    // Remember the latest value and make sure it is delivered once the interval has passed
                    _pending = value;
                    _hasPending = true;
                    if (!_flushScheduled)
                    {
                        _flushScheduled = true;
                        flushDelay = (int)Math.Max(1, _minIntervalMilliseconds - elapsed);
                    }
                }
            }

            if (reportNow)
            {
                Dispatch(value, sequence);
            }
            else if (flushDelay > 0)
            {
                _ = FlushLaterAsync(flushDelay);
            }
        }

        private async Task FlushLaterAsync(int delayMilliseconds)
        {
            await Task.Delay(delayMilliseconds).ConfigureAwait(false);

            T value;
            long sequence;
            lock (_gate)
            {
                _flushScheduled = false;
                if (!_hasPending)
                {
                    return;
                }

                value = _pending;
                _hasPending = false;
                _hasReported = true;
                _lastReported = value;
                _lastTick = Environment.TickCount64;
                sequence = ++_sequence;
            }

            Dispatch(value, sequence);
        }

        private void Dispatch(T value, long sequence)
        {
            if (Dispatcher.UIThread.CheckAccess())
            {
                ApplyIfLatest(value, sequence);
            }
            else
            {
                Dispatcher.UIThread.Post(() => ApplyIfLatest(value, sequence), DispatcherPriority.Background);
            }
        }

        private void ApplyIfLatest(T value, long sequence)
        {
            if (sequence <= _appliedSequence)
            {
                return;
            }

            _appliedSequence = sequence;
            _apply(value);
        }
    }
}
