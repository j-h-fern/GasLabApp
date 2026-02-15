
using System;
using System.ComponentModel;
using System.Net.Sockets;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using GasLabApp;                 



namespace GasLabApp
{
    public sealed class CPC6050Monitor : INotifyPropertyChanged, IDisposable
    {
        private readonly Cpc6050Client _cpc;
        private CancellationTokenSource? _cts;
        private Task? _pollTask;

        private string _id = "";
        private string _mode = "";
        private string _units = "";
        private string _ptype = "";
        private string _channel = "A";   // "A" or "B"
        private double _pressure;
        private bool _stable;
        private string _error = "";
        private double _stableElapsed = 0;
        private bool _stableTime = false;
        private bool _step = false;


        public CPC6050Monitor(Cpc6050Client cpc)
        {
            _cpc = cpc ?? throw new ArgumentNullException(nameof(cpc));
        }

        public string DeviceId { get => _id; private set => Set(ref _id, value); }
        public string Mode { get => _mode; private set => Set(ref _mode, value); }
        public string Units { get => _units; private set => Set(ref _units, value); }
        public string PType { get => _ptype; private set => Set(ref _ptype, value); }
        public string Channel { get => _channel; set => Set(ref _channel, value); }
        public double Pressure { get => _pressure; private set => Set(ref _pressure, value); }
        public bool Stable { get => _stable; private set => Set(ref _stable, value); }
        public string Error { get => _error; private set => Set(ref _error, value); }
        public double StableElapsed { get => _stableElapsed; private set => Set(ref _stableElapsed, value);  }
        public bool StableTime { get => _stableTime; private set => Set(ref _stableTime, value); }
        public bool Step { get => _step; private set => Set(ref _step, value); }


        public async Task StartAsync(TimeSpan pollInterval)
        {
            if (_cts != null) return;
            _cts = new CancellationTokenSource();

            // One-time metadata/readiness 
            await Task.Run(() =>
            {
                try
                {
                    if (!_cpc.IsOpen) _cpc.Open();
                    var id = _cpc.Identify();
                    var mode = _cpc.GetMode().ToString();
                    var u = _cpc.GetUnits();
                    var pt = _cpc.GetPressureType().ToString();
               
                    

                    Application.Current.Dispatcher.Invoke(() =>
                    {
                        DeviceId = id;
                        Mode = mode;
                        Units = u;
                        PType = pt;
                        Error = "";

                        

                    });
                }
                catch (Exception ex)
                {
                    Application.Current.Dispatcher.Invoke(() =>
                    {
                        Error = $"Startup error: {ex.Message}";
                    });
                }
            }).ConfigureAwait(false);

            // Background poll loop
            _pollTask = Task.Run(async () =>
            {
                var ct = _cts.Token;
                while (!ct.IsCancellationRequested)
                {
                    try
                    {
                        // choose channel from property
                        var ch = (Channel.Equals("A", StringComparison.OrdinalIgnoreCase))
                                ? PConChannel.A
                                : PConChannel.B;

                        // read values (synchronous client calls OK inside a background thread)
                        var p = _cpc.GetPressure(ch);
                        var st = _cpc.IsStable(ch);
                        var md = _cpc.GetMode().ToString();
                        var pt = _cpc.GetPressureType().ToString();
                        var u = _cpc.GetUnits();
                        
                      
                        
                      


                        Application.Current.Dispatcher.Invoke(() =>
                        {
                            
                            Pressure = p;
                            Stable = st;
                            Units = u;
                            Mode = md;
                            this.PType = pt;
                            Error = "";
                            Step = !Step;

                        });
                    }
                    catch (TimeoutException tex)
                    {
                        Application.Current.Dispatcher.Invoke(() =>
                        {
                            Error = $"Timeout: {tex.Message}";
                        });
                    }
                    catch (Exception ex)
                    {
                        Application.Current.Dispatcher.Invoke(() =>
                        {
                            Error = ex.Message;
                        });
                    }

                    try { await Task.Delay(pollInterval, ct).ConfigureAwait(false); }
                    catch (TaskCanceledException) { /* loop ends */ }
                }
            }, _cts.Token);
        }








        public void Stop()
        {
            try
            {
                _cts?.Cancel();
                _pollTask?.Wait(300);
            }
            catch { /* ignore */ }
            finally
            {
                _pollTask = null;
                _cts?.Dispose();
                
                _cts = null;
            }
        }

        public void Dispose()
        {
            Stop();
            try { if (_cpc.IsOpen) _cpc.Close(); } catch { /* ignore */ }
        }

        




        // --------------- INotifyPropertyChanged ---------------
        public event PropertyChangedEventHandler? PropertyChanged;
        private void OnPropertyChanged([CallerMemberName] string? name = null)
            => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
        private void Set<T>(ref T field, T value, [CallerMemberName] string? prop = null)
        {
            if (!Equals(field, value))
            {
                field = value;
                OnPropertyChanged(prop);
            }
        }
    }
}
