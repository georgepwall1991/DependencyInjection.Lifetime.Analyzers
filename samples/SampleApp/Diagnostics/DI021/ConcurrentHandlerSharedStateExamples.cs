using System;
using System.Data.Common;
using System.Threading;

namespace SampleApp.Diagnostics.DI021
{
    // Rule DI021: Non-Thread-Safe Service Shared Across Concurrent Handler Invocations
    // System.Threading.Timer callbacks can overlap. Sharing one DbConnection (or DbContext,
    // HttpContext, ...) across overlapping invocations fails at runtime.

    public class Bad_TimerCallbackSharedConnection
    {
        private readonly DbConnection _connection;
        private Timer? _timer;

        public Bad_TimerCallbackSharedConnection(DbConnection connection)
        {
            _connection = connection;
        }

        public void Start()
        {
            _timer = new Timer(Poll, null, 0, 5000);
        }

        private void Poll(object? state)
        {
            // [DI021] '_connection' is shared across concurrent invocations of System.Threading.Timer callbacks
            var command = _connection.CreateCommand();
            command.CommandText = "SELECT 1";
            command.ExecuteNonQuery();
        }
    }

    public class Good_ConnectionPerInvocation
    {
        private readonly Func<DbConnection> _connectionFactory;
        private Timer? _timer;

        public Good_ConnectionPerInvocation(Func<DbConnection> connectionFactory)
        {
            _connectionFactory = connectionFactory;
        }

        public void Start()
        {
            _timer = new Timer(Poll, null, 0, 5000);
        }

        private void Poll(object? state)
        {
            // Create the non-thread-safe resource per invocation instead of sharing one instance.
            using var connection = _connectionFactory();
            using var command = connection.CreateCommand();
            command.CommandText = "SELECT 1";
            command.ExecuteNonQuery();
        }
    }
}
