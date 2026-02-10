
using System;
using System.IO;
using System.IO.Ports;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace GasLabApp
{
    /// <summary>
    /// A robust RS-232 Reference around System.IO.Ports.SerialPort (v10).
    /// - Safe open/close and disposal
    /// - Thread-safe writes
    /// - Event-driven line reception via DataReceived
    /// - Optional async read loop via BaseStream for raw/binary protocols
    /// - Configurable timeouts, newline, encoding, and flow control
    /// </summary>
    public  class SerialPortClient : IDisposable
    {

        private readonly SerialPort _port;
        private readonly object _writeLock = new object();
        private CancellationTokenSource? _readLoopCts;
        private Task? _readLoopTask;

        /// <summary>
        /// Raised when a complete line is received (split by NewLine).
        /// </summary>
        public event EventHandler<string>? LineReceived;

        /// <summary>
        /// Raised when raw bytes are received (only in async read loop mode).
        /// </summary>
        public event EventHandler<byte[]>? BytesReceived;

        /// <summary>
        /// Raised when an error occurs (IO exceptions, port errors, etc.).
        /// </summary>
        public event EventHandler<Exception>? ErrorOccurred;

        /// <summary>
        /// Construct with typical serial parameters. Defaults fit many RS-232 devices.
        /// </summary>
        public SerialPortClient(
            string portName,
            int baudRate = 9600,
            Parity parity = Parity.None,
            int dataBits = 8,
            StopBits stopBits = StopBits.One,
            Handshake handshake = Handshake.None,
            int readTimeoutMs = 10000,
            int writeTimeoutMs = 10000,
            string newLine = "\r\n",
            Encoding? encoding = null,
            bool dtrEnable = false,
            bool rtsEnable = false,
            bool enableDataReceivedLines = false)
        {
            EnsurePortExists(portName);
            _port = new SerialPort(portName, baudRate, parity, dataBits, stopBits)
            {
                Handshake = handshake,
                ReadTimeout = readTimeoutMs,
                WriteTimeout = writeTimeoutMs,
                NewLine = newLine,
                Encoding = Encoding.ASCII,
                DtrEnable = dtrEnable,
                RtsEnable = rtsEnable
            };

            if (enableDataReceivedLines)           
                _port.DataReceived += OnDataReceived;

            _port.ErrorReceived += OnErrorReceived;


            // Event-driven approach for line-based ASCII devices.
            //_port.DataReceived += OnDataReceived;
            //_port.ErrorReceived += OnErrorReceived;
        }

        /// <summary>Whether the port is currently open.</summary>
        public bool IsOpen => _port.IsOpen;

        ///Check that port exists before opening

        public void EnsurePortExists(string portName)
        {
            var exists = Array.Exists(SerialPort.GetPortNames(),
                p => string.Equals(p, portName, StringComparison.OrdinalIgnoreCase));
            if (!exists)
                throw new InvalidOperationException(
                    $"Port {portName} not found. Available: {string.Join(", ", SerialPort.GetPortNames())}");
        }


        /// <summary>Open the serial port.</summary>
        public void Open()
        {

            if (IsOpen) return;
            _port.Open();
        }

        /// <summary>Close the serial port and stop any read loop.</summary>
        public void Close()
        {
            StopAsyncReadLoop();

            if (IsOpen)
            {
                try { _port.Close(); }
                catch (Exception ex) { ErrorOccurred?.Invoke(this, ex); }
            }
        }

        /// <summary>Send a raw byte array.</summary>
        public void WriteBytes(byte[] data)
        {
            if (!IsOpen) throw new InvalidOperationException("Port is not open.");
            lock (_writeLock)
            {
                _port.Write(data, 0, data.Length);
            }
        }

        /// <summary>Send a string exactly as provided (no newline appended).</summary>
        public void WriteString(string text)
        {
            if (!IsOpen) throw new InvalidOperationException("Port is not open.");
            lock (_writeLock)
            {
                var bytes = _port.Encoding.GetBytes(text);
                _port.Write(bytes, 0, bytes.Length);
            }
        }

        /// <summary>Send a line (appends SerialPort.NewLine).</summary>
        public void WriteLine(string line)
        {
            if (!IsOpen) throw new InvalidOperationException("Port is not open.");
            lock (_writeLock)
            {
                _port.WriteLine(line);
            }
        }

        /// <summary>Synchronously read a line (blocks until NewLine or timeout).</summary>
        public string ReadLine()
        {
            if (!IsOpen) throw new InvalidOperationException("Port is not open.");
            return _port.ReadLine();
        }

        /// <summary>Synchronously read available bytes (up to count) or until timeout.</summary>
        public int ReadBytes(byte[] buffer, int offset, int count)
        {
            if (!IsOpen) throw new InvalidOperationException("Port is not open.");
            return _port.Read(buffer, offset, count);
        }

        /// <summary>Enable/disable DTR (some instruments require DTR=ON).</summary>
        public void SetDtr(bool enabled) => _port.DtrEnable = enabled;

        /// <summary>Enable/disable RTS (used with RTS/CTS hardware flow control).</summary>
        public void SetRts(bool enabled) => _port.RtsEnable = enabled;

        /// <summary>
        /// Starts an async read loop using SerialPort.BaseStream for non-blocking reads.
        /// Recommended for binary protocols or when you don't want to rely on DataReceived.
        /// </summary>
        public void StartAsyncReadLoop(int bufferSize = 1024)
        {
            if (_readLoopTask != null && !_readLoopTask.IsCompleted) return;
            if (!IsOpen) throw new InvalidOperationException("Port is not open.");

            _readLoopCts = new CancellationTokenSource();
            var ct = _readLoopCts.Token;

            _readLoopTask = Task.Run(async () =>
            {
                var buffer = new byte[bufferSize];
                try
                {
                    while (!ct.IsCancellationRequested && IsOpen)
                    {
                        int read = 0;
                        try
                        {
                            read = await _port.BaseStream
                                .ReadAsync(buffer, 0, buffer.Length, ct)
                                .ConfigureAwait(false);
                        }
                        catch (OperationCanceledException) { break; }
                        catch (TimeoutException) { /* ignore timeouts */ }
                        catch (Exception ex)
                        {
                            ErrorOccurred?.Invoke(this, ex);
                            await Task.Delay(50, ct).ConfigureAwait(false);
                        }

                        if (read > 0)
                        {
                            var chunk = new byte[read];
                            Buffer.BlockCopy(buffer, 0, chunk, 0, read);
                            BytesReceived?.Invoke(this, chunk);
                            // If your device is line-based, you can parse chunk to lines here.
                        }
                    }
                }
                catch (Exception ex)
                {
                    ErrorOccurred?.Invoke(this, ex);
                }
            }, ct);
        }

        /// <summary>Stops the async read loop if running.</summary>
        public void StopAsyncReadLoop()
        {
            try
            {
                _readLoopCts?.Cancel();
                _readLoopTask?.Wait(500);
            }
            catch { /* ignore */ }
            finally
            {
                _readLoopTask = null;
                _readLoopCts?.Dispose();
                _readLoopCts = null;
            }
        }

        private void OnDataReceived(object? sender, SerialDataReceivedEventArgs e)
        {
            // Useful for line-based ASCII devices: read complete lines
            try
            {
                while (_port.BytesToRead > 0)
                {
                    string line = _port.ReadLine();
                    LineReceived?.Invoke(this, line);
                }
            }
            catch (TimeoutException) { /* benign */ }
            catch (InvalidOperationException) { /* port closed; benign */ }
            catch (Exception ex)
            {
                ErrorOccurred?.Invoke(this, ex);
            }
        }

        private void OnErrorReceived(object? sender, SerialErrorReceivedEventArgs e)
        {
            ErrorOccurred?.Invoke(this, new IOException($"Serial port error: {e.EventType}"));
        }

        public void Dispose()
        {
            Close();
            _port.DataReceived -= OnDataReceived;
            _port.ErrorReceived -= OnErrorReceived;
            _port.Dispose();
        }
    }
}
