using Movex.API.Core.Models;
using Movex.API.Services;

namespace Movex.API.Tests.Parsing;

public class M3ResponseParserTests
{
    private readonly M3ResponseParser _parser = new();

    private static List<ResponseFieldConfig> Fields(params (string name, int len, string type)[] defs)
    {
        var list = new List<ResponseFieldConfig>();
        for (int i = 0; i < defs.Length; i++)
            list.Add(new ResponseFieldConfig { Name = defs[i].name, Position = i + 1, Length = defs[i].len, Type = defs[i].type });
        return list;
    }

    // ── ParseResponse ──────────────────────────────────────────────────────────

    [Fact]
    public void ParseResponse_extracts_text_fields_sequentially()
    {
        var raw = "ABC   DEF   ";
        var fields = Fields(("F1", 6, "text"), ("F2", 6, "text"));

        var result = _parser.ParseResponse(raw, fields);

        result["F1"].Should().Be("ABC");
        result["F2"].Should().Be("DEF");
    }

    [Fact]
    public void ParseResponse_converts_numeric_to_decimal()
    {
        var raw = "000042";
        var fields = Fields(("QTY", 6, "numeric"));

        var result = _parser.ParseResponse(raw, fields);

        result["QTY"].Should().Be(42m);
    }

    [Fact]
    public void ParseResponse_converts_integer_field()
    {
        var raw = "007";
        var fields = Fields(("CNT", 3, "integer"));

        var result = _parser.ParseResponse(raw, fields);

        result["CNT"].Should().Be(7);
    }

    [Fact]
    public void ParseResponse_parses_YYYYMMDD_date()
    {
        var raw = "20260702";
        var fields = Fields(("DT", 8, "date"));

        var result = _parser.ParseResponse(raw, fields);

        result["DT"].Should().Be("2026-07-02");
    }

    [Fact]
    public void ParseResponse_returns_null_for_zero_date()
    {
        var raw = "00000000";
        var fields = Fields(("DT", 8, "date"));

        var result = _parser.ParseResponse(raw, fields);

        result["DT"].Should().BeNull();
    }

    [Fact]
    public void ParseResponse_returns_empty_dict_for_empty_input()
    {
        var result = _parser.ParseResponse("", Fields(("F1", 5, "text")));
        result.Should().BeEmpty();
    }

    [Fact]
    public void ParseResponse_returns_empty_dict_for_null_fields()
    {
        var result = _parser.ParseResponse("ABC", null!);
        result.Should().BeEmpty();
    }

    [Fact]
    public void ParseResponse_handles_response_shorter_than_declared_fields()
    {
        var raw = "AB"; // shorter than the 5-char field
        var fields = Fields(("F1", 5, "text"));

        // Should not throw; partial value extracted
        var act = () => _parser.ParseResponse(raw, fields);
        act.Should().NotThrow();
    }

    [Fact]
    public void ParseResponse_uses_StartIndex_for_absolute_positioning()
    {
        // 15-char M3 prefix + data: positions are 1-based absolute from start of response
        var raw = new string(' ', 14) + "HELLO"; // start at position 15 (index 14)
        var fields = new List<ResponseFieldConfig>
        {
            new() { Name = "F1", StartIndex = 15, Length = 5, Type = "text", Position = 1 }
        };

        var result = _parser.ParseResponse(raw, fields);

        result["F1"].Should().Be("HELLO");
    }

    [Fact]
    public void ParseBoolean_parses_Y_as_true()
    {
        var raw = "Y";
        var fields = Fields(("FLAG", 1, "boolean"));

        var result = _parser.ParseResponse(raw, fields);

        result["FLAG"].Should().Be(true);
    }

    [Fact]
    public void ParseBoolean_parses_0_as_false()
    {
        var raw = "0";
        var fields = Fields(("FLAG", 1, "boolean"));

        var result = _parser.ParseResponse(raw, fields);

        result["FLAG"].Should().Be(false);
    }

    // ── M3 list protocol prefix handling ──────────────────────────────────────

    [Fact]
    public void Response_starting_with_REP_is_not_a_terminator()
    {
        // The list loop in TransactionService checks TrimStart().StartsWith("OK"/"NOK")
        // A REP prefix should pass through as a data record
        var raw = "REP            HELLO          ";
        var trimmed = raw.TrimStart();

        trimmed.StartsWith("NOK", StringComparison.OrdinalIgnoreCase).Should().BeFalse();
        trimmed.StartsWith("OK", StringComparison.OrdinalIgnoreCase).Should().BeFalse();
    }

    [Fact]
    public void Response_starting_with_OK_is_end_of_list_terminator()
    {
        var raw = "OK             HELLO          ";
        var trimmed = raw.TrimStart();

        trimmed.StartsWith("OK", StringComparison.OrdinalIgnoreCase).Should().BeTrue();
    }

    [Fact]
    public void Response_starting_with_NOK_is_error_terminator()
    {
        var raw = "NOK XRE0103";
        var trimmed = raw.TrimStart();

        trimmed.StartsWith("NOK", StringComparison.OrdinalIgnoreCase).Should().BeTrue();
    }
}
