using Movex.API.Services.Validation;

namespace Movex.API.Tests.Validation;

public class PoStockTransactionRulesTests
{
    private static (PoStockTransactionQuery? Query, string? Error) Run(
        string? cono = "100", string? puno = "5306552", string? itno = null,
        string? from = null, string? to = null, int? limit = null) =>
        PoStockTransactionRules.Validate(cono, "300", puno, itno, from, to, limit);

    [Fact]
    public void Valid_input_is_normalised()
    {
        var (q, error) = Run(puno: " d11345 ", itno: " lfcm410063 ", from: "20260101", to: "20261124", limit: 50);

        error.Should().BeNull();
        q!.Puno.Should().Be(" D11345");        // upper-case, right-justified to 7 as MITTRA stores it
        q.Itno.Should().Be("LFCM410063");
        q.FromDate.Should().Be(20260101m);
        q.ToDate.Should().Be(20261124m);
        q.Limit.Should().Be(50);
    }

    [Fact]
    public void Seven_digit_po_is_not_padded() => Run().Query!.Puno.Should().Be("5306552");

    [Fact]
    public void Missing_cono_uses_the_default() =>
        PoStockTransactionRules.Validate(null, "300", "5306552", null, null, null, null).Query!.Cono.Should().Be("300");

    [Theory]
    [InlineData("999")]
    [InlineData("100 OR 1=1")]
    [InlineData("abc")]
    public void Unknown_company_is_refused(string cono) => Run(cono: cono).Error.Should().NotBeNull();

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("12345678")]                 // 8 characters
    [InlineData("53065'; DROP TABLE X;--")]
    [InlineData("53 06")]
    public void Bad_po_is_refused(string? puno) => Run(puno: puno).Error.Should().NotBeNull();

    [Theory]
    [InlineData("LFCM410063' OR '1'='1")]
    [InlineData("A B")]
    [InlineData("1234567890123456")]        // 16 characters
    public void Bad_item_is_refused(string itno) => Run(itno: itno).Error.Should().NotBeNull();

    [Theory]
    [InlineData("2026-01-01")]
    [InlineData("20261301")]                // month 13
    [InlineData("20260230")]                // 30 February
    [InlineData("2026011")]
    public void Bad_dates_are_refused(string date)
    {
        Run(from: date).Error.Should().NotBeNull();
        Run(to: date).Error.Should().NotBeNull();
    }

    [Fact]
    public void From_after_to_is_refused() => Run(from: "20261124", to: "20260101").Error.Should().NotBeNull();

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(501)]
    public void Limit_out_of_range_is_refused(int limit) => Run(limit: limit).Error.Should().NotBeNull();

    [Fact]
    public void Default_limit_is_applied() => Run().Query!.Limit.Should().Be(PoStockTransactionRules.DefaultLimit);
}
