using System;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;

namespace WarthogLedControl
{
    /// <summary>
    /// DCS-BIOS UDP listener.
    ///
    /// DCS-BIOS export protocol:
    ///
    ///   Frame sync:
    ///       55 55 55 55
    ///
    ///   Write:
    ///       address low
    ///       address high
    ///       length low
    ///       length high
    ///       data...
    ///
    /// All multi-byte values are little-endian.
    ///
    /// Example:
    ///   60 75 02 00 CD A6
    ///
    /// means:
    ///   Address = 0x7560
    ///   Length  = 2
    ///   Value   = 0xA6CD = 42669
    /// </summary>
    public sealed class DcsBiosListener : IDisposable
    {
        // --------------------------------------------------------------------
        // DCS-BIOS network configuration
        // --------------------------------------------------------------------

        public const string MulticastAddress = "239.255.50.10";
        public const int Port = 5010;

        private readonly IPAddress _multicastAddress =
            IPAddress.Parse(MulticastAddress);

        private UdpClient? _udpClient;
        private CancellationTokenSource? _cts;
        private Task? _receiveTask;

        private bool _running;

        // --------------------------------------------------------------------
        // Debug configuration
        // --------------------------------------------------------------------

        /// <summary>
        /// Enable detailed byte-level logging.
        ///
        /// This is intentionally false by default because DCS-BIOS produces
        /// a large amount of traffic.
        /// </summary>
        public bool DebugBytes { get; set; } = false;

        /// <summary>
        /// Print every decoded DCS-BIOS write.
        /// </summary>
        public bool DebugWrites { get; set; } = true;

        /// <summary>
        /// Print every frame synchronization.
        /// </summary>
        public bool DebugFrames { get; set; } = true;

        /// <summary>
        /// Only print writes to this address when non-zero.
        ///
        /// Example:
        ///     DebugAddress = 0x7560;
        ///
        /// Set to null to print all addresses.
        /// </summary>
        public ushort? DebugAddress { get; set; } = 0x7560;

        // --------------------------------------------------------------------
        // Events
        // --------------------------------------------------------------------

        /// <summary>
        /// Fired whenever DCS-BIOS delivers a complete write.
        ///
        /// address = 16-bit DCS-BIOS address
        /// data    = decoded 16-bit value
        /// </summary>
        public event Action<ushort, ushort>? DcsBiosWrite;

        /// <summary>
        /// Fired when the DCS-BIOS frame synchronization sequence
        /// 55 55 55 55 is received.
        /// </summary>
        public event Action? FrameSync;

        /// <summary>
        /// Fired for diagnostic messages.
        /// </summary>
        public event Action<string>? LogMessage;

        // --------------------------------------------------------------------
        // Statistics
        // --------------------------------------------------------------------

        public long ReceivedUdpPackets { get; private set; }

        public long ReceivedBytes { get; private set; }

        public long ReceivedFrames { get; private set; }

        public long ReceivedWrites { get; private set; }

        public long ParserErrors { get; private set; }

        // --------------------------------------------------------------------
        // Parser
        // --------------------------------------------------------------------

        private readonly DcsBiosProtocolParser _parser;

        public DcsBiosListener()
        {
            _parser = new DcsBiosProtocolParser();

            _parser.FrameSync += OnParserFrameSync;
            _parser.Write += OnParserWrite;
            _parser.Log += message => Log(message);
        }

        // --------------------------------------------------------------------
        // Start
        // --------------------------------------------------------------------

        public void Start()
        {
            if (_running)
                return;

            _running = true;

            _cts = new CancellationTokenSource();

            try
            {
                _udpClient = new UdpClient();

                // ----------------------------------------------------------------
                // IMPORTANT:
                //
                // DCS-BIOS allows multiple clients to listen to the multicast
                // stream. Windows therefore needs SO_REUSEADDR.
                // ----------------------------------------------------------------

                _udpClient.Client.SetSocketOption(
                    SocketOptionLevel.Socket,
                    SocketOptionName.ReuseAddress,
                    true);

                // Bind to the DCS-BIOS port.
                //
                // 0.0.0.0 allows Windows to receive multicast packets on
                // the selected interface.
                _udpClient.Client.Bind(
                    new IPEndPoint(IPAddress.Any, Port));

                // Join DCS-BIOS multicast group.
                _udpClient.JoinMulticastGroup(_multicastAddress);

                Log(
                    $"DCS-BIOS listener started: " +
                    $"{MulticastAddress}:{Port}");

                _receiveTask = Task.Run(
                    () => ReceiveLoopAsync(_cts.Token),
                    _cts.Token);
            }
            catch
            {
                _running = false;

                _udpClient?.Dispose();
                _udpClient = null;

                throw;
            }
        }

        // --------------------------------------------------------------------
        // Stop
        // --------------------------------------------------------------------

        public void Stop()
        {
            if (!_running)
                return;

            _running = false;

            try
            {
                _cts?.Cancel();
            }
            catch
            {
                // Ignore cancellation errors.
            }

            try
            {
                if (_udpClient != null)
                {
                    try
                    {
                        _udpClient.DropMulticastGroup(_multicastAddress);
                    }
                    catch
                    {
                        // Socket may already be closed.
                    }

                    _udpClient.Close();
                    _udpClient.Dispose();
                    _udpClient = null;
                }
            }
            catch
            {
                // Ignore shutdown errors.
            }

            Log("DCS-BIOS listener stopped.");
        }

        // --------------------------------------------------------------------
        // UDP receive loop
        // --------------------------------------------------------------------

        private async Task ReceiveLoopAsync(CancellationToken cancellationToken)
        {
            if (_udpClient == null)
                return;

            while (!cancellationToken.IsCancellationRequested)
            {
                try
                {
                    UdpReceiveResult result =
                        await _udpClient.ReceiveAsync(
                            cancellationToken);

                    ReceivedUdpPackets++;

                    byte[] buffer = result.Buffer;

                    ReceivedBytes += buffer.Length;

                    if (DebugBytes)
                    {
                        Log(
                            $"UDP packet: " +
                            $"{buffer.Length} bytes " +
                            $"from {result.RemoteEndPoint}");
                    }

                    // --------------------------------------------------------
                    // IMPORTANT:
                    //
                    // Do NOT try to interpret the UDP packet boundaries as
                    // DCS-BIOS message boundaries.
                    //
                    // The parser receives every byte individually.
                    // A DCS-BIOS write can be located anywhere in a UDP packet.
                    // --------------------------------------------------------

                    foreach (byte b in buffer)
                    {
                        if (DebugBytes)
                        {
                            Log($"RX BYTE: 0x{b:X2}");
                        }

                        _parser.ProcessChar(b);
                    }
                }
                catch (OperationCanceledException)
                {
                    break;
                }
                catch (ObjectDisposedException)
                {
                    break;
                }
                catch (SocketException ex)
                {
                    if (!cancellationToken.IsCancellationRequested)
                    {
                        ParserErrors++;

                        Log(
                            $"UDP socket error: " +
                            $"{ex.SocketErrorCode} - {ex.Message}");
                    }

                    break;
                }
                catch (Exception ex)
                {
                    ParserErrors++;

                    Log(
                        $"DCS-BIOS receive error: " +
                        $"{ex.GetType().Name}: {ex.Message}");
                }
            }
        }

        // --------------------------------------------------------------------
        // Parser callbacks
        // --------------------------------------------------------------------

        private void OnParserFrameSync()
        {
            ReceivedFrames++;

            if (DebugFrames)
            {
                Log(
                    $"DCS-BIOS FRAME SYNC " +
                    $"#{ReceivedFrames}");
            }

            FrameSync?.Invoke();
        }

        private void OnParserWrite(
            ushort address,
            ushort data)
        {
            ReceivedWrites++;

            // ------------------------------------------------------------
            // This is the important diagnostic output.
            //
            // For your example:
            //
            //     address = 0x7560
            //     data    = 42669
            //
            // We should see:
            //
            //     DCS-BIOS WRITE: 0x7560 = 42669
            // ------------------------------------------------------------

            if (DebugWrites)
            {
                if (!DebugAddress.HasValue ||
                    DebugAddress.Value == address)
                {
                    Log(
                        $"DCS-BIOS WRITE: " +
                        $"0x{address:X4} = {data} " +
                        $"(0x{data:X4})");
                }
            }

            DcsBiosWrite?.Invoke(address, data);
        }

        // --------------------------------------------------------------------
        // Logging
        // --------------------------------------------------------------------

        private void Log(string message)
        {
            string text =
                $"[{DateTime.Now:HH:mm:ss.fff}] {message}";

            try
            {
                LogMessage?.Invoke(text);
            }
            catch
            {
                // Logging must never stop DCS-BIOS reception.
            }

            // Also write to console so we have diagnostics even if
            // the WinForms UI has not connected a logging handler yet.
            Console.WriteLine(text);
        }

        // --------------------------------------------------------------------
        // Dispose
        // --------------------------------------------------------------------

        public void Dispose()
        {
            Stop();

            try
            {
                _cts?.Dispose();
            }
            catch
            {
                // Ignore.
            }

            _cts = null;
        }
    }


    // ========================================================================
    // DCS-BIOS PROTOCOL PARSER
    // ========================================================================

    /// <summary>
    /// Byte-by-byte DCS-BIOS export protocol parser.
    ///
    /// This follows the architecture of the DCS-Skunkworks Arduino library:
    ///
    ///     UDP data
    ///        |
    ///        v
    ///     ProcessChar()
    ///        |
    ///        +-- frame synchronization
    ///        |
    ///        +-- address
    ///        +-- length
    ///        +-- data
    ///        |
    ///        v
    ///     Write(address, data)
    ///
    /// DCS-BIOS integer data is little-endian.
    /// </summary>
    internal sealed class DcsBiosProtocolParser
    {
        // --------------------------------------------------------------------
        // Parser state
        // --------------------------------------------------------------------

        private enum ParserState
        {
            AddressLow,
            AddressHigh,
            LengthLow,
            LengthHigh,
            Data
        }

        private ParserState _state =
            ParserState.AddressLow;

        // --------------------------------------------------------------------
        // Current write
        // --------------------------------------------------------------------

        private ushort _address;
        private ushort _length;

        private ushort _dataOffset;

        // Current 16-bit word being assembled.
        private ushort _currentWord;

        private bool _dataLowByteReceived;

        // --------------------------------------------------------------------
        // Frame synchronization
        // --------------------------------------------------------------------

        private int _syncCount;

        // --------------------------------------------------------------------
        // Events
        // --------------------------------------------------------------------

        public event Action<ushort, ushort>? Write;

        public event Action? FrameSync;

        public event Action<string>? Log;

        // --------------------------------------------------------------------
        // Process one byte
        // --------------------------------------------------------------------

        public void ProcessChar(byte c)
        {
            // ------------------------------------------------------------
            // DCS-BIOS guarantees that 55 55 55 55 cannot occur as normal
            // data. Therefore we can detect frame synchronization while
            // parsing the stream.
            // ------------------------------------------------------------

            if (c == 0x55)
            {
                _syncCount++;

                if (_syncCount == 4)
                {
                    // ----------------------------------------------------
                    // Four consecutive 0x55 bytes = frame synchronization.
                    // ----------------------------------------------------

                    _syncCount = 0;

                    ResetWriteParser();

                    FrameSync?.Invoke();

                    return;
                }
            }
            else
            {
                _syncCount = 0;
            }

            // ------------------------------------------------------------
            // Normal DCS-BIOS write parser.
            // ------------------------------------------------------------

            switch (_state)
            {
                case ParserState.AddressLow:

                    _address = c;

                    _state =
                        ParserState.AddressHigh;

                    break;


                case ParserState.AddressHigh:

                    _address |=
                        (ushort)(c << 8);

                    _state =
                        ParserState.LengthLow;

                    break;


                case ParserState.LengthLow:

                    _length = c;

                    _state =
                        ParserState.LengthHigh;

                    break;


                case ParserState.LengthHigh:

                    _length |=
                        (ushort)(c << 8);

                    _dataOffset = 0;

                    _currentWord = 0;

                    _dataLowByteReceived = false;

                    // ----------------------------------------------------
                    // A zero-length write should not occur for normal
                    // DCS-BIOS data, but handle it safely.
                    // ----------------------------------------------------

                    if (_length == 0)
                    {
                        ResetWriteParser();
                    }
                    else
                    {
                        _state =
                            ParserState.Data;
                    }

                    break;


                case ParserState.Data:

                    ProcessDataByte(c);

                    break;
            }
        }

        // --------------------------------------------------------------------
        // Process data byte
        // --------------------------------------------------------------------

        private void ProcessDataByte(byte c)
        {
            if (!_dataLowByteReceived)
            {
                // Little endian:
                //
                // first byte = low byte
                //
                _currentWord = c;

                _dataLowByteReceived = true;
            }
            else
            {
                // Second byte = high byte.
                //
                // Example:
                //
                //     CD A6
                //
                // becomes:
                //
                //     0xA6CD = 42669
                //

                _currentWord |=
                    (ushort)(c << 8);

                ushort wordAddress =
                    (ushort)(_address + _dataOffset);

                // --------------------------------------------------------
                // DCS-BIOS integer writes are 16-bit aligned.
                //
                // Notify the application with:
                //
                //     address
                //     decoded 16-bit value
                // --------------------------------------------------------

                Write?.Invoke(
                    wordAddress,
                    _currentWord);

                _dataOffset += 2;

                _dataLowByteReceived = false;

                _currentWord = 0;

                // --------------------------------------------------------
                // Have we received the complete write?
                // --------------------------------------------------------

                if (_dataOffset >= _length)
                {
                    ResetWriteParser();
                }
            }
        }

        // --------------------------------------------------------------------
        // Reset parser
        // --------------------------------------------------------------------

        private void ResetWriteParser()
        {
            _state =
                ParserState.AddressLow;

            _address = 0;
            _length = 0;

            _dataOffset = 0;

            _currentWord = 0;

            _dataLowByteReceived = false;
        }
    }
}
