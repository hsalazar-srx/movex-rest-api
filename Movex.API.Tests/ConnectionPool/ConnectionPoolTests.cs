using Movex.API;

namespace Movex.API.Tests.ConnectionPool;

public class ConnectionPoolTests
{
    private static MvxConnectionPoolOptions PoolOptions(int min = 0, int max = 5) =>
        new() { MinPoolSize = min, MaxPoolSize = max, AcquireTimeoutSeconds = 2, ValidateOnAcquire = false };

    [Fact]
    public void Pool_constructs_without_throwing_for_zero_max()
    {
        // Swagger generation mode passes MaxPoolSize=0 — must not throw
        var act = () => new MvxConnectionPool("host", 0, "u", "p", options: PoolOptions(max: 0));
        act.Should().NotThrow();
    }

    [Fact]
    public void Pool_stats_report_correct_max_size()
    {
        using var pool = new MvxConnectionPool("host", 0, "u", "p", options: PoolOptions(max: 5));

        var stats = pool.GetStats();

        stats.MaxPoolSize.Should().Be(5);
    }

    [Fact]
    public void Pool_starts_with_zero_total_connections()
    {
        using var pool = new MvxConnectionPool("host", 0, "u", "p", options: PoolOptions(max: 3));

        var stats = pool.GetStats();

        stats.TotalConnections.Should().Be(0);
        stats.AvailableConnections.Should().Be(0);
        stats.InUseConnections.Should().Be(0);
    }

    [Fact]
    public async Task AcquireConnectionAsync_throws_when_zero_max_pool_size()
    {
        // With MaxPoolSize=0, semaphore max is clamped to 1 but _totalConnections < 0 is always false
        // so it should immediately fail with timeout or pool-limit error
        using var pool = new MvxConnectionPool("host", 0, "u", "p", options: PoolOptions(max: 0));

        var act = async () => await pool.AcquireConnectionAsync();

        await act.Should().ThrowAsync<Exception>();
    }

    [Fact]
    public void Pool_dispose_does_not_throw()
    {
        var pool = new MvxConnectionPool("host", 0, "u", "p", options: PoolOptions(max: 2));
        var act = () => pool.Dispose();
        act.Should().NotThrow();
    }

    [Fact]
    public void Double_dispose_does_not_throw()
    {
        var pool = new MvxConnectionPool("host", 0, "u", "p", options: PoolOptions(max: 2));
        pool.Dispose();
        var act = () => pool.Dispose();
        act.Should().NotThrow();
    }

    [Fact]
    public async Task AcquireConnectionAsync_throws_ObjectDisposed_after_pool_disposed()
    {
        var pool = new MvxConnectionPool("host", 0, "u", "p", options: PoolOptions(max: 2));
        pool.Dispose();

        var act = async () => await pool.AcquireConnectionAsync();

        await act.Should().ThrowAsync<ObjectDisposedException>();
    }
}
