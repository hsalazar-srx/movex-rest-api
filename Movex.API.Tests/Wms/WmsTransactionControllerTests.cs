using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Movex.API.Controllers;
using Movex.API.Core.Interfaces;
using Movex.API.Core.Models;
using Movex.API.Middleware;

namespace Movex.API.Tests.Wms;

public class WmsTransactionControllerTests
{
    private readonly Mock<ITransactionService> _service = new();
    private readonly Mock<ITransactionConfigurationService> _configService = new();
    private readonly Mock<ILogger<WmsTransactionController>> _logger = new();

    private WmsTransactionController BuildController(
        string? tier = "Wms",
        string[]? allowed = null)
    {
        var configData = new Dictionary<string, string?>();
        if (allowed != null)
            for (int i = 0; i < allowed.Length; i++)
                configData[$"ApiKeys:WmsAllowedTransactions:{i}"] = allowed[i];

        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(configData)
            .Build();

        var ctrl = new WmsTransactionController(_service.Object, _configService.Object, _logger.Object, config);

        var httpCtx = new DefaultHttpContext();
        if (tier != null)
            httpCtx.Items[ApiKeyAuthenticationMiddleware.ApiKeyTierItem] = tier;

        ctrl.ControllerContext = new ControllerContext { HttpContext = httpCtx };
        return ctrl;
    }

    private void SetupTransaction(string program, string tx, bool returnsList = false)
    {
        _configService.Setup(s => s.GetTransaction(program, tx))
            .Returns(new TransactionDefinition { Name = tx, ReturnsList = returnsList });
    }

    // ── Allowlist enforcement ─────────────────────────────────────────────────

    [Fact]
    public async Task Wms_key_denied_when_transaction_not_in_allowlist()
    {
        var ctrl = BuildController(tier: "Wms", allowed: ["MMS200MI/GetItmWhsBal"]);
        SetupTransaction("PMS100MI", "SelMaterials");

        var result = await ctrl.ExecuteTransaction("PMS100MI", "SelMaterials",
            new Dictionary<string, object>()) as ObjectResult;

        result!.StatusCode.Should().Be(403);
    }

    [Fact]
    public async Task Wms_key_allowed_when_transaction_in_allowlist()
    {
        _service.Setup(s => s.ExecuteTransactionAsync(
            It.IsAny<string>(), It.IsAny<string>(),
            It.IsAny<Dictionary<string, string>>(),
            It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<bool>()))
            .ReturnsAsync(new List<Dictionary<string, object?>>());

        var ctrl = BuildController(tier: "Wms", allowed: ["PMS100MI/SelMaterials"]);
        SetupTransaction("PMS100MI", "SelMaterials", returnsList: true);

        var result = await ctrl.ExecuteTransaction("PMS100MI", "SelMaterials",
            new Dictionary<string, object>()) as OkObjectResult;

        result.Should().NotBeNull();
        result!.StatusCode.Should().Be(200);
    }

    [Fact]
    public async Task Primary_key_bypasses_allowlist_check()
    {
        _service.Setup(s => s.ExecuteTransactionAsync(
            It.IsAny<string>(), It.IsAny<string>(),
            It.IsAny<Dictionary<string, string>>(),
            It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<bool>()))
            .ReturnsAsync(new Dictionary<string, object?>());

        // No allowed list configured, but tier is Primary — should pass through
        var ctrl = BuildController(tier: "Primary", allowed: null);
        SetupTransaction("PMS100MI", "SelMaterials");

        var result = await ctrl.ExecuteTransaction("PMS100MI", "SelMaterials",
            new Dictionary<string, object>()) as OkObjectResult;

        result.Should().NotBeNull();
    }

    [Fact]
    public async Task Admin_key_bypasses_allowlist_check()
    {
        _service.Setup(s => s.ExecuteTransactionAsync(
            It.IsAny<string>(), It.IsAny<string>(),
            It.IsAny<Dictionary<string, string>>(),
            It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<bool>()))
            .ReturnsAsync(new Dictionary<string, object?>());

        var ctrl = BuildController(tier: "Admin", allowed: null);
        SetupTransaction("MMS200MI", "GetItmWhsBal");

        var result = await ctrl.ExecuteTransaction("MMS200MI", "GetItmWhsBal",
            new Dictionary<string, object>()) as OkObjectResult;

        result.Should().NotBeNull();
    }

    [Fact]
    public async Task Wms_key_denied_when_allowlist_is_empty()
    {
        var ctrl = BuildController(tier: "Wms", allowed: []);
        SetupTransaction("PMS100MI", "SelMaterials");

        var result = await ctrl.ExecuteTransaction("PMS100MI", "SelMaterials",
            new Dictionary<string, object>()) as ObjectResult;

        result!.StatusCode.Should().Be(403);
    }

    [Fact]
    public async Task Allowlist_matching_is_case_insensitive()
    {
        _service.Setup(s => s.ExecuteTransactionAsync(
            It.IsAny<string>(), It.IsAny<string>(),
            It.IsAny<Dictionary<string, string>>(),
            It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<bool>()))
            .ReturnsAsync(new Dictionary<string, object?>());

        var ctrl = BuildController(tier: "Wms", allowed: ["pms100mi/selmaterials"]);
        SetupTransaction("PMS100MI", "SelMaterials");

        var result = await ctrl.ExecuteTransaction("PMS100MI", "SelMaterials",
            new Dictionary<string, object>()) as OkObjectResult;

        result.Should().NotBeNull();
    }

    // ── Transaction routing ───────────────────────────────────────────────────

    [Fact]
    public async Task Unknown_transaction_returns_404()
    {
        _configService.Setup(s => s.GetTransaction("PMS100MI", "NoSuchTx")).Returns((TransactionDefinition?)null);
        var ctrl = BuildController(tier: "Primary");

        var result = await ctrl.ExecuteTransaction("PMS100MI", "NoSuchTx",
            new Dictionary<string, object>()) as NotFoundObjectResult;

        result.Should().NotBeNull();
    }

    [Fact]
    public async Task GET_only_transaction_via_POST_returns_400()
    {
        _configService.Setup(s => s.GetTransaction("MMS200MI", "GetItmBasic"))
            .Returns(new TransactionDefinition { Name = "GetItmBasic", HttpMethod = "GET" });

        var ctrl = BuildController(tier: "Primary", allowed: ["MMS200MI/GetItmBasic"]);

        var result = await ctrl.ExecuteTransaction("MMS200MI", "GetItmBasic",
            new Dictionary<string, object>()) as BadRequestObjectResult;

        result.Should().NotBeNull();
    }

    [Fact]
    public async Task NOK_response_returns_422()
    {
        _service.Setup(s => s.ExecuteTransactionAsync(
            It.IsAny<string>(), It.IsAny<string>(),
            It.IsAny<Dictionary<string, string>>(),
            It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<bool>()))
            .ReturnsAsync("NOK XRE0001 Some error");

        var ctrl = BuildController(tier: "Primary");
        SetupTransaction("MMS200MI", "GetItmWhsBal");

        var result = await ctrl.ExecuteTransaction("MMS200MI", "GetItmWhsBal",
            new Dictionary<string, object>()) as ObjectResult;

        result!.StatusCode.Should().Be(422);
    }

    [Fact]
    public async Task List_response_is_wrapped_with_records_and_count()
    {
        var records = new List<Dictionary<string, object?>>
        {
            new() { ["MSEQ"] = "100" },
            new() { ["MSEQ"] = "120" }
        };

        _service.Setup(s => s.ExecuteTransactionAsync(
            It.IsAny<string>(), It.IsAny<string>(),
            It.IsAny<Dictionary<string, string>>(),
            It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<bool>()))
            .ReturnsAsync(records);

        var ctrl = BuildController(tier: "Primary");
        SetupTransaction("PMS100MI", "SelMaterials", returnsList: true);

        var result = await ctrl.ExecuteTransaction("PMS100MI", "SelMaterials",
            new Dictionary<string, object>()) as OkObjectResult;

        result.Should().NotBeNull();
        var data = result!.Value;
        var dataType = data!.GetType();
        var countProp = dataType.GetProperty("data")?.GetValue(data);
        // count property is on the inner data object
        countProp.Should().NotBeNull();
    }
}
