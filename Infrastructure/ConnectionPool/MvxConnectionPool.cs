// MvxConnectionPool.cs - Thread-safe connection pool for M3 socket connections
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace Movex.API
{
    public class MvxConnectionPoolOptions
    {
        public int MinPoolSize { get; set; } = 2;
        public int MaxPoolSize { get; set; } = 10;
        public int ConnectionIdleTimeoutSeconds { get; set; } = 300; // 5 minutes
        public int AcquireTimeoutSeconds { get; set; } = 10;
        public bool ValidateOnAcquire { get; set; } = true;
    }

    public class PooledConnection : IDisposable
    {
        public MvxSockWrapper Connection { get; }
        public DateTime LastUsed { get; set; }
        public DateTime Created { get; }
        public bool IsHealthy { get; set; }
        private readonly Action<PooledConnection> _returnToPool;

        public PooledConnection(MvxSockWrapper connection, Action<PooledConnection> returnToPool)
        {
            Connection = connection;
            Created = DateTime.UtcNow;
            LastUsed = DateTime.UtcNow;
            IsHealthy = true;
            _returnToPool = returnToPool;
        }

        public void Dispose()
        {
            // Return to pool when disposed
            _returnToPool?.Invoke(this);
        }
    }

    public class MvxConnectionPool : IDisposable
    {
        private readonly MvxConnectionPoolOptions _options;
        private readonly string _host;
        private readonly int _port;
        private readonly string _username;
        private readonly string _password;
        private readonly string _defaultLibrary;
        private readonly string _defaultProgram;

        private readonly ConcurrentBag<PooledConnection> _availableConnections = new();
        private readonly ConcurrentDictionary<int, PooledConnection> _inUseConnections = new();
        private int _totalConnections = 0;
        private readonly SemaphoreSlim _poolSemaphore;
        private readonly object _lockObject = new();
        private bool _disposed = false;
        private Timer? _cleanupTimer;

        public MvxConnectionPool(
            string host,
            int port,
            string username,
            string password,
            string defaultLibrary = "MVXCOBJ",
            string defaultProgram = "MMS200MI",
            MvxConnectionPoolOptions? options = null)
        {
            _host = host;
            _port = port;
            _username = username;
            _password = password;
            _defaultLibrary = defaultLibrary;
            _defaultProgram = defaultProgram;
            _options = options ?? new MvxConnectionPoolOptions();

            var semaphoreMax = Math.Max(1, _options.MaxPoolSize);
            _poolSemaphore = new SemaphoreSlim(semaphoreMax, semaphoreMax);

            Console.WriteLine($"[ConnectionPool] Initialized with Min={_options.MinPoolSize}, Max={_options.MaxPoolSize}, IdleTimeout={_options.ConnectionIdleTimeoutSeconds}s");

            // Start cleanup timer (run every minute)
            _cleanupTimer = new Timer(CleanupIdleConnections, null, TimeSpan.FromMinutes(1), TimeSpan.FromMinutes(1));
        }

        private void InitializeMinConnections()
        {
            for (int i = 0; i < _options.MinPoolSize; i++)
            {
                try
                {
                    var pooled = CreateNewConnection();
                    _availableConnections.Add(pooled);
                    Interlocked.Increment(ref _totalConnections);
                    Console.WriteLine($"[ConnectionPool] Pre-created connection {i + 1}/{_options.MinPoolSize}");
                }
                catch (Exception ex)
                {
                    Console.Error.WriteLine($"[ConnectionPool] Failed to pre-create connection {i + 1}: {ex.Message}");
                }
            }
        }

        private PooledConnection CreateNewConnection()
        {
            var wrapper = new MvxSockWrapper();
            // Connection will be established on first use via SendReceive/SendReceiveAsync
            return new PooledConnection(wrapper, ReturnConnection);
        }

        public PooledConnection AcquireConnection()
        {
            if (_disposed)
                throw new ObjectDisposedException(nameof(MvxConnectionPool));

            var acquired = _poolSemaphore.Wait(TimeSpan.FromSeconds(_options.AcquireTimeoutSeconds));
            if (!acquired)
            {
                throw new TimeoutException($"Failed to acquire connection within {_options.AcquireTimeoutSeconds} seconds. Pool exhausted.");
            }

            try
            {
                PooledConnection? pooled = null;

                // Try to get an available connection
                while (_availableConnections.TryTake(out pooled))
                {
                    // Validate if enabled
                    if (_options.ValidateOnAcquire && !ValidateConnection(pooled))
                    {
                        Console.WriteLine("[ConnectionPool] Removed invalid connection from pool");
                        DisposePooledConnection(pooled);
                        Interlocked.Decrement(ref _totalConnections);
                        continue;
                    }

                    // Check if connection is too old (idle timeout)
                    if ((DateTime.UtcNow - pooled.LastUsed).TotalSeconds > _options.ConnectionIdleTimeoutSeconds)
                    {
                        Console.WriteLine("[ConnectionPool] Removed idle connection from pool");
                        DisposePooledConnection(pooled);
                        Interlocked.Decrement(ref _totalConnections);
                        continue;
                    }

                    // Valid connection found
                    break;
                }

                // If no valid connection found, create a new one
                if (pooled == null)
                {
                    lock (_lockObject)
                    {
                        if (_totalConnections < _options.MaxPoolSize)
                        {
                            pooled = CreateNewConnection();
                            Interlocked.Increment(ref _totalConnections);
                            Console.WriteLine($"[ConnectionPool] Created new connection (total: {_totalConnections}/{_options.MaxPoolSize})");
                        }
                        else
                        {
                            throw new InvalidOperationException("Pool size limit reached but no connections available");
                        }
                    }
                }

                var leased = pooled ?? throw new InvalidOperationException("Failed to acquire pooled connection");
                leased.LastUsed = DateTime.UtcNow;
                _inUseConnections.TryAdd(leased.GetHashCode(), leased);
                
                Console.WriteLine($"[ConnectionPool] Acquired connection (in-use: {_inUseConnections.Count}, available: {_availableConnections.Count})");
                return leased;
            }
            catch
            {
                _poolSemaphore.Release();
                throw;
            }
        }

        public async Task<PooledConnection> AcquireConnectionAsync()
        {
            if (_disposed)
                throw new ObjectDisposedException(nameof(MvxConnectionPool));

            var acquired = await _poolSemaphore.WaitAsync(TimeSpan.FromSeconds(_options.AcquireTimeoutSeconds));
            if (!acquired)
            {
                throw new TimeoutException($"Failed to acquire connection within {_options.AcquireTimeoutSeconds} seconds. Pool exhausted.");
            }

            try
            {
                PooledConnection? pooled = null;

                // Try to get an available connection
                while (_availableConnections.TryTake(out pooled))
                {
                    // Validate if enabled
                    if (_options.ValidateOnAcquire && !ValidateConnection(pooled))
                    {
                        Console.WriteLine("[ConnectionPool] Removed invalid connection from pool");
                        DisposePooledConnection(pooled);
                        Interlocked.Decrement(ref _totalConnections);
                        continue;
                    }

                    // Check if connection is too old (idle timeout)
                    if ((DateTime.UtcNow - pooled.LastUsed).TotalSeconds > _options.ConnectionIdleTimeoutSeconds)
                    {
                        Console.WriteLine("[ConnectionPool] Removed idle connection from pool");
                        DisposePooledConnection(pooled);
                        Interlocked.Decrement(ref _totalConnections);
                        continue;
                    }

                    // Valid connection found
                    break;
                }

                // If no valid connection found, create a new one
                if (pooled == null)
                {
                    lock (_lockObject)
                    {
                        if (_totalConnections < _options.MaxPoolSize)
                        {
                            pooled = CreateNewConnection();
                            Interlocked.Increment(ref _totalConnections);
                            Console.WriteLine($"[ConnectionPool] Created new connection (total: {_totalConnections}/{_options.MaxPoolSize})");
                        }
                        else
                        {
                            throw new InvalidOperationException("Pool size limit reached but no connections available");
                        }
                    }
                }

                var leased = pooled ?? throw new InvalidOperationException("Failed to acquire pooled connection");
                leased.LastUsed = DateTime.UtcNow;
                _inUseConnections.TryAdd(leased.GetHashCode(), leased);

                Console.WriteLine($"[ConnectionPool] Acquired connection (in-use: {_inUseConnections.Count}, available: {_availableConnections.Count})");
                return leased;
            }
            catch
            {
                _poolSemaphore.Release();
                throw;
            }
        }

        private void ReturnConnection(PooledConnection pooled)
        {
            if (_disposed)
            {
                DisposePooledConnection(pooled);
                return;
            }

            _inUseConnections.TryRemove(pooled.GetHashCode(), out _);
            pooled.LastUsed = DateTime.UtcNow;

            if (pooled.IsHealthy)
            {
                _availableConnections.Add(pooled);
                Console.WriteLine($"[ConnectionPool] Returned connection to pool (in-use: {_inUseConnections.Count}, available: {_availableConnections.Count})");
            }
            else
            {
                Console.WriteLine("[ConnectionPool] Disposed unhealthy connection instead of returning to pool");
                DisposePooledConnection(pooled);
                Interlocked.Decrement(ref _totalConnections);
            }

            _poolSemaphore.Release();
        }

        private bool ValidateConnection(PooledConnection pooled)
        {
            // Simple validation: check if wrapper is not null and healthy flag is set
            // In production, you might send a lightweight ping/health check command
            return pooled != null && pooled.Connection != null && pooled.IsHealthy;
        }

        private void CleanupIdleConnections(object? state)
        {
            if (_disposed)
                return;

            var toRemove = new List<PooledConnection>();
            var tempList = _availableConnections.ToArray();

            foreach (var pooled in tempList)
            {
                if ((DateTime.UtcNow - pooled.LastUsed).TotalSeconds > _options.ConnectionIdleTimeoutSeconds)
                {
                    if (_availableConnections.TryTake(out var removed) && removed == pooled)
                    {
                        toRemove.Add(removed);
                    }
                }
            }

            foreach (var pooled in toRemove)
            {
                // Don't remove below min pool size
                if (_totalConnections > _options.MinPoolSize)
                {
                    Console.WriteLine($"[ConnectionPool] Cleanup: Removing idle connection (age: {(DateTime.UtcNow - pooled.LastUsed).TotalSeconds:F0}s)");
                    DisposePooledConnection(pooled);
                    Interlocked.Decrement(ref _totalConnections);
                }
                else
                {
                    // Return to pool if we're at min size
                    _availableConnections.Add(pooled);
                }
            }
        }

        private void DisposePooledConnection(PooledConnection pooled)
        {
            try
            {
                pooled?.Connection?.Dispose();
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"[ConnectionPool] Error disposing connection: {ex.Message}");
            }
        }

        public void Dispose()
        {
            if (_disposed)
                return;

            _disposed = true;
            _cleanupTimer?.Dispose();

            Console.WriteLine("[ConnectionPool] Disposing all connections...");

            // Dispose all available connections
            while (_availableConnections.TryTake(out var pooled))
            {
                DisposePooledConnection(pooled);
            }

            // Force dispose in-use connections (not ideal, but necessary on shutdown)
            foreach (var kvp in _inUseConnections)
            {
                DisposePooledConnection(kvp.Value);
            }

            _inUseConnections.Clear();
            _poolSemaphore?.Dispose();

            Console.WriteLine("[ConnectionPool] Disposed");
        }

        public ConnectionPoolStats GetStats()
        {
            return new ConnectionPoolStats
            {
                TotalConnections = _totalConnections,
                AvailableConnections = _availableConnections.Count,
                InUseConnections = _inUseConnections.Count,
                MinPoolSize = _options.MinPoolSize,
                MaxPoolSize = _options.MaxPoolSize
            };
        }
    }

    public class ConnectionPoolStats
    {
        public int TotalConnections { get; set; }
        public int AvailableConnections { get; set; }
        public int InUseConnections { get; set; }
        public int MinPoolSize { get; set; }
        public int MaxPoolSize { get; set; }
    }
}
