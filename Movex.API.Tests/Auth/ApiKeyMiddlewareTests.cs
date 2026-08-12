using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Movex.API.Middleware;

namespace Movex.API.Tests.Auth;

public class ApiKeyMiddlewareTests
{
    private static ApiKeyAuthenticationMiddleware BuildMiddleware(
        ApiKeyOptions options,
        RequestDelegate? next = null,
        bool isDevelopment = false)
    {
        var optionsMonitor = new Mock<IOptionsMonitor<ApiKeyOptions>>();
        optionsMonitor.Setup(m => m.CurrentValue).Returns(options);

        var env = new Mock<IHostEnvironment>();
        env.Setup(e => e.EnvironmentName)
           .Returns(isDevelopment ? Environments.Development : Environments.Production);

        var logger = new Mock<ILogger<ApiKeyAuthenticationMiddleware>>();

        return new ApiKeyAuthenticationMiddleware(
            next ?? (_ => Task.CompletedTask),
            optionsMonitor.Object,
            logger.Object,
            env.Object);
    }

    private static DefaultHttpContext MakeContext(
        string? apiKey = null,
        string? adminKey = null,
        string path = "/api/test")
    {
        var ctx = new DefaultHttpContext();
        ctx.Request.Path = path;
        if (apiKey != null)
            ctx.Request.Headers["X-API-Key"] = apiKey;
        if (adminKey != null)
            ctx.Request.Headers["X-Admin-Key"] = adminKey;
        ctx.Response.Body = new MemoryStream();
        return ctx;
    }

    [Fact]
    public async Task Health_endpoint_skips_auth()
    {
        var middleware = BuildMiddleware(new ApiKeyOptions { Primary = "key" });
        var ctx = MakeContext(path: "/health");

        await middleware.InvokeAsync(ctx);

        ctx.Response.StatusCode.Should().Be(200);
    }

    [Fact]
    public async Task Missing_key_returns_401()
    {
        var middleware = BuildMiddleware(new ApiKeyOptions { Primary = "key" });
        var ctx = MakeContext(); // no key header

        await middleware.InvokeAsync(ctx);

        ctx.Response.StatusCode.Should().Be(401);
    }

    [Fact]
    public async Task Wrong_key_returns_401()
    {
        var middleware = BuildMiddleware(new ApiKeyOptions { Primary = "correct" });
        var ctx = MakeContext(apiKey: "wrong");

        await middleware.InvokeAsync(ctx);

        ctx.Response.StatusCode.Should().Be(401);
    }

    [Fact]
    public async Task Primary_key_stamps_Primary_tier()
    {
        var middleware = BuildMiddleware(new ApiKeyOptions { Primary = "pk" });
        var ctx = MakeContext(apiKey: "pk");

        await middleware.InvokeAsync(ctx);

        ctx.Items[ApiKeyAuthenticationMiddleware.ApiKeyTierItem].Should().Be("Primary");
    }

    [Fact]
    public async Task Admin_key_via_XAdminKey_header_stamps_Admin_tier()
    {
        var middleware = BuildMiddleware(new ApiKeyOptions { Primary = "pk", Admin = "ak" });
        var ctx = MakeContext(adminKey: "ak");

        await middleware.InvokeAsync(ctx);

        ctx.Items[ApiKeyAuthenticationMiddleware.ApiKeyTierItem].Should().Be("Admin");
    }

    [Fact]
    public async Task Admin_key_via_XApiKey_header_stamps_Admin_tier()
    {
        var middleware = BuildMiddleware(new ApiKeyOptions { Primary = "pk", Admin = "ak" });
        var ctx = MakeContext(apiKey: "ak"); // admin key sent in X-API-Key

        await middleware.InvokeAsync(ctx);

        ctx.Items[ApiKeyAuthenticationMiddleware.ApiKeyTierItem].Should().Be("Admin");
    }

    [Fact]
    public async Task Wms_key_stamps_Wms_tier()
    {
        var middleware = BuildMiddleware(new ApiKeyOptions { Primary = "pk", Wms = "wk" });
        var ctx = MakeContext(apiKey: "wk");

        await middleware.InvokeAsync(ctx);

        ctx.Items[ApiKeyAuthenticationMiddleware.ApiKeyTierItem].Should().Be("Wms");
    }

    [Fact]
    public async Task Wms_key_does_not_match_Primary_tier()
    {
        var middleware = BuildMiddleware(new ApiKeyOptions { Primary = "pk", Wms = "wk" });
        var ctx = MakeContext(apiKey: "wk");

        await middleware.InvokeAsync(ctx);

        ctx.Items[ApiKeyAuthenticationMiddleware.ApiKeyTierItem].Should().NotBe("Primary");
    }

    [Fact]
    public async Task Unconfigured_primary_returns_503()
    {
        var middleware = BuildMiddleware(new ApiKeyOptions { Primary = null });
        var ctx = MakeContext(apiKey: "anything");

        await middleware.InvokeAsync(ctx);

        ctx.Response.StatusCode.Should().Be(503);
    }

    [Fact]
    public async Task Swagger_endpoint_skips_auth_in_development()
    {
        var middleware = BuildMiddleware(new ApiKeyOptions { Primary = "key" }, isDevelopment: true);
        var ctx = MakeContext(path: "/swagger/index.html");

        await middleware.InvokeAsync(ctx);

        ctx.Response.StatusCode.Should().Be(200);
    }

    [Fact]
    public async Task Swagger_endpoint_requires_auth_in_production()
    {
        var middleware = BuildMiddleware(new ApiKeyOptions { Primary = "key" }, isDevelopment: false);
        var ctx = MakeContext(path: "/swagger/index.html"); // no key

        await middleware.InvokeAsync(ctx);

        ctx.Response.StatusCode.Should().Be(401);
    }
}
