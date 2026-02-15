
using System;
using System.Collections.Concurrent;
using System.Diagnostics.Eventing.Reader;
using System.Globalization;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace GasLabApp
{
    /// <summary>
    /// CPC6050 client with buffered line parsing (tolerant of partial frames).
    /// Uses SerialPortClient's async BytesReceived stream; 
    /// </summary>
    public sealed  class Cpc6050Client : IDisposable, IController
    {
        private readonly SerialPortClient _sp;
        private readonly object _ioLock = new();            // serialize request/response
        private readonly StringBuilder _rxBuf = new(512);   // byte-to-text buffer
        private readonly ConcurrentQueue<string> _lineQ = new(); // parsed lines
        private TaskCompletionSource<string>? _waiter;      // next line waiter
        private readonly object _waiterLock = new();        // protect waiter
        private volatile bool _opened;

        public Cpc6050Client(SerialPortClient serialPortClient)
        {
            _sp = serialPortClient ?? throw new ArgumentNullException(nameof(serialPortClient));
        }

        public bool IsOpen => _sp.IsOpen;

        public void Open()
        {
            if (_opened) return;
            _sp.Open();

            // IMPORTANT: use async read loop; disable DataReceived lines in SerialPortClient
            // by passing enableDataReceivedLines=false in its constructor ,
            // so only BytesReceived is active.
            _sp.BytesReceived += OnBytesReceived;
            _sp.StartAsyncReadLoop();

            _opened = true;
        }

        public void Close()
        {
            if (!_opened) return;
            _sp.BytesReceived -= OnBytesReceived;
            _sp.StopAsyncReadLoop();
            _sp.Close();
            _opened = false;
        }

        public void Dispose()
        {
            Close();
            _sp.Dispose();
        }

        // --------- RX parsing (CRLF tolerant) ---------

        private void OnBytesReceived(object? sender, byte[] chunk)
        {
            // Convert bytes to text with the port's encoding
            var text = _spEncodingSafeGetString(chunk);

            lock (_rxBuf)
            {
                _rxBuf.Append(text);

                // Extract complete lines. Prefer CRLF; also handle stray CR or LF.
                while (true)
                {
                    var s = _rxBuf.ToString();
                    int idx = s.IndexOf("\r\n", StringComparison.Ordinal);
                    int sepLen = 2;

                    if (idx < 0)
                    {
                        // If no CRLF, tolerate either CR or LF alone.
                        int iCr = s.IndexOf('\r');
                        int iLf = s.IndexOf('\n');
                        if (iCr >= 0 && (iLf < 0 || iCr < iLf)) { idx = iCr; sepLen = 1; }
                        else if (iLf >= 0) { idx = iLf; sepLen = 1; }
                        else break; // no complete line yet
                    }

                    var line = s.Substring(0, idx);
                    _rxBuf.Remove(0, idx + sepLen);

                    line = line.TrimEnd('\r', '\n');

                    // Push parsed line; fulfill waiter if present
                    _lineQ.Enqueue(line);
                    TryCompleteWaiter(line);
                }
            }
        }

        private string _spEncodingSafeGetString(byte[] chunk)
        {
            try { return _spEncoding.GetString(chunk); }
            catch { return Encoding.ASCII.GetString(chunk); }
        }

        private Encoding _spEncoding => _spEncodingField ??= (_spEncodingField = GetEncoding());
        private Encoding? _spEncodingField;
        private Encoding GetEncoding() => Encoding.ASCII; // SerialPortClient sets Encoding; ASCII by default

        private void TryCompleteWaiter(string line)
        {
            lock (_waiterLock)
            {
                if (_waiter != null && !_waiter.Task.IsCompleted)
                    _waiter.TrySetResult(line);
            }
        }

        private async Task<string> DequeueLineAsync(int timeoutMs, CancellationToken ct = default)
        {
            // Fast path: if a line is already queued, return it
            if (_lineQ.TryDequeue(out var line))
                return line;

            // Otherwise, create a waiter for the next line
            var tcs = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
            lock (_waiterLock) _waiter = tcs;

            using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            cts.CancelAfter(timeoutMs);

            try
            {
                using (cts.Token.Register(() => tcs.TrySetCanceled(), useSynchronizationContext: false))
                {
                    return await tcs.Task.ConfigureAwait(false);
                }
            }
            finally
            {
                lock (_waiterLock)
                {
                    if (_waiter == tcs) _waiter = null;
                }
            }
        }

        // --------- CPC6050 response normalization ---------

        private static string CleanResponse(string raw, out bool hasErrorFlag)
        {
            hasErrorFlag = false;
            if (string.IsNullOrEmpty(raw)) return string.Empty;

            var s = raw;
            if (s.Length > 0 && s[0] == 'E')
            {
                hasErrorFlag = true; s = s.Substring(1);
            }
            else if (s.Length > 0 && s[0] == ' ')
            {
                s = s.Substring(1);
            }
            return s.Trim();
        }

        private static double ParseDoubleInvariant(string text)
        {
            if (double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var v))
                return v;
            throw new FormatException($"Invalid numeric: '{text}'");
        }

        // --------- Core request/response helpers ---------

        private string Query(string command, int timeoutMs = 10000)
        {
            lock (_ioLock)
            {
                // Drop any stale line(s) before sending a new command
                while (_lineQ.TryDequeue(out _)) { /* discard */ }

                // Send command
                _sp.WriteLine(command);
                
                // Wait for the next parsed line
                try
                {
                    var line = DequeueLineAsync(timeoutMs).GetAwaiter().GetResult();

                     //Optional: ignore echo if device ever echoes (default is echo OFF)
                    if (string.Equals(line, command, StringComparison.OrdinalIgnoreCase))
                    {
                        // Get the real response line
                        line = DequeueLineAsync(timeoutMs).GetAwaiter().GetResult();
                    }
                    
                    return line ?? string.Empty;
                }
                catch (TaskCanceledException)
                {
                    throw new TimeoutException($"Timed out waiting for response to '{command}'");
                }
            }
        }

        private void Command(string command, int timeoutMs = 10000)
        {
            lock (_ioLock)
            {
                // Drop any stale line(s) before sending a new command
                while (_lineQ.TryDequeue(out _)) { /* discard */ }

                // Send command
                _sp.WriteLine(command);



            }
        }

        // ------------ Public API  ------------

        public string Connect()
        {
            if (_sp == null) throw new InvalidOperationException("No Serial Port Available");
            if (!IsOpen) Open();

            var connection = new StringBuilder();
            connection.Append(Identify());
            return connection.ToString();
        }

        public void Disconnect()
        {
            if (IsOpen)
            {
                Close();
                Dispose();
            }
        }

        public string Identify()
        {
            var raw = Query("Id?");
            var resp = CleanResponse(raw, out var err);
            if (err) throw new InvalidOperationException($"Instrument error flag. Next error: {GetNextError() ?? "unknown"}");
            return resp;
        }

        public string? GetNextError()
        {
            var raw = Query("Error?");
            var resp = CleanResponse(raw, out _);
            return resp.Equals("NO ERRORS", StringComparison.OrdinalIgnoreCase) ? null : resp;
        }

        

        public PConChannel ReturnChVal(string ch) 
        { 
            if(ch.ToLower() =="a") return PConChannel.A;
            if(ch.ToLower() =="b") return PConChannel.B;
            else throw new InvalidOperationException("PConChannel value must be A or B");
        }
        public void SetChannel(PConChannel ch) => Command ($"Chan {(ch == PConChannel.A ? "A" : "B")}");
        public PConChannel GetChannel()
        {
            var raw = Query("Chan?");
            var resp = CleanResponse(raw, out var err);
            if (err) throw new InvalidOperationException($"Instrument error flag. Next error: {GetNextError() ?? "unknown"}");
            return resp == "A" ? PConChannel.A : PConChannel.B;
        }

        public double GetPressure(PConChannel ch)
        {
            var raw = Query(ch == PConChannel.A ? "A?" : "B?");
            var resp = CleanResponse(raw, out var err);
            if (err) throw new InvalidOperationException($"Error flag on read. Next error: {GetNextError() ?? "unknown"}");
            return ParseDoubleInvariant(resp);
        }

        public double GetReading()
        {
            var ch = GetChannel();
            var raw = Query(ch == PConChannel.A ? "A?" : "B?");
            var resp = CleanResponse(raw, out var err);
            if (err) throw new InvalidOperationException($"Error flag on read. Next error: {GetNextError() ?? "unknown"}");
            return ParseDoubleInvariant(resp);
        }

        public bool IsStable(PConChannel ch)
        {
            var raw = Query(ch == PConChannel.A ? "AS?" : "BS?");
            var resp = CleanResponse(raw, out var err);
            if (err) throw new InvalidOperationException($"Error flag on stability query. Next error: {GetNextError() ?? "unknown"}");
            return resp.Equals("YES", StringComparison.OrdinalIgnoreCase);
        }

        public bool IsStable()
        {
            var ch = GetChannel();
            var raw = Query(ch == PConChannel.A ? "AS?" : "BS?");
            var resp = CleanResponse(raw, out var err);
            if (err) throw new InvalidOperationException($"Error flag on stability query. Next error: {GetNextError() ?? "unknown"}");
            return resp.Equals("YES", StringComparison.OrdinalIgnoreCase);
        }

        public double GetRate(PConChannel ch)
        {
            var raw = Query(ch == PConChannel.A ? "AR?" : "BR?");
            var resp = CleanResponse(raw, out var err);
            if (err) throw new InvalidOperationException($"Error flag on rate query. Next error: {GetNextError() ?? "unknown"}");
            return ParseDoubleInvariant(resp);
        }

        

        public void SetMode(PconMode mode) => Command($" {mode.ToString().ToUpperInvariant()}");

        public bool IsMode(PconMode mode)
        {
            var raw = Query($"{mode.ToString()}?");
            var resp = CleanResponse(raw, out var err);
            if (err) throw new InvalidOperationException($"Error flag on mode query. Next error: {GetNextError() ?? "unknown"}");

             if (resp.StartsWith("YES", StringComparison.OrdinalIgnoreCase)) return true;
            if (resp.StartsWith($"{mode.ToString()}", StringComparison.OrdinalIgnoreCase)) return true;

            return false;
        }

 
        public PconMode GetMode()
        {
            if(IsMode(PconMode.Vent))return PconMode.Vent;
            if (IsMode(PconMode.Control)) return PconMode.Control;
            else return PconMode.Measure;
        }

        public void SetSetPoint(double value) => Command($"Setpt {value.ToString("G", CultureInfo.InvariantCulture)}");
        public double GetSetPoint()
        {
            var raw = Query("Setpt?");
            var resp = CleanResponse(raw, out var err);
            if (err) throw new InvalidOperationException($"Error flag on setpoint query. Next error: {GetNextError() ?? "unknown"}");
            return ParseDoubleInvariant(resp);
        }

        public void SetControlBehavior(int value)
        {
            if (value < 0 || value > 100) throw new ArgumentOutOfRangeException(nameof(value));
            _ = Query($"Control_behavior {value}");
        }

        public void SetControlRatePreset(string preset) => Command($"Crate {preset}");
        public void SetRateSetpoint(double value) => Command($"Rsetpt {value.ToString("G", CultureInfo.InvariantCulture)}");

        public bool WaitStable(PConChannel ch, TimeSpan timeout, TimeSpan pollInterval)
        {
            var deadline = DateTime.UtcNow + timeout;
            while (DateTime.UtcNow < deadline)
            {
                if (IsStable(ch)) return true;
                Thread.Sleep(pollInterval);
            }
            return false;
        }

        public void SetUnits(string unitToken) => Command($"Units {unitToken}");
        public string GetUnits()
        {
            var raw = Query("Units?");
            var resp = CleanResponse(raw, out var err);
            if (err) throw new InvalidOperationException($"Error flag on units query. Next error: {GetNextError() ?? "unknown"}");
            return resp;
        }

        
        public static Ptype GetPtypeFromString(string ptype)
        {
            if( ptype.ToString() == null ) throw new ArgumentNullException(nameof(ptype));
            if (ptype.ToString() == "Gauge") return Ptype.Gauge;
            if (ptype.ToString() == "GAUGE EMULATION") return Ptype.Gauge;
            if (ptype.ToString() == "Absolute") return Ptype.Abs;

            throw new InvalidOperationException("ValueMust be gauge or absolute");
        }


        public void SetPressureType(Ptype ptype) => Command($"Ptype {(ptype == Ptype.Gauge ? "Gauge" : "Absolute")}");

        public void SetPressureType(string type) => Command($"Ptype {type}");
        public Ptype GetPressureType()
        {
            var raw = Query("Ptype?");
            var resp = CleanResponse(raw, out var err);
            if (err) throw new InvalidOperationException($"Error flag on pressure-type query. Next error: {GetNextError() ?? "unknown"}");
            if (resp.ToLower() == "gauge") return Ptype.Gauge;
            if (resp.ToLower() == "gauge emulation") return Ptype.Gauge;
            if (resp.ToLower() == "absolute") return Ptype.Abs;
            else throw new InvalidOperationException("Pressure Type value must be Gauge or Absolute");
           
        }

        public void Vent() =>  Command("Vent");
        public void Measure() =>  Command("Measure");
        public void Control() => Command("Control");
    }

    //--Public Facing Enums
    public enum Ptype { Gauge, Abs }

    public enum PConChannel { A, B }

    public enum PconMode { Measure, Control, Vent }
}
