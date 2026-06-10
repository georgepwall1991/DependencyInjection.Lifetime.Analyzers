using System.Threading.Tasks;
using DependencyInjection.Lifetime.Analyzers;
using DependencyInjection.Lifetime.Analyzers.CodeFixes;
using DependencyInjection.Lifetime.Analyzers.Rules;
using DependencyInjection.Lifetime.Analyzers.Tests.Infrastructure;
using Microsoft.CodeAnalysis.Testing;
using Xunit;

namespace DependencyInjection.Lifetime.Analyzers.Tests.CodeFixes;

public class DI021_ScopePerInvocationCodeFixTests
{
    private const string EquivalenceKey = "DI021_ScopePerInvocation";

    private const string Usings = """
        using System;
        using System.Threading;
        using System.Threading.Tasks;
        using Microsoft.Extensions.DependencyInjection;

        """;

    private const string Stubs = """
        namespace Azure.Messaging.ServiceBus
        {
            public class ServiceBusProcessor
            {
                public event System.Func<ProcessMessageEventArgs, System.Threading.Tasks.Task> ProcessMessageAsync;
                public event System.Func<ProcessErrorEventArgs, System.Threading.Tasks.Task> ProcessErrorAsync;
            }

            public class ServiceBusSessionProcessor
            {
                public event System.Func<ProcessSessionMessageEventArgs, System.Threading.Tasks.Task> ProcessMessageAsync;
                public event System.Func<ProcessErrorEventArgs, System.Threading.Tasks.Task> ProcessErrorAsync;
            }

            public class ProcessMessageEventArgs { }
            public class ProcessSessionMessageEventArgs { }
            public class ProcessErrorEventArgs { }
        }

        namespace Microsoft.EntityFrameworkCore
        {
            public class DbContext : System.IDisposable
            {
                public int SaveChanges() => 0;
                public System.Threading.Tasks.Task<int> SaveChangesAsync(System.Threading.CancellationToken cancellationToken = default) => System.Threading.Tasks.Task.FromResult(0);
                public void Dispose() { }
            }
        }

        public class AppDbContext : Microsoft.EntityFrameworkCore.DbContext
        {
            public void Add(object entity) { }
        }

        """;

    private static Task VerifyFixAsync(string source, string fixedSource) =>
        CodeFixVerifier<DI021_ConcurrentHandlerSharedStateAnalyzer, DI021_ScopePerInvocationCodeFixProvider>
            .VerifyCodeFixAsync(source, new DiagnosticResult[0], fixedSource);

    [Fact]
    public async Task AsyncLambda_FieldCapture_PlumbsFactoryAndRemovesDeadField()
    {
        var source = "using Azure.Messaging.ServiceBus;\n" + Usings + Stubs + """
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
                        {|DI021:_db|}.Add(args);
                        await _db.SaveChangesAsync();
                    };
                }
            }
            """;

        var fixedSource = "using Azure.Messaging.ServiceBus;\n" + Usings + Stubs + """
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

        await VerifyFixAsync(source, fixedSource);
    }

    [Fact]
    public async Task SyncMethodGroupTimerHandler_ExistingFactory_ReusedAndFieldRemoved()
    {
        var source = Usings + Stubs + """
            public class Poller
            {
                private readonly IServiceScopeFactory _scopeFactory;
                private readonly AppDbContext _db;
                private Timer _timer;

                public Poller(IServiceScopeFactory scopeFactory, AppDbContext db)
                {
                    _scopeFactory = scopeFactory;
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

        var fixedSource = Usings + Stubs + """
            public class Poller
            {
                private readonly IServiceScopeFactory _scopeFactory;
                private Timer _timer;

                public Poller(IServiceScopeFactory scopeFactory)
                {
                    _scopeFactory = scopeFactory;
                }

                public void Start()
                {
                    _timer = new Timer(Poll, null, 0, 5000);
                }

                private void Poll(object state)
                {
                    using var scope = _scopeFactory.CreateScope();
                    var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
                    db.Add(state);
                    db.SaveChanges();
                }
            }
            """;

        await VerifyFixAsync(source, fixedSource);
    }

    [Fact]
    public async Task ExpressionBodiedTimerLambda_ConvertedToBlock()
    {
        var source = Usings + Stubs + """
            public class Poller
            {
                private readonly AppDbContext _db;

                public Poller(AppDbContext db)
                {
                    _db = db;
                }

                public void Start()
                {
                    var timer = new Timer(_ => {|DI021:_db|}.SaveChanges(), null, 0, 1000);
                }
            }
            """;

        var fixedSource = Usings + Stubs + """
            public class Poller
            {
                private readonly IServiceScopeFactory _scopeFactory;

                public Poller(IServiceScopeFactory scopeFactory)
                {
                    _scopeFactory = scopeFactory;
                }

                public void Start()
                {
                    var timer = new Timer(_ =>
                    {
                        using var scope = _scopeFactory.CreateScope();
                        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
                        db.SaveChanges();
                    }, null, 0, 1000);
                }
            }
            """;

        await VerifyFixAsync(source, fixedSource);
    }

    [Fact]
    public async Task OuterLocalCapture_ScopeInserted_LocalShadowAvoided()
    {
        var source = Usings + Stubs + """
            public class Importer
            {
                private readonly IServiceScopeFactory _scopeFactory;

                public Importer(IServiceScopeFactory scopeFactory)
                {
                    _scopeFactory = scopeFactory;
                }

                public void Import(string[] rows)
                {
                    var db = new AppDbContext();
                    Parallel.ForEach(rows, row =>
                    {
                        {|DI021:db|}.Add(row);
                        db.SaveChanges();
                    });
                }
            }
            """;

        var fixedSource = Usings + Stubs + """
            public class Importer
            {
                private readonly IServiceScopeFactory _scopeFactory;

                public Importer(IServiceScopeFactory scopeFactory)
                {
                    _scopeFactory = scopeFactory;
                }

                public void Import(string[] rows)
                {
                    var db = new AppDbContext();
                    Parallel.ForEach(rows, row =>
                    {
                        using var scope = _scopeFactory.CreateScope();
                        var db1 = scope.ServiceProvider.GetRequiredService<AppDbContext>();
                        db1.Add(row);
                        db1.SaveChanges();
                    });
                }
            }
            """;

        await VerifyFixAsync(source, fixedSource);
    }

    [Fact]
    public async Task FieldReferencedElsewhere_FieldKept()
    {
        var source = "using Azure.Messaging.ServiceBus;\n" + Usings + Stubs + """
            public class Worker
            {
                private readonly AppDbContext _db;

                public Worker(AppDbContext db)
                {
                    _db = db;
                }

                public void Seed()
                {
                    _db.SaveChanges();
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

        var fixedSource = "using Azure.Messaging.ServiceBus;\n" + Usings + Stubs + """
            public class Worker
            {
                private readonly IServiceScopeFactory _scopeFactory;
                private readonly AppDbContext _db;

                public Worker(AppDbContext db, IServiceScopeFactory scopeFactory)
                {
                    _db = db;
                    _scopeFactory = scopeFactory;
                }

                public void Seed()
                {
                    _db.SaveChanges();
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

        await VerifyFixAsync(source, fixedSource);
    }

    [Fact]
    public async Task DI022ConfigGatedDiagnostic_SameFixApplies()
    {
        var source = "using Azure.Messaging.ServiceBus;\n" + Usings + Stubs + """
            public class Worker
            {
                private readonly AppDbContext _db;

                public Worker(AppDbContext db)
                {
                    _db = db;
                }

                public void Start(ServiceBusProcessor processor)
                {
                    processor.ProcessMessageAsync += async args =>
                    {
                        {|DI022:_db|}.Add(args);
                        await _db.SaveChangesAsync();
                    };
                }
            }
            """;

        var fixedSource = "using Azure.Messaging.ServiceBus;\n" + Usings + Stubs + """
            public class Worker
            {
                private readonly IServiceScopeFactory _scopeFactory;

                public Worker(IServiceScopeFactory scopeFactory)
                {
                    _scopeFactory = scopeFactory;
                }

                public void Start(ServiceBusProcessor processor)
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

        await VerifyFixAsync(source, fixedSource);
    }

    [Fact]
    public async Task MissingDependencyInjectionUsing_Added()
    {
        var source = """
            using System;
            using System.Threading;

            """ + Stubs + """
            public class Poller
            {
                private readonly AppDbContext _db;

                public Poller(AppDbContext db)
                {
                    _db = db;
                }

                public void Start()
                {
                    var timer = new Timer(Poll, null, 0, 5000);
                }

                private void Poll(object state)
                {
                    {|DI021:_db|}.Add(state);
                    _db.SaveChanges();
                }
            }
            """;

        var fixedSource = """
            using System;
            using System.Threading;
            using Microsoft.Extensions.DependencyInjection;

            """ + "\n" + Stubs + """
            public class Poller
            {
                private readonly IServiceScopeFactory _scopeFactory;

                public Poller(IServiceScopeFactory scopeFactory)
                {
                    _scopeFactory = scopeFactory;
                }

                public void Start()
                {
                    var timer = new Timer(Poll, null, 0, 5000);
                }

                private void Poll(object state)
                {
                    using var scope = _scopeFactory.CreateScope();
                    var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
                    db.Add(state);
                    db.SaveChanges();
                }
            }
            """;

        await VerifyFixAsync(source, fixedSource);
    }

    [Fact]
    public async Task StaticHandlerWithStaticField_NoFixOffered()
    {
        var source = "using Azure.Messaging.ServiceBus;\n" + Usings + Stubs + """
            public class Worker
            {
                private static AppDbContext _shared = new AppDbContext();

                public void Start(ServiceBusSessionProcessor processor)
                {
                    processor.ProcessMessageAsync += HandleAsync;
                }

                private static async Task HandleAsync(ProcessSessionMessageEventArgs args)
                {
                    _shared.Add(args);
                    await _shared.SaveChangesAsync();
                }
            }
            """;

        await CodeFixVerifier<DI021_ConcurrentHandlerSharedStateAnalyzer, DI021_ScopePerInvocationCodeFixProvider>
            .VerifyCodeFixNotOfferedAsync(
                source,
                CodeFixVerifier<DI021_ConcurrentHandlerSharedStateAnalyzer, DI021_ScopePerInvocationCodeFixProvider>
                    .Diagnostic(DiagnosticDescriptors.ConcurrentHandlerSharedState),
                EquivalenceKey);
    }

    [Fact]
    public async Task CapturedScopeResolution_NoFixOffered()
    {
        var source = "using Azure.Messaging.ServiceBus;\n" + Usings + Stubs + """
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
                        var db = _scope.ServiceProvider.GetRequiredService<AppDbContext>();
                        db.Add(args);
                        await db.SaveChangesAsync();
                    };
                }
            }
            """;

        await CodeFixVerifier<DI021_ConcurrentHandlerSharedStateAnalyzer, DI021_ScopePerInvocationCodeFixProvider>
            .VerifyCodeFixNotOfferedAsync(
                source,
                CodeFixVerifier<DI021_ConcurrentHandlerSharedStateAnalyzer, DI021_ScopePerInvocationCodeFixProvider>
                    .Diagnostic(DiagnosticDescriptors.ConcurrentHandlerSharedState),
                EquivalenceKey);
    }

    [Fact]
    public async Task SameNamedTypeInOtherNamespace_PlumbingTargetsHandlersType()
    {
        // Two types share the simple name "Worker"; the fix must plumb the factory into the
        // declaration that contains the handler, not the first same-named type in the file.
        var source = Usings + Stubs + """
            namespace First
            {
                public class Worker
                {
                    public Worker(string name)
                    {
                    }
                }
            }

            namespace Second
            {
                public class Worker
                {
                    private readonly AppDbContext _db;

                    public Worker(AppDbContext db)
                    {
                        _db = db;
                    }

                    public void Start()
                    {
                        var timer = new System.Threading.Timer(Poll, null, 0, 5000);
                    }

                    private void Poll(object state)
                    {
                        {|DI021:_db|}.Add(state);
                        _db.SaveChanges();
                    }
                }
            }
            """;

        var fixedSource = Usings + Stubs + """
            namespace First
            {
                public class Worker
                {
                    public Worker(string name)
                    {
                    }
                }
            }

            namespace Second
            {
                public class Worker
                {
                    private readonly IServiceScopeFactory _scopeFactory;

                    public Worker(IServiceScopeFactory scopeFactory)
                    {
                        _scopeFactory = scopeFactory;
                    }

                    public void Start()
                    {
                        var timer = new System.Threading.Timer(Poll, null, 0, 5000);
                    }

                    private void Poll(object state)
                    {
                        using var scope = _scopeFactory.CreateScope();
                        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
                        db.Add(state);
                        db.SaveChanges();
                    }
                }
            }
            """;

        await VerifyFixAsync(source, fixedSource);
    }

    [Fact]
    public async Task ConstructorWithOptionalParameter_FactoryInsertedBeforeIt()
    {
        var source = Usings + Stubs + """
            public class Poller
            {
                private readonly AppDbContext _db;
                private Timer _timer;

                public Poller(AppDbContext db, string queue = "default")
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

        var fixedSource = Usings + Stubs + """
            public class Poller
            {
                private readonly IServiceScopeFactory _scopeFactory;
                private Timer _timer;

                public Poller(IServiceScopeFactory scopeFactory, string queue = "default")
                {
                    _scopeFactory = scopeFactory;
                }

                public void Start()
                {
                    _timer = new Timer(Poll, null, 0, 5000);
                }

                private void Poll(object state)
                {
                    using var scope = _scopeFactory.CreateScope();
                    var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
                    db.Add(state);
                    db.SaveChanges();
                }
            }
            """;

        await VerifyFixAsync(source, fixedSource);
    }

    [Fact]
    public async Task NonAsyncTaskReturningHandler_NoFixOffered()
    {
        // A synchronous using-scope would dispose before the returned task completes.
        var source = "using Azure.Messaging.ServiceBus;\n" + Usings + Stubs + """
            public class Worker
            {
                private readonly AppDbContext _db;

                public Worker(AppDbContext db)
                {
                    _db = db;
                }

                public void Start(ServiceBusSessionProcessor processor)
                {
                    processor.ProcessMessageAsync += args =>
                    {
                        _db.Add(args);
                        return _db.SaveChangesAsync();
                    };
                }
            }
            """;

        await CodeFixVerifier<DI021_ConcurrentHandlerSharedStateAnalyzer, DI021_ScopePerInvocationCodeFixProvider>
            .VerifyCodeFixNotOfferedAsync(
                source,
                CodeFixVerifier<DI021_ConcurrentHandlerSharedStateAnalyzer, DI021_ScopePerInvocationCodeFixProvider>
                    .Diagnostic(DiagnosticDescriptors.ConcurrentHandlerSharedState),
                EquivalenceKey);
    }

    [Fact]
    public async Task MultipleConstructors_NoFixOffered()
    {
        // Plumbing only one of several constructors would leave the other construction path
        // with a null scope factory at runtime.
        var source = "using Azure.Messaging.ServiceBus;\n" + Usings + Stubs + """
            public class Worker
            {
                private readonly AppDbContext _db;

                public Worker(AppDbContext db)
                {
                    _db = db;
                }

                public Worker() : this(new AppDbContext())
                {
                }

                public void Start(ServiceBusSessionProcessor processor)
                {
                    processor.ProcessMessageAsync += async args =>
                    {
                        _db.Add(args);
                        await _db.SaveChangesAsync();
                    };
                }
            }
            """;

        await CodeFixVerifier<DI021_ConcurrentHandlerSharedStateAnalyzer, DI021_ScopePerInvocationCodeFixProvider>
            .VerifyCodeFixNotOfferedAsync(
                source,
                CodeFixVerifier<DI021_ConcurrentHandlerSharedStateAnalyzer, DI021_ScopePerInvocationCodeFixProvider>
                    .Diagnostic(DiagnosticDescriptors.ConcurrentHandlerSharedState),
                EquivalenceKey);
    }

    [Fact]
    public async Task ExpressionBodiedConstructor_NoFixOffered()
    {
        // The constructor cannot receive the _scopeFactory assignment without a block body.
        var source = "using Azure.Messaging.ServiceBus;\n" + Usings + Stubs + """
            public class Worker
            {
                private readonly AppDbContext _db;

                public Worker(AppDbContext db) => _db = db;

                public void Start(ServiceBusSessionProcessor processor)
                {
                    processor.ProcessMessageAsync += async args =>
                    {
                        _db.Add(args);
                        await _db.SaveChangesAsync();
                    };
                }
            }
            """;

        await CodeFixVerifier<DI021_ConcurrentHandlerSharedStateAnalyzer, DI021_ScopePerInvocationCodeFixProvider>
            .VerifyCodeFixNotOfferedAsync(
                source,
                CodeFixVerifier<DI021_ConcurrentHandlerSharedStateAnalyzer, DI021_ScopePerInvocationCodeFixProvider>
                    .Diagnostic(DiagnosticDescriptors.ConcurrentHandlerSharedState),
                EquivalenceKey);
    }

    [Fact]
    public async Task LocalFunctionHandler_ScopeInsertedInLocalFunction()
    {
        var source = Usings + Stubs + """
            public class Poller
            {
                private readonly IServiceScopeFactory _scopeFactory;
                private readonly AppDbContext _db;
                private Timer _timer;

                public Poller(IServiceScopeFactory scopeFactory, AppDbContext db)
                {
                    _scopeFactory = scopeFactory;
                    _db = db;
                }

                public void Start()
                {
                    _timer = new Timer(Poll, null, 0, 5000);

                    void Poll(object state)
                    {
                        {|DI021:_db|}.Add(state);
                        _db.SaveChanges();
                    }
                }
            }
            """;

        var fixedSource = Usings + Stubs + """
            public class Poller
            {
                private readonly IServiceScopeFactory _scopeFactory;
                private Timer _timer;

                public Poller(IServiceScopeFactory scopeFactory)
                {
                    _scopeFactory = scopeFactory;
                }

                public void Start()
                {
                    _timer = new Timer(Poll, null, 0, 5000);

                    void Poll(object state)
                    {
                        using var scope = _scopeFactory.CreateScope();
                        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
                        db.Add(state);
                        db.SaveChanges();
                    }
                }
            }
            """;

        await VerifyFixAsync(source, fixedSource);
    }

    [Fact]
    public async Task NoDeclaredConstructor_NoFixOffered()
    {
        var source = "using Azure.Messaging.ServiceBus;\n" + Usings + Stubs + """
            public class Worker
            {
                private AppDbContext _db = new AppDbContext();

                public void Start(ServiceBusSessionProcessor processor)
                {
                    processor.ProcessMessageAsync += async args =>
                    {
                        _db.Add(args);
                        await _db.SaveChangesAsync();
                    };
                }
            }
            """;

        await CodeFixVerifier<DI021_ConcurrentHandlerSharedStateAnalyzer, DI021_ScopePerInvocationCodeFixProvider>
            .VerifyCodeFixNotOfferedAsync(
                source,
                CodeFixVerifier<DI021_ConcurrentHandlerSharedStateAnalyzer, DI021_ScopePerInvocationCodeFixProvider>
                    .Diagnostic(DiagnosticDescriptors.ConcurrentHandlerSharedState),
                EquivalenceKey);
    }
}
