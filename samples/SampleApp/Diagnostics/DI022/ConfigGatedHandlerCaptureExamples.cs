using System;
using System.Data.Common;
using System.Threading.Tasks;

namespace Azure.Messaging.ServiceBus
{
    // Minimal stand-ins mirroring the Azure SDK surface. DI021/DI022 match sinks by
    // fully-qualified name, so these behave exactly like the real package types.
    public class ServiceBusProcessor
    {
        public event Func<ProcessMessageEventArgs, Task>? ProcessMessageAsync;

        public Task StartProcessingAsync() => Task.CompletedTask;
    }

    public class ProcessMessageEventArgs
    {
    }
}

namespace SampleApp.Diagnostics.DI022
{
    using Azure.Messaging.ServiceBus;

    // Rule DI022: Service Instance Reused Across Handler Invocations (config-gated sink)
    // ServiceBusProcessor dispatches sequentially by default, but MaxConcurrentCalls is a
    // configuration knob: if it is ever raised above 1 this capture becomes a concurrency
    // crash, and even sequentially one instance accumulates state across all messages.

    public class Bad_ProcessorCaptureWithUnprovenConcurrency
    {
        private readonly DbConnection _connection;

        public Bad_ProcessorCaptureWithUnprovenConcurrency(DbConnection connection)
        {
            _connection = connection;
        }

        public void Start(ServiceBusProcessor processor)
        {
            processor.ProcessMessageAsync += args =>
            {
                // [DI022] '_connection' is captured once and reused across all invocations
                var command = _connection.CreateCommand();
                command.CommandText = "SELECT 1";
                command.ExecuteNonQuery();
                return Task.CompletedTask;
            };
        }
    }

    public class Good_ConnectionPerMessage
    {
        private readonly Func<DbConnection> _connectionFactory;

        public Good_ConnectionPerMessage(Func<DbConnection> connectionFactory)
        {
            _connectionFactory = connectionFactory;
        }

        public void Start(ServiceBusProcessor processor)
        {
            processor.ProcessMessageAsync += args =>
            {
                // Create the non-thread-safe resource per message instead of sharing one instance.
                using var connection = _connectionFactory();
                using var command = connection.CreateCommand();
                command.CommandText = "SELECT 1";
                command.ExecuteNonQuery();
                return Task.CompletedTask;
            };
        }
    }
}
