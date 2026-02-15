using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Timers;
using System.Windows.Documents;

namespace GasLabApp
{


    using System;
    using System.Diagnostics;
    using System.Threading;
    using System.Threading.Tasks;

    public sealed class AsyncStableTimer : IAsyncDisposable
    {
        private readonly Stopwatch _sw = new();
        private readonly TimeSpan _tickInterval;
        private readonly Func<TimeSpan, CancellationToken, Task>? _onTick;
        private CancellationTokenSource? _cts;
        private readonly object _lock = new();
       
        public bool IsRunning { get; private set; } = false;
        

        public AsyncStableTimer(
            TimeSpan tickInterval,
            Func<TimeSpan, CancellationToken, Task>? onTick = null)
        {
            _tickInterval = tickInterval;
            _onTick = onTick; // optional
        }

        /// <summary>Start counting and (optionally) ticking.</summary>
        public void Start()
        {
            lock (_lock)
            {
                if (_cts != null) return; // already running
                _cts = new CancellationTokenSource();
                _sw.Start();
                IsRunning = true;
                _ = LoopAsync(_cts.Token);
            }
        }

        /// <summary>Stop counting and stop ticks.</summary>
        public void Stop()
        {
            CancellationTokenSource? old;
            lock (_lock)
            {
                old = _cts;
                _cts = null;
                _sw.Stop();
                IsRunning = false;
            }
            old?.Cancel();
            old?.Dispose();
        }

        /// <summary>Reset elapsed to zero. If running, continues running from zero.</summary>
        public void Reset()
        {
            lock (_lock)
            {
                bool wasRunning = _cts != null;
                _sw.Reset();
                if (wasRunning) _sw.Start();
            }
        }

        /// <summary>Stopwatch restart (reset + start).</summary>
        public void Restart()
        {
            lock (_lock)
            {
                _sw.Restart();
                if (_cts == null)
                {
                    _cts = new CancellationTokenSource();
                    _ = LoopAsync(_cts.Token);
                }
            }
        }





        public TimeSpan Elapsed => _sw.Elapsed;

        private async Task LoopAsync(CancellationToken token)
        {
            try
            {
                while (!token.IsCancellationRequested)
                {
                    if (_onTick != null)
                        await _onTick(Elapsed, token).ConfigureAwait(false);

                    await Task.Delay(_tickInterval, token).ConfigureAwait(false);
                }
            }
            catch (OperationCanceledException) { /* normal */ }
        }

        public ValueTask DisposeAsync()
        {
            Stop();
            return ValueTask.CompletedTask;
        }
    }



    internal class StableTimer : IDisposable
    {
        private System.Timers.Timer Timer;
        private bool Stable =false;
        public static bool StableTimeElapsed { get; private set; } = false;
        public static int _secondsElapsed { get; private set; } = 0;
        private static int StablePeriod  = 0;


        public StableTimer( int interval)
        {
            StablePeriod = interval;
            Timer = new System.Timers.Timer(1000);
            Timer.AutoReset = true;
        }

        // check if the system is stable and enable to timer 
        public bool Run(bool stable)
        {
            if (Stable && !stable)
            {
                Timer.Stop();
                _secondsElapsed = 0;
            }


            Stable = stable;
            if (Stable && Timer.Enabled ==false)
            {
                Timer.Enabled = true;
            }
            Timer.Elapsed += OnTimedEvent;

            return StableTimeElapsed;
        }

        // Event handler for timer ticks
        private void  OnTimedEvent(object sender, ElapsedEventArgs e)
        {
            _secondsElapsed++;
            if(_secondsElapsed >= StablePeriod)
            {
                StableTimeElapsed = true;
            }
           
           
        }





        public void Dispose()
        {
           Timer?.Dispose();
        }
    }
}
