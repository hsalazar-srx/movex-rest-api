// MvxSockWrapper.cs - Pure .NET socket-based implementation inspired by MvxLib
// No P/Invoke needed - communicates directly via TCP sockets
using System;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace Movex.API
{
    public class MvxSockWrapper : IDisposable
    {
        private static readonly Encoding _encoding = Encoding.GetEncoding("ISO-8859-1");
        private Socket? _socket;
        private bool _loggedIn = false;
        private bool _disposed = false;
        private const int RECEIVE_TIMEOUT = 30000; // 30 seconds
        private const int SEND_TIMEOUT = 2000; // 2 seconds

        public MvxSockWrapper()
        {
            Console.WriteLine("[MvxSock] Using pure .NET socket implementation (no DLL required)");
        }

        private async Task ConnectAsync(string host, int port)
        {
            Console.WriteLine($"[MvxSock] Connecting to {host}:{port}...");

            _socket = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);

            // Resolve IP addresses
            IPAddress[] ipAddresses = await Dns.GetHostAddressesAsync(host);
            Console.WriteLine($"[MvxSock] Found {ipAddresses.Length} IP address(es)");

            foreach (var ipAddress in ipAddresses)
            {
                try
                {
                    Console.WriteLine($"[MvxSock] Trying {ipAddress}...");
                    await _socket.ConnectAsync(new IPEndPoint(ipAddress, port));
                    if (_socket.Connected)
                    {
                        Console.WriteLine("[MvxSock] ✅ Socket connected successfully");
                        return;
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"[MvxSock] Failed to connect to {ipAddress}: {ex.Message}");
                }
            }

            throw new Exception($"Could not connect to {host}:{port}");
        }

        private async Task LoginAsync(string username, string password, string library, string program)
        {
            Console.WriteLine($"[MvxSock] Logging in as {username} to {library}/{program}...");

            string command = "LOGON" +
                Environment.MachineName.PadRight(32) +
                username.PadRight(16) +
                password.PadRight(16) +
                (library + "/" + program).PadRight(32) +
                program.PadRight(32);

            string response = await ExecuteAsync(command, false);

            if (response != "Connection accepted")
            {
                throw new Exception($"Login unsuccessful: {response}");
            }

            _loggedIn = true;
            Console.WriteLine("[MvxSock] ✅ Login successful");
        }

        private async Task<string> ExecuteAsync(string command, bool checkLogin = true)
        {
            if (_socket == null || !_socket.Connected)
            {
                throw new Exception("Socket is not connected");
            }

            if (checkLogin && !_loggedIn)
            {
                throw new Exception("Cannot run command because not logged in");
            }

            if (command != null)
            {
                byte[] send = AddCommandLengthPrefix(command);
                Console.WriteLine($"[MvxSock] Sending command (length={command.Length})");

                using var sendCts = new CancellationTokenSource(SEND_TIMEOUT);
                await _socket.SendAsync(send, SocketFlags.None, sendCts.Token);
            }

            int countReceived = 0, countTotalReceived = 0;
            int countExpected = 0, iteration = 0;
            byte[] buffer = new byte[4];
            string received = string.Empty;

            using var receiveCts = new CancellationTokenSource(RECEIVE_TIMEOUT);

            do
            {
                countReceived = await _socket.ReceiveAsync(buffer, SocketFlags.None, receiveCts.Token);
                if (countReceived == 0)
                {
                    throw new Exception("Socket closed by remote host");
                }

                received += _encoding.GetString(buffer, 0, countReceived);
                countTotalReceived += countReceived;

                if (iteration == 0 && countReceived >= 4)
                {
                    countExpected = buffer[2] * 256 + buffer[3] + 4;
                }

                if (countExpected == 0)
                    return string.Empty;

                iteration++;
                buffer = new byte[countExpected - 4];
            }
            while (countExpected > countTotalReceived);

            return received.Substring(4);
        }

        private byte[] AddCommandLengthPrefix(string command)
        {
            byte[] ret = _encoding.GetBytes("\x00\x00\x01\x01" + command);
            ret[2] = Convert.ToByte(command.Length / 256);
            ret[3] = Convert.ToByte(command.Length % 256);
            return ret;
        }

        public async Task<string> SendReceiveAsync(string host, int port, string username, string password, string request, string apiprogram)
        {
            try
            {
                // Connect if not already connected
                if (_socket == null || !_socket.Connected)
                {
                    await ConnectAsync(host, port);
                }

                // Login if not already logged in
                if (!_loggedIn)
                {
                    // Extract library from apiprogram if it contains a slash, otherwise use default
                    string library = "MVXCOBJ";
                    string program = apiprogram;
                    
                    if (apiprogram.Contains("/"))
                    {
                        var parts = apiprogram.Split('/');
                        library = parts[0];
                        program = parts[1];
                    }

                    await LoginAsync(username, password, library, program);
                }

                // Execute the transaction
                Console.WriteLine($"[MvxSock] Executing transaction: {request.Substring(0, Math.Min(50, request.Length))}...");
                string response = await ExecuteAsync(request, true);
                Console.WriteLine($"[MvxSock] ✅ Transaction completed, response length: {response?.Length ?? 0}");
                return response ?? string.Empty;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[MvxSock] ❌ Error: {ex.Message}\n{ex}");
                // Close connection on error
                await CloseAsync();
                throw;
            }
        }

        public string SendReceive(string host, int port, string username, string password, string request, string apiprogram)
        {
            return SendReceiveAsync(host, port, username, password, request, apiprogram).GetAwaiter().GetResult();
        }

        public async Task CloseAsync()
        {
            Console.WriteLine("[MvxSock] Closing connection...");

            if (_socket != null && _socket.Connected)
            {
                if (_loggedIn)
                {
                    try
                    {
                        await ExecuteAsync("CLOSE", true);
                    }
                    catch { }
                }

                _socket.Close();
            }

            _loggedIn = false;
            _socket = null;
        }

        public void Close()
        {
            CloseAsync().GetAwaiter().GetResult();
        }

        public void Dispose()
        {
            if (!_disposed)
            {
                Close();
                _disposed = true;
            }
        }
    }
}
