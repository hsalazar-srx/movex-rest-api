using Movex.API.Infrastructure.Configuration;
using Movex.API.Infrastructure.TransactionBuilding;
using Movex.API.Services.Validation;

namespace Movex.API.Tests.Configuration;

/// <summary>
/// Guards transactions/PPS001MI.json against the offset drift that broke UpdateComponent and
/// GetComponent: every field must land at its MRS003 position (verified 2026-09-22).
/// </summary>
public class Pps001MiReceiptLayoutTests
{
    private static string TransactionsDir()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && !File.Exists(Path.Combine(dir.FullName, "transactions", "PPS001MI.json")))
            dir = dir.Parent;
        return Path.Combine(dir!.FullName, "transactions");
    }

    private static string BuildReceipt(Dictionary<string, string> values)
    {
        using var config = new TransactionConfigurationService(TransactionsDir());
        var tx = config.GetTransaction("PPS001MI", "Receipt");
        tx.Should().NotBeNull();
        return new TransactionStringBuilder(new FieldValidator()).BuildTransactionString(tx!, values);
    }

    // 1-based MRS003 start position → 0-based index in the full MI command string
    private static string At(string command, int start, int length) => command.Substring(start - 1, length);

    [Fact]
    public void Receipt_fields_land_at_their_MRS003_positions()
    {
        var command = BuildReceipt(new()
        {
            ["CONO"] = "300", ["TRDT"] = "20261029", ["RESP"] = "HSALAZAR",
            ["PUNO"] = "4123456", ["PNLI"] = "1", ["PNLS"] = "0", ["RVQA"] = "100",
            ["WHLO"] = "M10", ["NOPK"] = "2"
        });

        command.Should().HaveLength(147);
        At(command, 1, 15).Should().Be("Receipt        ");
        At(command, 16, 3).Should().Be("300");
        At(command, 19, 8).Should().Be("20261029");
        At(command, 27, 12).Should().Be("HSALAZAR    ");
        At(command, 39, 7).Should().Be("4123456");
        At(command, 46, 3).Should().Be("001");
        At(command, 49, 3).Should().Be("000");
        At(command, 52, 11).Should().Be("100        ");
        At(command, 63, 1).Should().Be("0");
        At(command, 100, 3).Should().Be("M10");
        At(command, 143, 5).Should().Be("00002");
    }

    // Live-tested 2026-10-29: "D11345" left-aligned → "Purchase order line 1 does not exist"; " D11345" → OK.
    [Theory]
    [InlineData("D11345")]
    [InlineData(" D11345")]
    [InlineData("D11345 ")]
    public void Receipt_PUNO_is_right_aligned_however_the_caller_sends_it(string puno)
    {
        var command = BuildReceipt(new()
        {
            ["CONO"] = "300", ["TRDT"] = "20261029", ["RESP"] = "HSALAZAR",
            ["PUNO"] = puno, ["PNLI"] = "1", ["PNLS"] = "0", ["RVQA"] = "1"
        });

        At(command, 39, 7).Should().Be(" D11345");
    }
}
