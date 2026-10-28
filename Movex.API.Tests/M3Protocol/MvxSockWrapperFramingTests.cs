using System.Net;
using System.Net.Sockets;
using System.Text;
using Movex.API;

namespace Movex.API.Tests.M3Protocol;

/// <summary>
/// Message framing of MvxSockWrapper against a loopback server standing in for M3.
/// Each message is a 4-byte header (00 00 len_hi len_lo) followed by <c>len</c> bytes.
///
/// Regression guard: a body that arrived in more than one read used to be read with a full-size
/// buffer each time, so the second read swallowed the start of the next message when M3 had already
/// sent it (list responses). Header bytes arriving split across reads is a separate, unhandled case
/// and is deliberately not exercised here.
/// </summary>
public class MvxSockWrapperFramingTests
{
    private static readonly Encoding Latin1 = Encoding.GetEncoding("ISO-8859-1");
    private const string Command = "LstByNumber    300";
    private const string ApiProgram = "MVXCOBJ/CRS610MI";

    [Fact]
    public async Task Message_in_one_write_is_returned_intact()
    {
        var payload = Payload('A', 400);

        await using var server = new FakeM3Server(async stream =>
            await stream.WriteAsync(Frame(payload)));
        using var wrapper = new MvxSockWrapper();

        var first = await wrapper.SendReceiveAsync("127.0.0.1", server.Port, "u", "p", Command, ApiProgram)
            .WaitAsync(TimeSpan.FromSeconds(10));

        first.Should().Be(payload);
    }

    [Fact]
    public async Task Body_arriving_in_two_reads_is_returned_intact()
    {
        var payload = Payload('A', 400);
        var frame = Frame(payload);

        await using var server = new FakeM3Server(async stream =>
        {
            await stream.WriteAsync(frame.AsMemory(0, 4 + 150));
            await Task.Delay(50);
            await stream.WriteAsync(frame.AsMemory(4 + 150));
        });
        using var wrapper = new MvxSockWrapper();

        var first = await wrapper.SendReceiveAsync("127.0.0.1", server.Port, "u", "p", Command, ApiProgram)
            .WaitAsync(TimeSpan.FromSeconds(10));

        first.Should().Be(payload);
    }

    [Fact]
    public async Task Split_body_does_not_swallow_the_next_message_already_sent()
    {
        var a = Payload('A', 400);
        var b = Payload('B', 400);
        var frameA = Frame(a);
        var frameB = Frame(b);

        await using var server = new FakeM3Server(async stream =>
        {
            await stream.WriteAsync(frameA.AsMemory(0, 4 + 150));
            await Task.Delay(50);
            // rest of A and all of B arrive together
            var tail = frameA.AsSpan(4 + 150).ToArray().Concat(frameB).ToArray();
            await stream.WriteAsync(tail);
        });
        using var wrapper = new MvxSockWrapper();

        var first = await wrapper.SendReceiveAsync("127.0.0.1", server.Port, "u", "p", Command, ApiProgram)
            .WaitAsync(TimeSpan.FromSeconds(10));
        first.Should().Be(a);

        var second = await wrapper.SendReceiveNextAsync(Command)
            .WaitAsync(TimeSpan.FromSeconds(10));
        second.Should().Be(b);
    }

    [Fact]
    public async Task List_of_fixed_size_records_with_split_bodies_is_returned_record_by_record()
    {
        // Mirrors CRS610MI/LstByNumber: 857-byte records sent back-to-back as one stream. The stream is
        // written in chunks that end inside a record body, with a pause after each, so the client reads a
        // partial body and the next chunk then carries the rest of it plus the following records.
        const int records = 120;
        var frames = Enumerable.Range(0, records).Select(i => Frame(Record(i))).ToList();
        var stream = frames.SelectMany(f => f).ToArray();

        var random = new Random(42);
        var cuts = new List<int>();
        var frameStart = 0;
        foreach (var frame in frames)
        {
            if (random.Next(3) == 0)
                cuts.Add(frameStart + random.Next(4 + 1, frame.Length)); // inside the body, never in a header
            frameStart += frame.Length;
        }
        cuts.Add(stream.Length);

        await using var server = new FakeM3Server(async socket =>
        {
            var from = 0;
            foreach (var cut in cuts)
            {
                await socket.WriteAsync(stream.AsMemory(from, cut - from));
                from = cut;
                await Task.Delay(5);
            }
        });
        using var wrapper = new MvxSockWrapper();

        var received = new List<string>
        {
            await wrapper.SendReceiveAsync("127.0.0.1", server.Port, "u", "p", Command, ApiProgram)
                .WaitAsync(TimeSpan.FromSeconds(10))
        };
        for (var i = 1; i < records; i++)
            received.Add(await wrapper.SendReceiveNextAsync(Command).WaitAsync(TimeSpan.FromSeconds(10)));

        received.Should().Equal(Enumerable.Range(0, records).Select(Record));
    }

    private static string Payload(char fill, int length) => new(fill, length);

    // 857 chars: 15-char prefix + record number + a filler that differs per record, so any bleed-over shows.
    private static string Record(int i) =>
        ("REP".PadRight(15) + $"R{i:D4}").PadRight(857, (char)('A' + i % 26));

    private static byte[] Frame(string payload)
    {
        var body = Latin1.GetBytes(payload);
        var frame = new byte[4 + body.Length];
        frame[2] = (byte)(body.Length / 256);
        frame[3] = (byte)(body.Length % 256);
        body.CopyTo(frame, 4);
        return frame;
    }

    /// <summary>
    /// Accepts one client, answers the LOGON frame, reads the first transaction command, then hands
    /// the stream to <paramref name="script"/> to write whatever M3 would send. Later client commands are drained.
    /// </summary>
    private sealed class FakeM3Server : IAsyncDisposable
    {
        private readonly TcpListener _listener = new(IPAddress.Loopback, 0);
        private readonly Task _serving;

        public int Port => ((IPEndPoint)_listener.LocalEndpoint).Port;

        public FakeM3Server(Func<NetworkStream, Task> script)
        {
            _listener.Start();
            _serving = ServeAsync(script);
        }

        private async Task ServeAsync(Func<NetworkStream, Task> script)
        {
            using var client = await _listener.AcceptTcpClientAsync();
            client.NoDelay = true;
            var stream = client.GetStream();

            await ReadFrameAsync(stream);                                  // LOGON
            await stream.WriteAsync(Frame("Connection accepted"));
            await ReadFrameAsync(stream);                                  // first transaction command
            await script(stream);

            try
            {
                while (true)                                               // drain; answer only CLOSE, as M3 does
                {
                    if (await ReadFrameAsync(stream) == "CLOSE")
                        await stream.WriteAsync(Frame("OK"));
                }
            }
            catch (EndOfStreamException) { }
            catch (IOException) { }
        }

        private static async Task<string> ReadFrameAsync(NetworkStream stream)
        {
            var header = new byte[4];
            await stream.ReadExactlyAsync(header);
            var body = new byte[header[2] * 256 + header[3]];
            await stream.ReadExactlyAsync(body);
            return Latin1.GetString(body);
        }

        public async ValueTask DisposeAsync()
        {
            _listener.Stop();
            try { await _serving.WaitAsync(TimeSpan.FromSeconds(5)); }
            catch { /* the client side's assertions report the real failure */ }
        }
    }
}
