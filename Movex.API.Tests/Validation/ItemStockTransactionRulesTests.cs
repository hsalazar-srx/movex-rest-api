using Movex.API.Services.Validation;

namespace Movex.API.Tests.Validation;

public class ItemStockTransactionRulesTests
{
    private static (ItemStockTransactionQuery? Query, string? Error) Run(
        string? cono = "100", string? itno = "LFCM410063", string? from = "20260101",
        string? to = null, string? puno = null, int? ttyp = null, int? limit = null) =>
        ItemStockTransactionRules.Validate(cono, itno, from, to, puno, ttyp, limit);

    [Fact]
    public void Valid_input_is_normalised()
    {
        var (q, error) = Run(itno: " lfcm410063 ", to: "20261124", puno: " d11345 ", ttyp: 25, limit: 50);

        error.Should().BeNull();
        q!.Itno.Should().Be("LFCM410063");
        q.FromDate.Should().Be(20260101m);
        q.ToDate.Should().Be(20261124m);
        q.Puno.Should().Be(" D11345");        // upper-case, right-justified to 7 as MITTRA stores it
        q.Ttyp.Should().Be(25);
        q.Limit.Should().Be(50);
    }

    [Fact]
    public void Optional_filters_default_to_all_movements()
    {
        var (q, error) = Run();

        error.Should().BeNull();
        q!.Puno.Should().BeNull();
        q.Ttyp.Should().BeNull();
        q.ToDate.Should().BeNull();
        q.Limit.Should().Be(ItemStockTransactionRules.DefaultLimit);
    }

    [Fact]
    public void Seven_digit_po_is_not_padded() => Run(puno: "5306552").Query!.Puno.Should().Be("5306552");

    [Theory]
    [InlineData(null)]                       // no default company
    [InlineData("")]
    [InlineData("999")]
    [InlineData("100 OR 1=1")]
    [InlineData("abc")]
    public void Missing_or_unknown_company_is_refused(string? cono) => Run(cono: cono).Error.Should().NotBeNull();

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("LFCM410063' OR '1'='1")]
    [InlineData("A B")]
    [InlineData("1234567890123456")]        // 16 characters
    public void Missing_or_bad_item_is_refused(string? itno) => Run(itno: itno).Error.Should().NotBeNull();

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    public void Missing_from_date_is_refused(string? from) => Run(from: from).Error.Should().NotBeNull();

    [Theory]
    [InlineData("12345678")]                 // 8 characters
    [InlineData("53065'; DROP TABLE X;--")]
    [InlineData("53 06")]
    public void Bad_po_is_refused(string puno) => Run(puno: puno).Error.Should().NotBeNull();

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
    [InlineData(100)]
    public void Type_out_of_range_is_refused(int ttyp) => Run(ttyp: ttyp).Error.Should().NotBeNull();

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(501)]
    public void Limit_out_of_range_is_refused(int limit) => Run(limit: limit).Error.Should().NotBeNull();
}
