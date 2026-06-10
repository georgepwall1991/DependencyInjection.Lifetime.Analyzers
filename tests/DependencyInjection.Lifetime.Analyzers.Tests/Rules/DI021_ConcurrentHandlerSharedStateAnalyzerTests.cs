using System.Threading.Tasks;
using DependencyInjection.Lifetime.Analyzers.Rules;
using DependencyInjection.Lifetime.Analyzers.Tests.Infrastructure;
using Xunit;

namespace DependencyInjection.Lifetime.Analyzers.Tests.Rules;

public class DI021_ConcurrentHandlerSharedStateAnalyzerTests
{
    private const string BaseUsings = """
        using System;
        using System.Threading;
        using System.Threading.Tasks;
        using Microsoft.Extensions.DependencyInjection;

        """;

    private const string ServiceBusUsing = "using Azure.Messaging.ServiceBus;\n";
    private const string EventHubsUsing = "using Azure.Messaging.EventHubs;\n";
    private const string HostingUsing = "using Microsoft.Extensions.Hosting;\n";
    private const string DataUsing = "using System.Data.Common;\n";

    /// <summary>
    /// Source stubs mirroring the metadata shape of Azure.Messaging.ServiceBus. The analyzer
    /// matches sinks by fully-qualified name, so source-declared stubs behave like the real SDK.
    /// </summary>
    private const string ServiceBusStubs = """
        namespace Azure.Messaging.ServiceBus
        {
            public class ServiceBusClient
            {
                public ServiceBusProcessor CreateProcessor(string queueName) => new ServiceBusProcessor();
                public ServiceBusProcessor CreateProcessor(string queueName, ServiceBusProcessorOptions options) => new ServiceBusProcessor();
                public ServiceBusSessionProcessor CreateSessionProcessor(string queueName) => new ServiceBusSessionProcessor();
                public ServiceBusSessionProcessor CreateSessionProcessor(string queueName, ServiceBusSessionProcessorOptions options) => new ServiceBusSessionProcessor();
            }

            public class ServiceBusProcessorOptions
            {
                public int MaxConcurrentCalls { get; set; }
            }

            public class ServiceBusSessionProcessorOptions
            {
                public int MaxConcurrentSessions { get; set; }
                public int MaxConcurrentCallsPerSession { get; set; }
            }

            public class ServiceBusProcessor
            {
                public event System.Func<ProcessMessageEventArgs, System.Threading.Tasks.Task> ProcessMessageAsync;
                public event System.Func<ProcessErrorEventArgs, System.Threading.Tasks.Task> ProcessErrorAsync;
                public System.Threading.Tasks.Task StartProcessingAsync(System.Threading.CancellationToken cancellationToken = default) => System.Threading.Tasks.Task.CompletedTask;
            }

            public class ServiceBusSessionProcessor
            {
                public event System.Func<ProcessSessionMessageEventArgs, System.Threading.Tasks.Task> ProcessMessageAsync;
                public event System.Func<ProcessErrorEventArgs, System.Threading.Tasks.Task> ProcessErrorAsync;
                public System.Threading.Tasks.Task StartProcessingAsync(System.Threading.CancellationToken cancellationToken = default) => System.Threading.Tasks.Task.CompletedTask;
            }

            public class ProcessMessageEventArgs { }
            public class ProcessSessionMessageEventArgs { }
            public class ProcessErrorEventArgs { }
        }

        """;

    private const string EventHubsStubs = """
        namespace Azure.Messaging.EventHubs
        {
            public class EventProcessorClient
            {
                public event System.Func<Azure.Messaging.EventHubs.Processor.ProcessEventArgs, System.Threading.Tasks.Task> ProcessEventAsync;
                public event System.Func<Azure.Messaging.EventHubs.Processor.ProcessErrorEventArgs, System.Threading.Tasks.Task> ProcessErrorAsync;
            }
        }

        namespace Azure.Messaging.EventHubs.Processor
        {
            public class ProcessEventArgs { }
            public class ProcessErrorEventArgs { }
        }

        """;

    private const string EfCoreStubs = """
        namespace Microsoft.EntityFrameworkCore
        {
            public class DbContext : System.IDisposable
            {
                public int SaveChanges() => 0;
                public System.Threading.Tasks.Task<int> SaveChangesAsync(System.Threading.CancellationToken cancellationToken = default) => System.Threading.Tasks.Task.FromResult(0);
                public void Dispose() { }
            }

            public interface IDbContextFactory<TContext> where TContext : DbContext
            {
                TContext CreateDbContext();
                System.Threading.Tasks.Task<TContext> CreateDbContextAsync(System.Threading.CancellationToken cancellationToken = default);
            }
        }

        public class AppDbContext : Microsoft.EntityFrameworkCore.DbContext
        {
            public void Add(object entity) { }
        }

        """;

    private const string HostingStubs = """
        namespace Microsoft.Extensions.Hosting
        {
            public interface IHostedService
            {
                System.Threading.Tasks.Task StartAsync(System.Threading.CancellationToken cancellationToken);
                System.Threading.Tasks.Task StopAsync(System.Threading.CancellationToken cancellationToken);
            }

            public abstract class BackgroundService : IHostedService
            {
                protected abstract System.Threading.Tasks.Task ExecuteAsync(System.Threading.CancellationToken stoppingToken);
                public virtual System.Threading.Tasks.Task StartAsync(System.Threading.CancellationToken cancellationToken) => System.Threading.Tasks.Task.CompletedTask;
                public virtual System.Threading.Tasks.Task StopAsync(System.Threading.CancellationToken cancellationToken) => System.Threading.Tasks.Task.CompletedTask;
            }
        }

        """;

    private static Task VerifyAsync(string source) =>
        AnalyzerVerifier<DI021_ConcurrentHandlerSharedStateAnalyzer>.VerifyDiagnosticsAsync(source);

    private static Task VerifyNoneAsync(string source) =>
        AnalyzerVerifier<DI021_ConcurrentHandlerSharedStateAnalyzer>.VerifyNoDiagnosticsAsync(source);

    // ---------------------------------------------------------------------
    // Positives: always-concurrent sinks
    // ---------------------------------------------------------------------

    [Fact]
    public async Task SessionProcessor_DbContextField_MethodGroupHandler_ReportsDiagnostic()
    {
        var source = ServiceBusUsing + HostingUsing + BaseUsings + ServiceBusStubs + EfCoreStubs + HostingStubs + """
            public class OrderProcessor : BackgroundService
            {
                private readonly AppDbContext _db;
                private readonly ServiceBusSessionProcessor _processor;

                public OrderProcessor(AppDbContext db, ServiceBusSessionProcessor processor)
                {
                    _db = db;
                    _processor = processor;
                }

                protected override async Task ExecuteAsync(CancellationToken stoppingToken)
                {
                    _processor.ProcessMessageAsync += HandleAsync;
                    await _processor.StartProcessingAsync(stoppingToken);
                }

                private async Task HandleAsync(ProcessSessionMessageEventArgs args)
                {
                    {|DI021:_db|}.Add(args);
                    await _db.SaveChangesAsync();
                }
            }
            """;

        await VerifyAsync(source);
    }

    [Fact]
    public async Task SessionProcessor_LambdaCapturesOuterLocal_ReportsDiagnostic()
    {
        var source = ServiceBusUsing + BaseUsings + ServiceBusStubs + EfCoreStubs + """
            public class Worker
            {
                public async Task RunAsync(ServiceBusSessionProcessor processor)
                {
                    var db = new AppDbContext();
                    processor.ProcessMessageAsync += async args =>
                    {
                        {|DI021:db|}.Add(args);
                        await db.SaveChangesAsync();
                    };
                    await processor.StartProcessingAsync();
                }
            }
            """;

        await VerifyAsync(source);
    }

    [Fact]
    public async Task EventProcessorClient_FieldUseInLambda_ReportsDiagnostic()
    {
        var source = EventHubsUsing + BaseUsings + EventHubsStubs + EfCoreStubs + """
            public class TelemetryIngester
            {
                private readonly AppDbContext _db;
                private readonly EventProcessorClient _client;

                public TelemetryIngester(AppDbContext db, EventProcessorClient client)
                {
                    _db = db;
                    _client = client;
                }

                public void Start()
                {
                    _client.ProcessEventAsync += async args =>
                    {
                        {|DI021:_db|}.Add(args);
                        await _db.SaveChangesAsync();
                    };
                }
            }
            """;

        await VerifyAsync(source);
    }

    [Fact]
    public async Task ServiceBusProcessor_MaxConcurrentCallsConstantAboveOne_ReportsWarning()
    {
        var source = ServiceBusUsing + BaseUsings + ServiceBusStubs + EfCoreStubs + """
            public class Worker
            {
                private readonly AppDbContext _db;

                public Worker(AppDbContext db)
                {
                    _db = db;
                }

                public void Start(ServiceBusClient client)
                {
                    var processor = client.CreateProcessor("queue", new ServiceBusProcessorOptions
                    {
                        MaxConcurrentCalls = 8
                    });
                    processor.ProcessMessageAsync += async args =>
                    {
                        {|DI021:_db|}.Add(args);
                        await _db.SaveChangesAsync();
                    };
                }
            }
            """;

        await VerifyAsync(source);
    }

    [Fact]
    public async Task ServiceBusProcessor_OptionsConfiguredInCtor_SubscribedInStartAsync_ReportsWarning()
    {
        var source = ServiceBusUsing + HostingUsing + BaseUsings + ServiceBusStubs + EfCoreStubs + HostingStubs + """
            public class Worker : BackgroundService
            {
                private readonly AppDbContext _db;
                private readonly ServiceBusProcessor _processor;

                public Worker(AppDbContext db, ServiceBusClient client)
                {
                    _db = db;
                    var options = new ServiceBusProcessorOptions();
                    options.MaxConcurrentCalls = 4;
                    _processor = client.CreateProcessor("queue", options);
                }

                protected override async Task ExecuteAsync(CancellationToken stoppingToken)
                {
                    _processor.ProcessMessageAsync += HandleAsync;
                    await _processor.StartProcessingAsync(stoppingToken);
                }

                private async Task HandleAsync(ProcessMessageEventArgs args)
                {
                    {|DI021:_db|}.Add(args);
                    await _db.SaveChangesAsync();
                }
            }
            """;

        await VerifyAsync(source);
    }

    [Fact]
    public async Task ThreadingTimer_FinitePeriod_CallbackUsesField_ReportsDiagnostic()
    {
        var source = BaseUsings + EfCoreStubs + """
            public class Poller
            {
                private readonly AppDbContext _db;
                private Timer _timer;

                public Poller(AppDbContext db)
                {
                    _db = db;
                }

                public void Start()
                {
                    _timer = new Timer(Poll, null, 0, 5000);
                }

                private void Poll(object state)
                {
                    {|DI021:_db|}.Add(state);
                    _db.SaveChanges();
                }
            }
            """;

        await VerifyAsync(source);
    }

    [Fact]
    public async Task TimersTimer_ElapsedWithDefaultAutoReset_ReportsDiagnostic()
    {
        var source = BaseUsings + EfCoreStubs + """
            public class Poller
            {
                private readonly AppDbContext _db;

                public Poller(AppDbContext db)
                {
                    _db = db;
                }

                public void Start()
                {
                    var timer = new System.Timers.Timer(5000);
                    timer.Elapsed += (sender, args) =>
                    {
                        {|DI021:_db|}.Add(args);
                        _db.SaveChanges();
                    };
                    timer.Start();
                }
            }
            """;

        await VerifyAsync(source);
    }

    [Fact]
    public async Task ParallelForEach_SharedDbConnection_ReportsDiagnostic()
    {
        var source = DataUsing + BaseUsings + """
            public class Importer
            {
                public void Import(string[] rows, DbConnection connection)
                {
                    Parallel.ForEach(rows, row =>
                    {
                        var command = {|DI021:connection|}.CreateCommand();
                        command.CommandText = row;
                        command.ExecuteNonQuery();
                    });
                }
            }
            """;

        await VerifyAsync(source);
    }

    [Fact]
    public async Task ParallelForEachAsync_SharedDbContext_ReportsDiagnostic()
    {
        var source = BaseUsings + EfCoreStubs + """
            public class Importer
            {
                public async Task ImportAsync(string[] rows)
                {
                    var db = new AppDbContext();
                    await Parallel.ForEachAsync(rows, async (row, ct) =>
                    {
                        {|DI021:db|}.Add(row);
                        await db.SaveChangesAsync(ct);
                    });
                }
            }
            """;

        await VerifyAsync(source);
    }

    [Fact]
    public async Task ParallelInvoke_SharedDbContext_ReportsDiagnostic()
    {
        var source = BaseUsings + EfCoreStubs + """
            public class Importer
            {
                private readonly AppDbContext _db;

                public Importer(AppDbContext db)
                {
                    _db = db;
                }

                public void Import()
                {
                    Parallel.Invoke(
                        () => {|DI021:_db|}.Add("a"),
                        () => {|DI021:_db|}.Add("b"));
                }
            }
            """;

        await VerifyAsync(source);
    }

    [Fact]
    public async Task OneHopDelegation_ThinLambdaToInstanceMethod_ReportsDiagnostic()
    {
        var source = ServiceBusUsing + BaseUsings + ServiceBusStubs + EfCoreStubs + """
            public class Worker
            {
                private readonly AppDbContext _db;
                private readonly ServiceBusSessionProcessor _processor;

                public Worker(AppDbContext db, ServiceBusSessionProcessor processor)
                {
                    _db = db;
                    _processor = processor;
                }

                public void Start()
                {
                    _processor.ProcessMessageAsync += args => HandleAsync(args);
                }

                private async Task HandleAsync(ProcessSessionMessageEventArgs args)
                {
                    {|DI021:_db|}.Add(args);
                    await _db.SaveChangesAsync();
                }
            }
            """;

        await VerifyAsync(source);
    }

    [Fact]
    public async Task CapturedScopeResolution_LongLivedScopeResolvedInsideHandler_ReportsDiagnostic()
    {
        var source = ServiceBusUsing + BaseUsings + ServiceBusStubs + EfCoreStubs + """
            public class Worker
            {
                private readonly IServiceScopeFactory _scopeFactory;
                private IServiceScope _scope;

                public Worker(IServiceScopeFactory scopeFactory)
                {
                    _scopeFactory = scopeFactory;
                }

                public void Start(ServiceBusSessionProcessor processor)
                {
                    _scope = _scopeFactory.CreateScope();
                    processor.ProcessMessageAsync += async args =>
                    {
                        var db = {|DI021:_scope.ServiceProvider.GetRequiredService<AppDbContext>()|};
                        db.Add(args);
                        await db.SaveChangesAsync();
                    };
                }
            }
            """;

        await VerifyAsync(source);
    }

    [Fact]
    public async Task CapturedOuterScopeLocal_ResolvedInsideParallelBody_ReportsDiagnostic()
    {
        var source = BaseUsings + EfCoreStubs + """
            public class Importer
            {
                private readonly IServiceScopeFactory _scopeFactory;

                public Importer(IServiceScopeFactory scopeFactory)
                {
                    _scopeFactory = scopeFactory;
                }

                public void Import(string[] rows)
                {
                    using var scope = _scopeFactory.CreateScope();
                    Parallel.ForEach(rows, row =>
                    {
                        var db = {|DI021:scope.ServiceProvider.GetRequiredService<AppDbContext>()|};
                        db.Add(row);
                        db.SaveChanges();
                    });
                }
            }
            """;

        await VerifyAsync(source);
    }

    [Fact]
    public async Task DerivedDbContext_TwoLevelsDeep_ReportsDiagnostic()
    {
        var source = ServiceBusUsing + BaseUsings + ServiceBusStubs + EfCoreStubs + """
            public abstract class TenantDbContext : AppDbContext { }
            public class OrdersDbContext : TenantDbContext { }

            public class Worker
            {
                private readonly OrdersDbContext _db;

                public Worker(OrdersDbContext db)
                {
                    _db = db;
                }

                public void Start(ServiceBusSessionProcessor processor)
                {
                    processor.ProcessMessageAsync += async args =>
                    {
                        {|DI021:_db|}.Add(args);
                        await _db.SaveChangesAsync();
                    };
                }
            }
            """;

        await VerifyAsync(source);
    }

    [Fact]
    public async Task EnclosingMethodParameter_CapturedIntoHandler_ReportsDiagnostic()
    {
        var source = ServiceBusUsing + BaseUsings + ServiceBusStubs + EfCoreStubs + """
            public class Worker
            {
                public void Start(ServiceBusSessionProcessor processor, AppDbContext db)
                {
                    processor.ProcessMessageAsync += async args =>
                    {
                        {|DI021:db|}.Add(args);
                        await db.SaveChangesAsync();
                    };
                }
            }
            """;

        await VerifyAsync(source);
    }

    [Fact]
    public async Task StaticDbContextField_UsedInMethodGroupHandler_ReportsDiagnostic()
    {
        var source = ServiceBusUsing + BaseUsings + ServiceBusStubs + EfCoreStubs + """
            public class Worker
            {
                private static AppDbContext _shared = new AppDbContext();

                public void Start(ServiceBusSessionProcessor processor)
                {
                    processor.ProcessMessageAsync += HandleAsync;
                }

                private static async Task HandleAsync(ProcessSessionMessageEventArgs args)
                {
                    {|DI021:_shared|}.Add(args);
                    await _shared.SaveChangesAsync();
                }
            }
            """;

        await VerifyAsync(source);
    }

    [Fact]
    public async Task NestedReceiverChain_ProcessorBehindHolderField_ReportsDiagnostic()
    {
        var source = ServiceBusUsing + BaseUsings + ServiceBusStubs + EfCoreStubs + """
            public class ProcessorHolder
            {
                public ServiceBusSessionProcessor Processor { get; } = new ServiceBusSessionProcessor();
            }

            public class Worker
            {
                private readonly AppDbContext _db;
                private readonly ProcessorHolder _holder = new ProcessorHolder();

                public Worker(AppDbContext db)
                {
                    _db = db;
                }

                public void Start()
                {
                    _holder.Processor.ProcessMessageAsync += async args =>
                    {
                        {|DI021:_db|}.Add(args);
                        await _db.SaveChangesAsync();
                    };
                }
            }
            """;

        await VerifyAsync(source);
    }

    [Fact]
    public async Task HttpContextField_UsedInTimerCallback_ReportsDiagnostic()
    {
        var source = BaseUsings + """
            namespace Microsoft.AspNetCore.Http
            {
                public class HttpContext
                {
                    public string TraceIdentifier { get; set; }
                }
            }

            public class Snapshotter
            {
                private readonly Microsoft.AspNetCore.Http.HttpContext _context;

                public Snapshotter(Microsoft.AspNetCore.Http.HttpContext context)
                {
                    _context = context;
                }

                public void Start()
                {
                    var timer = new Timer(_ => System.Console.WriteLine({|DI021:_context|}.TraceIdentifier), null, 0, 1000);
                }
            }
            """;

        await VerifyAsync(source);
    }

    // ---------------------------------------------------------------------
    // DI022: config-gated sinks with unprovable concurrency
    // ---------------------------------------------------------------------

    [Fact]
    public async Task ServiceBusProcessor_NoOptions_ReportsInfo()
    {
        var source = ServiceBusUsing + BaseUsings + ServiceBusStubs + EfCoreStubs + """
            public class Worker
            {
                private readonly AppDbContext _db;

                public Worker(AppDbContext db)
                {
                    _db = db;
                }

                public void Start(ServiceBusClient client)
                {
                    var processor = client.CreateProcessor("queue");
                    processor.ProcessMessageAsync += async args =>
                    {
                        {|DI022:_db|}.Add(args);
                        await _db.SaveChangesAsync();
                    };
                }
            }
            """;

        await VerifyAsync(source);
    }

    [Fact]
    public async Task ServiceBusProcessor_KnobFromConfiguration_ReportsInfo()
    {
        var source = ServiceBusUsing + BaseUsings + ServiceBusStubs + EfCoreStubs + """
            public class Worker
            {
                private readonly AppDbContext _db;

                public Worker(AppDbContext db)
                {
                    _db = db;
                }

                public void Start(ServiceBusClient client, int configuredConcurrency)
                {
                    var processor = client.CreateProcessor("queue", new ServiceBusProcessorOptions
                    {
                        MaxConcurrentCalls = configuredConcurrency
                    });
                    processor.ProcessMessageAsync += async args =>
                    {
                        {|DI022:_db|}.Add(args);
                        await _db.SaveChangesAsync();
                    };
                }
            }
            """;

        await VerifyAsync(source);
    }

    // ---------------------------------------------------------------------
    // Negatives: safe patterns
    // ---------------------------------------------------------------------

    [Fact]
    public async Task Handler_CreatesOwnScopePerInvocation_NoDiagnostic()
    {
        var source = ServiceBusUsing + BaseUsings + ServiceBusStubs + EfCoreStubs + """
            public class Worker
            {
                private readonly IServiceScopeFactory _scopeFactory;

                public Worker(IServiceScopeFactory scopeFactory)
                {
                    _scopeFactory = scopeFactory;
                }

                public void Start(ServiceBusSessionProcessor processor)
                {
                    processor.ProcessMessageAsync += async args =>
                    {
                        await using var scope = _scopeFactory.CreateAsyncScope();
                        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
                        db.Add(args);
                        await db.SaveChangesAsync();
                    };
                }
            }
            """;

        await VerifyNoneAsync(source);
    }

    [Fact]
    public async Task Handler_UsesDbContextFactoryPerInvocation_NoDiagnostic()
    {
        var source = ServiceBusUsing + BaseUsings + ServiceBusStubs + EfCoreStubs + """
            public class Worker
            {
                private readonly Microsoft.EntityFrameworkCore.IDbContextFactory<AppDbContext> _factory;

                public Worker(Microsoft.EntityFrameworkCore.IDbContextFactory<AppDbContext> factory)
                {
                    _factory = factory;
                }

                public void Start(ServiceBusSessionProcessor processor)
                {
                    processor.ProcessMessageAsync += async args =>
                    {
                        using var db = await _factory.CreateDbContextAsync();
                        db.Add(args);
                        await db.SaveChangesAsync();
                    };
                }
            }
            """;

        await VerifyNoneAsync(source);
    }

    [Fact]
    public async Task Handler_CreatesContextInline_NoDiagnostic()
    {
        var source = ServiceBusUsing + BaseUsings + ServiceBusStubs + EfCoreStubs + """
            public class Worker
            {
                public void Start(ServiceBusSessionProcessor processor)
                {
                    processor.ProcessMessageAsync += async args =>
                    {
                        using var db = new AppDbContext();
                        db.Add(args);
                        await db.SaveChangesAsync();
                    };
                }
            }
            """;

        await VerifyNoneAsync(source);
    }

    [Fact]
    public async Task ServiceBusProcessor_MaxConcurrentCallsOne_NoDiagnostic()
    {
        var source = ServiceBusUsing + BaseUsings + ServiceBusStubs + EfCoreStubs + """
            public class Worker
            {
                private readonly AppDbContext _db;

                public Worker(AppDbContext db)
                {
                    _db = db;
                }

                public void Start(ServiceBusClient client)
                {
                    var processor = client.CreateProcessor("queue", new ServiceBusProcessorOptions
                    {
                        MaxConcurrentCalls = 1
                    });
                    processor.ProcessMessageAsync += async args =>
                    {
                        _db.Add(args);
                        await _db.SaveChangesAsync();
                    };
                }
            }
            """;

        await VerifyNoneAsync(source);
    }

    [Fact]
    public async Task SessionProcessor_SingleSessionSingleCall_NoDiagnostic()
    {
        var source = ServiceBusUsing + BaseUsings + ServiceBusStubs + EfCoreStubs + """
            public class Worker
            {
                private readonly AppDbContext _db;

                public Worker(AppDbContext db)
                {
                    _db = db;
                }

                public void Start(ServiceBusClient client)
                {
                    var processor = client.CreateSessionProcessor("queue", new ServiceBusSessionProcessorOptions
                    {
                        MaxConcurrentSessions = 1
                    });
                    processor.ProcessMessageAsync += async args =>
                    {
                        _db.Add(args);
                        await _db.SaveChangesAsync();
                    };
                }
            }
            """;

        await VerifyNoneAsync(source);
    }

    [Fact]
    public async Task Parallel_MaxDegreeOfParallelismOne_NoDiagnostic()
    {
        var source = BaseUsings + EfCoreStubs + """
            public class Importer
            {
                private readonly AppDbContext _db;

                public Importer(AppDbContext db)
                {
                    _db = db;
                }

                public void Import(string[] rows)
                {
                    var options = new ParallelOptions { MaxDegreeOfParallelism = 1 };
                    Parallel.ForEach(rows, options, row =>
                    {
                        _db.Add(row);
                        _db.SaveChanges();
                    });
                }
            }
            """;

        await VerifyNoneAsync(source);
    }

    [Fact]
    public async Task ThreadingTimer_InfiniteTimeSpanPeriod_OneShot_NoDiagnostic()
    {
        var source = BaseUsings + EfCoreStubs + """
            public class OneShot
            {
                private readonly AppDbContext _db;
                private Timer _timer;

                public OneShot(AppDbContext db)
                {
                    _db = db;
                }

                public void Schedule()
                {
                    _timer = new Timer(Run, null, TimeSpan.FromSeconds(5), Timeout.InfiniteTimeSpan);
                }

                private void Run(object state)
                {
                    _db.Add(state);
                    _db.SaveChanges();
                }
            }
            """;

        await VerifyNoneAsync(source);
    }

    [Fact]
    public async Task ThreadingTimer_TimeoutInfinitePeriod_OneShot_NoDiagnostic()
    {
        var source = BaseUsings + EfCoreStubs + """
            public class OneShot
            {
                private readonly AppDbContext _db;
                private Timer _timer;

                public OneShot(AppDbContext db)
                {
                    _db = db;
                }

                public void Schedule()
                {
                    _timer = new Timer(Run, null, 5000, Timeout.Infinite);
                }

                private void Run(object state)
                {
                    _db.Add(state);
                    _db.SaveChanges();
                }
            }
            """;

        await VerifyNoneAsync(source);
    }

    [Fact]
    public async Task ThreadingTimer_NeverStartedSingleArgCtor_NoDiagnostic()
    {
        var source = BaseUsings + EfCoreStubs + """
            public class Dormant
            {
                private readonly AppDbContext _db;
                private Timer _timer;

                public Dormant(AppDbContext db)
                {
                    _db = db;
                    _timer = new Timer(Run);
                }

                private void Run(object state)
                {
                    _db.Add(state);
                    _db.SaveChanges();
                }
            }
            """;

        await VerifyNoneAsync(source);
    }

    [Fact]
    public async Task TimersTimer_AutoResetFalse_NoDiagnostic()
    {
        var source = BaseUsings + EfCoreStubs + """
            public class OneShot
            {
                private readonly AppDbContext _db;

                public OneShot(AppDbContext db)
                {
                    _db = db;
                }

                public void Start()
                {
                    var timer = new System.Timers.Timer(5000);
                    timer.AutoReset = false;
                    timer.Elapsed += (sender, args) =>
                    {
                        _db.Add(args);
                        _db.SaveChanges();
                    };
                    timer.Start();
                }
            }
            """;

        await VerifyNoneAsync(source);
    }

    [Fact]
    public async Task Handler_SerializedWithSemaphoreSlim_NoDiagnostic()
    {
        var source = ServiceBusUsing + BaseUsings + ServiceBusStubs + EfCoreStubs + """
            public class Worker
            {
                private readonly AppDbContext _db;
                private readonly SemaphoreSlim _gate = new SemaphoreSlim(1, 1);

                public Worker(AppDbContext db)
                {
                    _db = db;
                }

                public void Start(ServiceBusSessionProcessor processor)
                {
                    processor.ProcessMessageAsync += async args =>
                    {
                        await _gate.WaitAsync();
                        try
                        {
                            _db.Add(args);
                            await _db.SaveChangesAsync();
                        }
                        finally
                        {
                            _gate.Release();
                        }
                    };
                }
            }
            """;

        await VerifyNoneAsync(source);
    }

    [Fact]
    public async Task Handler_SerializedWithLock_NoDiagnostic()
    {
        var source = BaseUsings + EfCoreStubs + """
            public class Importer
            {
                private readonly AppDbContext _db;
                private readonly object _sync = new object();

                public Importer(AppDbContext db)
                {
                    _db = db;
                }

                public void Import(string[] rows)
                {
                    Parallel.ForEach(rows, row =>
                    {
                        lock (_sync)
                        {
                            _db.Add(row);
                            _db.SaveChanges();
                        }
                    });
                }
            }
            """;

        await VerifyNoneAsync(source);
    }

    [Fact]
    public async Task Handler_InterlockedReentrancyGuard_NoDiagnostic()
    {
        var source = BaseUsings + EfCoreStubs + """
            public class Poller
            {
                private readonly AppDbContext _db;
                private int _running;
                private Timer _timer;

                public Poller(AppDbContext db)
                {
                    _db = db;
                }

                public void Start()
                {
                    _timer = new Timer(Poll, null, 0, 1000);
                }

                private void Poll(object state)
                {
                    if (Interlocked.CompareExchange(ref _running, 1, 0) != 0)
                    {
                        return;
                    }

                    try
                    {
                        _db.Add(state);
                        _db.SaveChanges();
                    }
                    finally
                    {
                        Interlocked.Exchange(ref _running, 0);
                    }
                }
            }
            """;

        await VerifyNoneAsync(source);
    }

    [Fact]
    public async Task Handler_TimerRearmGuard_NoDiagnostic()
    {
        var source = BaseUsings + EfCoreStubs + """
            public class Poller
            {
                private readonly AppDbContext _db;
                private Timer _timer;

                public Poller(AppDbContext db)
                {
                    _db = db;
                }

                public void Start()
                {
                    _timer = new Timer(Poll, null, 0, 1000);
                }

                private void Poll(object state)
                {
                    _timer.Change(Timeout.Infinite, Timeout.Infinite);
                    try
                    {
                        _db.Add(state);
                        _db.SaveChanges();
                    }
                    finally
                    {
                        _timer.Change(1000, 1000);
                    }
                }
            }
            """;

        await VerifyNoneAsync(source);
    }

    [Fact]
    public async Task Handler_CapturesOnlyWhitelistedServices_NoDiagnostic()
    {
        var source = "using Microsoft.Extensions.Logging;\n" + ServiceBusUsing + BaseUsings + ServiceBusStubs + EfCoreStubs + """
            namespace Microsoft.Extensions.Logging
            {
                public interface ILogger { void Log(string message); }
                public interface ILogger<T> : ILogger { }
            }

            public class Worker
            {
                private readonly ILogger<Worker> _logger;
                private readonly IServiceScopeFactory _scopeFactory;

                public Worker(ILogger<Worker> logger, IServiceScopeFactory scopeFactory)
                {
                    _logger = logger;
                    _scopeFactory = scopeFactory;
                }

                public void Start(ServiceBusSessionProcessor processor)
                {
                    processor.ProcessMessageAsync += async args =>
                    {
                        _logger.Log("received");
                        await using var scope = _scopeFactory.CreateAsyncScope();
                        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
                        db.Add(args);
                        await db.SaveChangesAsync();
                    };
                }
            }
            """;

        await VerifyNoneAsync(source);
    }

    [Fact]
    public async Task Handler_FieldReassignedInsideHandlerBeforeUse_NoDiagnostic()
    {
        var source = ServiceBusUsing + BaseUsings + ServiceBusStubs + EfCoreStubs + """
            public class Worker
            {
                private AppDbContext _db;

                public void Start(ServiceBusSessionProcessor processor)
                {
                    processor.ProcessMessageAsync += async args =>
                    {
                        _db = new AppDbContext();
                        _db.Add(args);
                        await _db.SaveChangesAsync();
                    };
                }
            }
            """;

        await VerifyNoneAsync(source);
    }

    [Fact]
    public async Task Handler_UsesOnlyHandlerParameters_NoDiagnostic()
    {
        var source = ServiceBusUsing + BaseUsings + ServiceBusStubs + EfCoreStubs + """
            public class Worker
            {
                public void Start(ServiceBusSessionProcessor processor)
                {
                    processor.ProcessMessageAsync += async args =>
                    {
                        System.Console.WriteLine(args);
                        await Task.CompletedTask;
                    };
                }
            }
            """;

        await VerifyNoneAsync(source);
    }

    [Fact]
    public async Task MassTransitShapedConsumer_CtorInjectedDbContext_NoDiagnostic()
    {
        var source = BaseUsings + EfCoreStubs + """
            namespace MassTransit
            {
                public interface ConsumeContext<T> { T Message { get; } }
                public interface IConsumer<T>
                {
                    System.Threading.Tasks.Task Consume(ConsumeContext<T> context);
                }
            }

            public class OrderSubmitted { }

            public class OrderConsumer : MassTransit.IConsumer<OrderSubmitted>
            {
                private readonly AppDbContext _db;

                public OrderConsumer(AppDbContext db)
                {
                    _db = db;
                }

                public async Task Consume(MassTransit.ConsumeContext<OrderSubmitted> context)
                {
                    _db.Add(context.Message);
                    await _db.SaveChangesAsync();
                }
            }
            """;

        await VerifyNoneAsync(source);
    }

    [Fact]
    public async Task AwaitedTaskRun_SingleFanOut_NoDiagnostic()
    {
        var source = BaseUsings + EfCoreStubs + """
            public class Worker
            {
                private readonly AppDbContext _db;

                public Worker(AppDbContext db)
                {
                    _db = db;
                }

                public async Task RunAsync()
                {
                    await Task.Run(() =>
                    {
                        _db.Add("row");
                        _db.SaveChanges();
                    });
                }
            }
            """;

        await VerifyNoneAsync(source);
    }

    [Fact]
    public async Task SameNamedUserType_DifferentNamespace_NoDiagnostic()
    {
        var source = BaseUsings + EfCoreStubs + """
            namespace MyCompany.Messaging
            {
                public class ServiceBusProcessor
                {
                    public event System.Func<string, System.Threading.Tasks.Task> ProcessMessageAsync;
                }
            }

            public class Worker
            {
                private readonly AppDbContext _db;

                public Worker(AppDbContext db)
                {
                    _db = db;
                }

                public void Start(MyCompany.Messaging.ServiceBusProcessor processor)
                {
                    processor.ProcessMessageAsync += async message =>
                    {
                        _db.Add(message);
                        await _db.SaveChangesAsync();
                    };
                }
            }
            """;

        await VerifyNoneAsync(source);
    }

    [Fact]
    public async Task MethodGroupHandler_DeclaredOnDifferentType_NoDiagnostic()
    {
        var source = ServiceBusUsing + BaseUsings + ServiceBusStubs + EfCoreStubs + """
            public class Handlers
            {
                public static Task HandleAsync(ProcessSessionMessageEventArgs args) => Task.CompletedTask;
            }

            public class Worker
            {
                public void Start(ServiceBusSessionProcessor processor)
                {
                    processor.ProcessMessageAsync += Handlers.HandleAsync;
                }
            }
            """;

        await VerifyNoneAsync(source);
    }

    [Fact]
    public async Task ThreadSafeCustomSingleton_CapturedInHandler_NoDiagnostic()
    {
        var source = ServiceBusUsing + BaseUsings + ServiceBusStubs + EfCoreStubs + """
            public class MetricsCollector
            {
                public void Increment(string name) { }
            }

            public class Worker
            {
                private readonly MetricsCollector _metrics;

                public Worker(MetricsCollector metrics)
                {
                    _metrics = metrics;
                }

                public void Start(ServiceBusSessionProcessor processor)
                {
                    processor.ProcessMessageAsync += async args =>
                    {
                        _metrics.Increment("messages");
                        await Task.CompletedTask;
                    };
                }
            }
            """;

        await VerifyNoneAsync(source);
    }

    [Fact]
    public async Task DisposeOnlyReference_InHandler_NoDiagnostic()
    {
        var source = ServiceBusUsing + BaseUsings + ServiceBusStubs + EfCoreStubs + """
            public class Worker
            {
                private readonly AppDbContext _db;

                public Worker(AppDbContext db)
                {
                    _db = db;
                }

                public void Start(ServiceBusSessionProcessor processor)
                {
                    processor.ProcessMessageAsync += async args =>
                    {
                        _db.Dispose();
                        await Task.CompletedTask;
                    };
                }
            }
            """;

        await VerifyNoneAsync(source);
    }

    [Fact]
    public async Task TwoProcessorsInOneType_SequentialKnobDoesNotSuppressOtherSink()
    {
        // The first processor is provably sequential (its own options say MaxConcurrentCalls = 1);
        // the second uses the default and must still report DI022 — a sequential knob on a
        // different options instance in the same type is not proof for this sink.
        var source = ServiceBusUsing + BaseUsings + ServiceBusStubs + EfCoreStubs + """
            public class Worker
            {
                private readonly AppDbContext _db;

                public Worker(AppDbContext db)
                {
                    _db = db;
                }

                public void Start(ServiceBusClient client)
                {
                    var sequential = client.CreateProcessor("queue-a", new ServiceBusProcessorOptions
                    {
                        MaxConcurrentCalls = 1
                    });
                    sequential.ProcessMessageAsync += async args =>
                    {
                        _db.Add(args);
                        await _db.SaveChangesAsync();
                    };

                    var defaulted = client.CreateProcessor("queue-b");
                    defaulted.ProcessMessageAsync += async args =>
                    {
                        {|DI022:_db|}.Add(args);
                        await _db.SaveChangesAsync();
                    };
                }
            }
            """;

        await VerifyAsync(source);
    }

    [Fact]
    public async Task TimersTimer_SynchronizingObjectAssignedNull_StillReports()
    {
        var source = BaseUsings + EfCoreStubs + """
            public class Poller
            {
                private readonly AppDbContext _db;

                public Poller(AppDbContext db)
                {
                    _db = db;
                }

                public void Start()
                {
                    var timer = new System.Timers.Timer(5000);
                    timer.SynchronizingObject = null;
                    timer.Elapsed += (sender, args) =>
                    {
                        {|DI021:_db|}.Add(args);
                        _db.SaveChanges();
                    };
                    timer.Start();
                }
            }
            """;

        await VerifyAsync(source);
    }

    [Fact]
    public async Task ServiceBusProcessor_TargetTypedOptionsSequential_NoDiagnostic()
    {
        var source = ServiceBusUsing + BaseUsings + ServiceBusStubs + EfCoreStubs + """
            public class Worker
            {
                private readonly AppDbContext _db;

                public Worker(AppDbContext db)
                {
                    _db = db;
                }

                public void Start(ServiceBusClient client)
                {
                    ServiceBusProcessorOptions options = new() { MaxConcurrentCalls = 1 };
                    var processor = client.CreateProcessor("queue", options);
                    processor.ProcessMessageAsync += async args =>
                    {
                        _db.Add(args);
                        await _db.SaveChangesAsync();
                    };
                }
            }
            """;

        await VerifyNoneAsync(source);
    }

    [Fact]
    public async Task SemaphoreGuard_UseBeforeWait_StillReports()
    {
        // The semaphore bracket only protects uses inside the guarded try region; the access
        // before WaitAsync is still concurrent.
        var source = ServiceBusUsing + BaseUsings + ServiceBusStubs + EfCoreStubs + """
            public class Worker
            {
                private readonly AppDbContext _db;
                private readonly SemaphoreSlim _gate = new SemaphoreSlim(1, 1);

                public Worker(AppDbContext db)
                {
                    _db = db;
                }

                public void Start(ServiceBusSessionProcessor processor)
                {
                    processor.ProcessMessageAsync += async args =>
                    {
                        {|DI021:_db|}.Add(args);
                        await _gate.WaitAsync();
                        try
                        {
                            await _db.SaveChangesAsync();
                        }
                        finally
                        {
                            _gate.Release();
                        }
                    };
                }
            }
            """;

        await VerifyAsync(source);
    }

    [Fact]
    public async Task ThreadingTimer_InfiniteDueTimeNeverStarted_NoDiagnostic()
    {
        // Timeout.Infinite due time creates the timer disabled; without a Change(...) on this
        // timer it never fires, finite period or not.
        var source = BaseUsings + EfCoreStubs + """
            public class Dormant
            {
                private readonly AppDbContext _db;
                private Timer _timer;

                public Dormant(AppDbContext db)
                {
                    _db = db;
                    _timer = new Timer(Run, null, Timeout.Infinite, 5000);
                }

                private void Run(object state)
                {
                    _db.Add(state);
                    _db.SaveChanges();
                }
            }
            """;

        await VerifyNoneAsync(source);
    }

    [Fact]
    public async Task ThreadingTimer_ChangeOnDifferentTimer_DormantNotReported()
    {
        // The finite Change(...) starts a different timer; the dormant single-arg timer
        // capturing the DbContext never fires.
        var source = BaseUsings + EfCoreStubs + """
            public class TwoTimers
            {
                private readonly AppDbContext _db;
                private Timer _dormant;
                private Timer _active;

                public TwoTimers(AppDbContext db)
                {
                    _db = db;
                    _dormant = new Timer(UseDb);
                    _active = new Timer(Heartbeat);
                    _active.Change(0, 1000);
                }

                private void UseDb(object state)
                {
                    _db.Add(state);
                    _db.SaveChanges();
                }

                private void Heartbeat(object state)
                {
                    System.Console.WriteLine("tick");
                }
            }
            """;

        await VerifyNoneAsync(source);
    }

    [Fact]
    public async Task ThreadingTimer_ZeroPeriod_OneShot_NoDiagnostic()
    {
        var source = BaseUsings + EfCoreStubs + """
            public class OneShot
            {
                private readonly AppDbContext _db;
                private Timer _timer;

                public OneShot(AppDbContext db)
                {
                    _db = db;
                }

                public void Schedule()
                {
                    _timer = new Timer(Run, null, 0, 0);
                }

                private void Run(object state)
                {
                    _db.Add(state);
                    _db.SaveChanges();
                }
            }
            """;

        await VerifyNoneAsync(source);
    }

    [Fact]
    public async Task TimerRearmGuard_StopsDifferentTimer_StillReports()
    {
        // Stopping some other timer does not serialize this handler's own invocations.
        var source = BaseUsings + EfCoreStubs + """
            public class Poller
            {
                private readonly AppDbContext _db;
                private Timer _timer;
                private Timer _other;

                public Poller(AppDbContext db)
                {
                    _db = db;
                }

                public void Start()
                {
                    _other = new Timer(Noop, null, 0, 1000);
                    _timer = new Timer(Poll, null, 0, 1000);
                }

                private void Noop(object state)
                {
                }

                private void Poll(object state)
                {
                    _other.Change(Timeout.Infinite, Timeout.Infinite);
                    {|DI021:_db|}.Add(state);
                    _db.SaveChanges();
                }
            }
            """;

        await VerifyAsync(source);
    }

    [Fact]
    public async Task TimerRearmGuard_StopAfterUse_StillReports()
    {
        // The shared-state access happens before the handler stops its own timer, so
        // overlapping invocations can still race on it.
        var source = BaseUsings + EfCoreStubs + """
            public class Poller
            {
                private readonly AppDbContext _db;
                private Timer _timer;

                public Poller(AppDbContext db)
                {
                    _db = db;
                }

                public void Start()
                {
                    _timer = new Timer(Poll, null, 0, 1000);
                }

                private void Poll(object state)
                {
                    {|DI021:_db|}.Add(state);
                    _timer.Change(Timeout.Infinite, Timeout.Infinite);
                    _db.SaveChanges();
                    _timer.Change(1000, 1000);
                }
            }
            """;

        await VerifyAsync(source);
    }

    [Fact]
    public async Task LockOnPerInvocationObject_StillReports()
    {
        // Each invocation creates its own monitor object, so the lock serializes nothing.
        var source = BaseUsings + EfCoreStubs + """
            public class Importer
            {
                private readonly AppDbContext _db;

                public Importer(AppDbContext db)
                {
                    _db = db;
                }

                public void Import(string[] rows)
                {
                    Parallel.ForEach(rows, row =>
                    {
                        var gate = new object();
                        lock (gate)
                        {
                            {|DI021:_db|}.Add(row);
                            _db.SaveChanges();
                        }
                    });
                }
            }
            """;

        await VerifyAsync(source);
    }

    [Fact]
    public async Task AsyncLockGuard_UseOutsideUsingRegion_StillReports()
    {
        // The disposable async-lock only guards the using region; the access after it races.
        var source = ServiceBusUsing + BaseUsings + ServiceBusStubs + EfCoreStubs + """
            public class AsyncLock
            {
                public Task<System.IDisposable> LockAsync() => Task.FromResult<System.IDisposable>(null);
            }

            public class Worker
            {
                private readonly AppDbContext _db;
                private readonly AsyncLock _mutex = new AsyncLock();

                public Worker(AppDbContext db)
                {
                    _db = db;
                }

                public void Start(ServiceBusSessionProcessor processor)
                {
                    processor.ProcessMessageAsync += async args =>
                    {
                        using (await _mutex.LockAsync())
                        {
                            _db.Add(args);
                        }

                        await {|DI021:_db|}.SaveChangesAsync();
                    };
                }
            }
            """;

        await VerifyAsync(source);
    }

    [Fact]
    public async Task EventProcessorClient_ProcessorNamespaceVariant_ReportsDiagnostic()
    {
        var source = BaseUsings + EfCoreStubs + """
            namespace Azure.Messaging.EventHubs.Processor
            {
                public class EventProcessorClient
                {
                    public event System.Func<ProcessEventArgs, System.Threading.Tasks.Task> ProcessEventAsync;
                }

                public class ProcessEventArgs { }
            }

            public class TelemetryIngester
            {
                private readonly AppDbContext _db;

                public TelemetryIngester(AppDbContext db)
                {
                    _db = db;
                }

                public void Start(Azure.Messaging.EventHubs.Processor.EventProcessorClient client)
                {
                    client.ProcessEventAsync += async args =>
                    {
                        {|DI021:_db|}.Add(args);
                        await _db.SaveChangesAsync();
                    };
                }
            }
            """;

        await VerifyAsync(source);
    }

    [Fact]
    public async Task PerInvocationSemaphore_StillReports()
    {
        // Each invocation creates its own semaphore, so the bracket serializes nothing.
        var source = ServiceBusUsing + BaseUsings + ServiceBusStubs + EfCoreStubs + """
            public class Worker
            {
                private readonly AppDbContext _db;

                public Worker(AppDbContext db)
                {
                    _db = db;
                }

                public void Start(ServiceBusSessionProcessor processor)
                {
                    processor.ProcessMessageAsync += async args =>
                    {
                        var gate = new SemaphoreSlim(1, 1);
                        await gate.WaitAsync();
                        try
                        {
                            {|DI021:_db|}.Add(args);
                            await _db.SaveChangesAsync();
                        }
                        finally
                        {
                            gate.Release();
                        }
                    };
                }
            }
            """;

        await VerifyAsync(source);
    }

    [Fact]
    public async Task InterlockedGuard_UseBeforeGuard_StillReports()
    {
        // The shared access before the reentrancy check is not protected by it.
        var source = BaseUsings + EfCoreStubs + """
            public class Poller
            {
                private readonly AppDbContext _db;
                private int _running;
                private Timer _timer;

                public Poller(AppDbContext db)
                {
                    _db = db;
                }

                public void Start()
                {
                    _timer = new Timer(Poll, null, 0, 1000);
                }

                private void Poll(object state)
                {
                    {|DI021:_db|}.Add(state);
                    if (Interlocked.CompareExchange(ref _running, 1, 0) != 0)
                    {
                        return;
                    }

                    try
                    {
                        _db.SaveChanges();
                    }
                    finally
                    {
                        Interlocked.Exchange(ref _running, 0);
                    }
                }
            }
            """;

        await VerifyAsync(source);
    }

    [Fact]
    public async Task TimersTimer_AutoResetFalseThenTrue_StillReports()
    {
        // The later AutoReset = true re-enables overlapping callbacks; one safe write is not proof.
        var source = BaseUsings + EfCoreStubs + """
            public class Poller
            {
                private readonly AppDbContext _db;

                public Poller(AppDbContext db)
                {
                    _db = db;
                }

                public void Start()
                {
                    var timer = new System.Timers.Timer(5000);
                    timer.AutoReset = false;
                    timer.AutoReset = true;
                    timer.Elapsed += (sender, args) =>
                    {
                        {|DI021:_db|}.Add(args);
                        _db.SaveChanges();
                    };
                    timer.Start();
                }
            }
            """;

        await VerifyAsync(source);
    }

    [Fact]
    public async Task LocalFunctionHandler_CapturedField_ReportsDiagnostic()
    {
        var source = ServiceBusUsing + BaseUsings + ServiceBusStubs + EfCoreStubs + """
            public class Worker
            {
                private readonly AppDbContext _db;

                public Worker(AppDbContext db)
                {
                    _db = db;
                }

                public void Start(ServiceBusSessionProcessor processor)
                {
                    processor.ProcessMessageAsync += HandleAsync;

                    async Task HandleAsync(ProcessSessionMessageEventArgs args)
                    {
                        {|DI021:_db|}.Add(args);
                        await _db.SaveChangesAsync();
                    }
                }
            }
            """;

        await VerifyAsync(source);
    }

    [Fact]
    public async Task PeriodicTimerLoop_ScopePerTick_NoDiagnostic()
    {
        var source = HostingUsing + BaseUsings + EfCoreStubs + HostingStubs + """
            public class Worker : BackgroundService
            {
                private readonly IServiceScopeFactory _scopeFactory;

                public Worker(IServiceScopeFactory scopeFactory)
                {
                    _scopeFactory = scopeFactory;
                }

                protected override async Task ExecuteAsync(CancellationToken stoppingToken)
                {
                    using var timer = new PeriodicTimer(TimeSpan.FromSeconds(5));
                    while (await timer.WaitForNextTickAsync(stoppingToken))
                    {
                        await using var scope = _scopeFactory.CreateAsyncScope();
                        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
                        db.Add("tick");
                        await db.SaveChangesAsync(stoppingToken);
                    }
                }
            }
            """;

        await VerifyNoneAsync(source);
    }
}
