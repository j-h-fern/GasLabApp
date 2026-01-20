
using System;
using System.Globalization;
using System.IO.Ports;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Collections.Generic;

namespace GasLabApp
{
    /// <summary>
    /// Emulates CPC6050 over a serial port (ASCII, CRLF responses).
    /// Responses begin with leading ' ' (space) when no errors are queued,
    /// or 'E' when there is at least one pending error (drain via Error?).
    /// Non-blocking async read loop using BaseStream with CRLF line parsing.
    /// </summary>
    public sealed class Cpc6050Emulator : SerialPortClient, IDisposable
    {
        private readonly SerialPort _port;
        private readonly CancellationTokenSource _cts = new();
        private Task? _readLoopTask;
        private Task? _physicsTask;

        // ----- Instrument state -----
        public enum Channel { A, B }
        public enum Mode { Measure, Control, Vent }

        private Channel _activeChannel = Channel.A;
        private Mode _mode = Mode.Vent;

        private string _units = "KPA";     // Units token
        private string _ptype = "Gauge";   // "Gauge" or "Absolute"

        private double _pA = 0.0;          // Channel A pressure (units)
        private double _pB = 0.0;          // Channel B pressure (units)
        private double _setpt = 0.0;       // Control setpoint (units)
        private double _rateSetpt = 5.0;   // Variable rate setpoint (units/sec)
        private string _ratePreset = "Variable"; // "Slow"|"Medium"|"Fast"|"Variable"
        private int _controlBehavior = 50; // 0..100

        // Dynamics & stability:
        private const double StableWindow = 0.05;     // ±0.05 units considered "stable"
        private const double StableHoldSeconds = 1.0; // must remain within window for >=1s
        private DateTime _stableEntryA = DateTime.MinValue;
        private DateTime _stableEntryB = DateTime.MinValue;
        private double _lastPA = 0.0;
        private double _lastPB = 0.0;
        private DateTime _lastUpdate = DateTime.UtcNow;

        // Error queue & synchronization:
        private readonly object _stateLock = new();
        private readonly Queue<string> _errorQueue = new();

        // Identity (dummy but realistic):
        private readonly string _idn = "MENSOR,CPC6050,123456,1.23";

        public Cpc6050Emulator(
            string portName,
            int baudRate = 9600,
            Parity parity = Parity.None,
            int dataBits = 8,
            StopBits stopBits = StopBits.One,
            int readTimeoutMs = 2000,
            int writeTimeoutMs = 2000):base(portName)
        {
            EnsurePortExists(portName);
            _port = new SerialPort(portName, baudRate, parity, dataBits, stopBits)
            {
                ReadTimeout = readTimeoutMs,
                WriteTimeout = writeTimeoutMs,
                NewLine = "\r\n",
                Encoding = Encoding.ASCII,
                Handshake = Handshake.None,
                DtrEnable = false,
                RtsEnable = false
            };

 
        }

        public bool IsOpen => _port.IsOpen;

        public void Start()
        {
            if (IsOpen) return;
            _port.Open();

            var ct = _cts.Token;

            // Physics loop: simple real-time plant model (20 Hz).
            _physicsTask = Task.Run(async () =>
            {
                while (!ct.IsCancellationRequested)
                {
                    StepPhysics();
                    await Task.Delay(50, ct).ConfigureAwait(false);
                }
            }, ct);

            // Async read loop: accumulate bytes and split on CRLF.
            _readLoopTask = Task.Run(async () =>
            {
                var sb = new StringBuilder(256);
                var buf = new byte[1024];

                while (!ct.IsCancellationRequested)
                {
                    int n = 0;
                    try
                    {
                        n = await _port.BaseStream.ReadAsync(buf, 0, buf.Length, ct).ConfigureAwait(false);
                    }
                    catch (OperationCanceledException) { break; }
                    catch (TimeoutException) { continue; } // not expected with async, but harmless
                    catch
                    {
                        // swallow read exceptions in an emulator context and continue
                        continue;
                    }

                    if (n <= 0) continue;

                    sb.Append(_port.Encoding.GetString(buf, 0, n));

                    // Split on CRLF
                    while (true)
                    {
                        var s = sb.ToString();
                        var idx = s.IndexOf("\r\n", StringComparison.Ordinal);
                        if (idx < 0) break;

                        var line = s.Substring(0, idx);
                        sb.Remove(0, idx + 2);

                        var trimmed = line.Trim();
                        if (trimmed.Length == 0) continue;

                        HandleCommandSafe(trimmed);
                    }
                }
            }, ct);
        }

        public void Stop()
        {
            try
            {
                _cts.Cancel();
                _physicsTask?.Wait(500);
                _readLoopTask?.Wait(500);
            }
            catch { /* ignore */ }
            finally
            {
                if (IsOpen)
                {
                    try { _port.Close(); } catch { /* ignore */ }
                }
            }
        }

        public void Dispose()
        {
            Stop();
            // Ensure no lingering subscriptions (we don't use DataReceived now).
            // _port.DataReceived -= OnDataReceived;
            _port.Dispose();
            _cts.Dispose();
        }

        // ----- Physics simulation -----
        private void StepPhysics()
        {
            lock (_stateLock)
            {
                var now = DateTime.UtcNow;
                var dt = (now - _lastUpdate).TotalSeconds;
                if (dt <= 0) dt = 0.001;

                double rate = _ratePreset switch
                {
                    "Slow" => 1.0,
                    "Medium" => 3.0,
                    "Fast" => 10.0,
                    _ => _rateSetpt > 0 ? _rateSetpt : 5.0
                };

                // Control behavior: higher = faster response
                rate *= Math.Max(0.25, _controlBehavior / 50.0); // 0.25x .. 2x

                // Targets per mode
                double targetA = _mode == Mode.Control ? _setpt
                               : _mode == Mode.Vent ? 0.0
                               : _pA; // Measure: hold

                double targetB = _mode == Mode.Control ? _setpt
                               : _mode == Mode.Vent ? 0.0
                               : _pB; // Measure: hold

                // Slew both channels for simplicity
                Slew(ref _pA, targetA, rate, dt);
                Slew(ref _pB, targetB, rate, dt);

                // Stability windows
                UpdateStability(ref _stableEntryA, _pA, targetA);
                UpdateStability(ref _stableEntryB, _pB, targetB);

                _lastPA = _pA;
                _lastPB = _pB;
                _lastUpdate = now;
            }
        }

        private static void Slew(ref double value, double target, double rate, double dt)
        {
            var delta = target - value;
            var maxStep = rate * dt;
            if (Math.Abs(delta) <= maxStep) value = target;
            else value += Math.Sign(delta) * maxStep;
        }

        private static void UpdateStability(ref DateTime stableEntry, double pressure, double target)
        {
            if (Math.Abs(pressure - target) <= StableWindow)
            {
                if (stableEntry == DateTime.MinValue)
                    stableEntry = DateTime.UtcNow;
            }
            else
            {
                stableEntry = DateTime.MinValue;
            }
        }

        private bool IsStable(Channel ch)
        {
            var t = ch == Channel.A ? _stableEntryA : _stableEntryB;
            return t != DateTime.MinValue && (DateTime.UtcNow - t).TotalSeconds >= StableHoldSeconds;
        }

        // ----- Command handling -----
        private void HandleCommandSafe(string cmd)
        {
            string payload;
            try
            {
                // Compute response (and mutate state) under lock
                lock (_stateLock)
                {
                    payload = ComputeResponse(cmd);
                }
            }
            catch (Exception ex)
            {
                lock (_stateLock)
                {
                    _errorQueue.Enqueue(ex.Message);
                    payload = "SYNTAX ERROR";
                }
            }

            // Write after releasing the lock
            WriteResponse(payload);
        }

        /// <summary>
        /// Parses a single command and returns the response payload (without prefix or CRLF).
        /// This method is always called under _stateLock.
        /// </summary>
        private string ComputeResponse(string cmdLine)
        {
            var parts = cmdLine.Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length == 0) return string.Empty;

            string head = parts[0].ToUpperInvariant();

            switch (head)
            {
                case "ID?":
                case "IDN?":
                    return _idn;

                case "CHAN":
                    if (parts.Length < 2) throw new ArgumentException("CHAN requires A or B");
                    _activeChannel = parts[1].Equals("A", StringComparison.OrdinalIgnoreCase) ? Channel.A : Channel.B;
                    return $"CHAN {(_activeChannel == Channel.A ? "A" : "B")}";

                case "CHAN?":
                    return _activeChannel == Channel.A ? "A" : "B";

                case "UNITS":
                    if (parts.Length < 2) throw new ArgumentException("UNITS requires token");
                    _units = parts[1].ToUpperInvariant();
                    return _units;

                case "UNITS?":
                    return _units;

                case "PTYPE":
                    if (parts.Length < 2) throw new ArgumentException("PTYPE requires Gauge or Absolute");
                    _ptype = Capitalize(parts[1]);
                    return _ptype;

                case "PTYPE?":
                    return _ptype;

                case "MODE":
                    if (parts.Length < 2) throw new ArgumentException("MODE requires MEASURE|CONTROL|VENT");
                    _mode = ParseMode(parts[1]);
                    return _mode.ToString().ToUpperInvariant();

                case "MODE?":
                    return _mode.ToString().ToUpperInvariant();

                case "SETPT":
                    if (parts.Length < 2) throw new ArgumentException("SETPT requires value");
                    _setpt = ParseDouble(parts[1]);
                    return FormatNumber(_setpt);

                case "SETPT?":
                    return FormatNumber(_setpt);

                case "CRATE":
                    if (parts.Length < 2) throw new ArgumentException("CRATE requires Slow|Medium|Fast|Variable");
                    _ratePreset = Capitalize(parts[1]);
                    return _ratePreset;

                case "RSETPT":
                    if (parts.Length < 2) throw new ArgumentException("RSETPT requires value (units/sec)");
                    _rateSetpt = ParseDouble(parts[1]);
                    return FormatNumber(_rateSetpt);

                case "CONTROL_BEHAVIOR":
                    if (parts.Length < 2) throw new ArgumentException("CONTROL_BEHAVIOR requires 0..100");
                    _controlBehavior = (int)ParseDouble(parts[1]);
                    _controlBehavior = Math.Clamp(_controlBehavior, 0, 100);
                    return _controlBehavior.ToString(CultureInfo.InvariantCulture);

                case "CONTROL":
                    _mode = Mode.Control;
                    return "CONTROL";

                case "MEASURE":
                    _mode = Mode.Measure;
                    return "MEASURE";

                case "VENT":
                    _mode = Mode.Vent;
                    return "VENT";

                case "A?":
                    return FormatNumber(_pA);

                case "B?":
                    return FormatNumber(_pB);

                case "AS?":
                    return IsStable(Channel.A) ? "YES" : "NO";

                case "BS?":
                    return IsStable(Channel.B) ? "YES" : "NO";

                case "AR?":
                    {
                        var dt = Math.Max(1e-3, (DateTime.UtcNow - _lastUpdate).TotalSeconds);
                        var rate = Math.Abs(_pA - _lastPA) / dt;
                        return FormatNumber(rate);
                    }

                case "BR?":
                    {
                        var dt = Math.Max(1e-3, (DateTime.UtcNow - _lastUpdate).TotalSeconds);
                        var rate = Math.Abs(_pB - _lastPB) / dt;
                        return FormatNumber(rate);
                    }

                case "ERROR?":
                    if (_errorQueue.Count == 0) return "NO ERRORS";
                    return _errorQueue.Dequeue();

                default:
                    _errorQueue.Enqueue($"UNKNOWN CMD: {cmdLine}");
                    return "UNKNOWN COMMAND";
            }
        }

        private static string Capitalize(string s) =>
            string.IsNullOrEmpty(s) ? s : char.ToUpperInvariant(s[0]) + s.Substring(1).ToLowerInvariant();

        private static Mode ParseMode(string s) =>
            s.StartsWith("MEAS", StringComparison.OrdinalIgnoreCase) ? Mode.Measure
          : s.StartsWith("CONT", StringComparison.OrdinalIgnoreCase) ? Mode.Control
          : Mode.Vent;

        private static double ParseDouble(string text)
        {
            if (double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var v))
                return v;
            throw new FormatException($"Invalid numeric: '{text}'");
        }

        private static string FormatNumber(double v)
        {
            // CPC6050 uses exponential; include leading '+' for positive values.
            var s = v.ToString("E6", CultureInfo.InvariantCulture); // e.g., -1.234567E+02
            return v >= 0 ? "+" + s : s;
        }

        private void WriteResponse(string payload)
        {
            // Prefix: 'E' if errors queued; otherwise leading space
            string prefix;
            lock (_stateLock)
            {
                prefix = _errorQueue.Count > 0 ? "E" : " ";
            }

            var line = prefix + payload + _port.NewLine; // Ensure CRLF exactly once
            try
            {
                _port.Write(line);
            }
            catch
            {
                // swallow in emulator; real device would not do this
            }
        }
    }
}
