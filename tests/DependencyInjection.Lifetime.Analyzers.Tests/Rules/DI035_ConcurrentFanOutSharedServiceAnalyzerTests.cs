using System.Threading.Tasks;
using DependencyInjection.Lifetime.Analyzers.Rules;
using DependencyInjection.Lifetime.Analyzers.Tests.Infrastructure;
using Xunit;

namespace DependencyInjection.Lifetime.Analyzers.Tests.Rules;

public class DI035_ConcurrentFanOutSharedServiceAnalyzerTests
{
    private const string Usings = """
        using System;
        using System.Collections.Generic;
        using System.Linq;
        using System.Threading.Tasks;
        using Microsoft.EntityFrameworkCore;
        using Microsoft.Extensions.DependencyInjection;

        namespace Microsoft.EntityFrameworkCore
        {
            public class DbContext
            {
                public Task SaveChangesAsync() => Task.CompletedTask;
                public Task<int> CountAsync(int id) => Task.FromResult(id);
            }
        }

        public class AppDbContext : DbContext { }

        """;

    [Fact]
    public async Task SharedDbContextAcrossWhenAll_ReportsDiagnostic()
    {
        var source =
            Usings
            + """
                public class OrderProcessor
                {
                    private readonly AppDbContext _db;

                    public OrderProcessor(AppDbContext db)
                    {
                        _db = db;
                    }

                    public async Task ProcessAsync(IEnumerable<int> orderIds)
                    {
                        await Task.WhenAll(orderIds.Select(id => {|DI035:_db|}.CountAsync(id)));
                    }
                }
                """;

        await AnalyzerVerifier<DI035_ConcurrentFanOutSharedServiceAnalyzer>.VerifyDiagnosticsAsync(
            source
        );
    }

    [Fact]
    public async Task SharedDbContextLocalAcrossWhenAll_ReportsDiagnostic()
    {
        var source =
            Usings
            + """
                public class OrderProcessor
                {
                    public async Task ProcessAsync(IEnumerable<int> orderIds, AppDbContext db)
                    {
                        await Task.WhenAll(orderIds.Select(async id =>
                        {
                            await {|DI035:db|}.CountAsync(id);
                        }));
                    }
                }
                """;

        await AnalyzerVerifier<DI035_ConcurrentFanOutSharedServiceAnalyzer>.VerifyDiagnosticsAsync(
            source
        );
    }

    [Fact]
    public async Task ScopePerTask_NoDiagnostic()
    {
        // The documented fix: each task owns its own scope and its own context.
        var source =
            Usings
            + """
                public class OrderProcessor
                {
                    private readonly IServiceScopeFactory _scopeFactory;

                    public OrderProcessor(IServiceScopeFactory scopeFactory)
                    {
                        _scopeFactory = scopeFactory;
                    }

                    public async Task ProcessAsync(IEnumerable<int> orderIds)
                    {
                        await Task.WhenAll(orderIds.Select(async id =>
                        {
                            using var scope = _scopeFactory.CreateScope();
                            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
                            await db.CountAsync(id);
                        }));
                    }
                }
                """;

        await AnalyzerVerifier<DI035_ConcurrentFanOutSharedServiceAnalyzer>.VerifyNoDiagnosticsAsync(
            source
        );
    }

    [Fact]
    public async Task ThreadSafeServiceAcrossWhenAll_NoDiagnostic()
    {
        var source =
            Usings
            + """
                public interface ICalculator
                {
                    Task<int> ComputeAsync(int value);
                }

                public class Processor
                {
                    private readonly ICalculator _calculator;

                    public Processor(ICalculator calculator)
                    {
                        _calculator = calculator;
                    }

                    public async Task ProcessAsync(IEnumerable<int> values)
                    {
                        await Task.WhenAll(values.Select(v => _calculator.ComputeAsync(v)));
                    }
                }
                """;

        await AnalyzerVerifier<DI035_ConcurrentFanOutSharedServiceAnalyzer>.VerifyNoDiagnosticsAsync(
            source
        );
    }

    [Fact]
    public async Task SequentialForeach_NoDiagnostic()
    {
        // Awaiting each in turn means only one operation is ever in flight.
        var source =
            Usings
            + """
                public class OrderProcessor
                {
                    private readonly AppDbContext _db;

                    public OrderProcessor(AppDbContext db)
                    {
                        _db = db;
                    }

                    public async Task ProcessAsync(IEnumerable<int> orderIds)
                    {
                        foreach (var id in orderIds)
                        {
                            await _db.CountAsync(id);
                        }
                    }
                }
                """;

        await AnalyzerVerifier<DI035_ConcurrentFanOutSharedServiceAnalyzer>.VerifyNoDiagnosticsAsync(
            source
        );
    }

    [Fact]
    public async Task NameOfSharedContext_NoDiagnostic()
    {
        var source =
            Usings
            + """
                public class OrderProcessor
                {
                    private readonly AppDbContext _db;

                    public OrderProcessor(AppDbContext db)
                    {
                        _db = db;
                    }

                    public async Task ProcessAsync(IEnumerable<int> orderIds)
                    {
                        await Task.WhenAll(orderIds.Select(id => Task.FromResult(nameof(_db).Length)));
                    }
                }
                """;

        await AnalyzerVerifier<DI035_ConcurrentFanOutSharedServiceAnalyzer>.VerifyNoDiagnosticsAsync(
            source
        );
    }

    [Fact]
    public async Task ContextUsedOnlyInWherePredicate_NoDiagnostic()
    {
        // A Where predicate runs during enumeration, one element at a time.
        var source =
            Usings
            + """
                public class OrderProcessor
                {
                    private readonly AppDbContext _db;

                    public OrderProcessor(AppDbContext db)
                    {
                        _db = db;
                    }

                    private bool IsUsable(AppDbContext db) => true;

                    public async Task ProcessAsync(IEnumerable<int> orderIds)
                    {
                        await Task.WhenAll(
                            orderIds.Where(id => IsUsable(_db)).Select(id => Task.CompletedTask));
                    }
                }
                """;

        await AnalyzerVerifier<DI035_ConcurrentFanOutSharedServiceAnalyzer>.VerifyNoDiagnosticsAsync(
            source
        );
    }

    [Fact]
    public async Task ContextCreatedPerTaskAndUsedByNestedLambda_NoDiagnostic()
    {
        // The context belongs to one task; the nested lambda is part of that same task.
        var source =
            Usings
            + """
                public class OrderProcessor
                {
                    public async Task ProcessAsync(IEnumerable<int> orderIds)
                    {
                        await Task.WhenAll(orderIds.Select(async id =>
                        {
                            var db = new AppDbContext();
                            var ids = new[] { id };
                            foreach (var count in ids.Select(x => db.CountAsync(x)))
                            {
                                await count;
                            }
                        }));
                    }
                }
                """;

        await AnalyzerVerifier<DI035_ConcurrentFanOutSharedServiceAnalyzer>.VerifyNoDiagnosticsAsync(
            source
        );
    }

    [Fact]
    public async Task ContextCreatedPerGroupAndSharedByFlattenedTasks_ReportsDiagnostic()
    {
        var source =
            Usings
            + """
                public class OrderProcessor
                {
                    public async Task ProcessAsync(IEnumerable<IEnumerable<int>> orderGroups)
                    {
                        await Task.WhenAll(orderGroups.SelectMany(group =>
                        {
                            var db = new AppDbContext();
                            return group.Select(id => {|DI035:db|}.CountAsync(id));
                        }));
                    }
                }
                """;

        await AnalyzerVerifier<DI035_ConcurrentFanOutSharedServiceAnalyzer>.VerifyDiagnosticsAsync(
            source
        );
    }

    [Fact]
    public async Task NestedSelectorReturnedOnlyFromUncalledLocalFunction_NoDiagnostic()
    {
        var source =
            Usings
            + """
                public class OrderProcessor
                {
                    public async Task ProcessAsync(IEnumerable<IEnumerable<int>> orderGroups)
                    {
                        await Task.WhenAll(orderGroups.SelectMany(group =>
                        {
                            var db = new AppDbContext();

                            IEnumerable<Task<int>> Build()
                            {
                                return group.Select(id => db.CountAsync(id));
                            }

                            return Array.Empty<Task<int>>();
                        }));
                    }
                }
                """;

        await AnalyzerVerifier<DI035_ConcurrentFanOutSharedServiceAnalyzer>.VerifyNoDiagnosticsAsync(
            source
        );
    }

    [Fact]
    public async Task ContextFromComputedProperty_NoDiagnostic()
    {
        // A computed property can hand back a fresh instance per access.
        var source =
            Usings
            + """
                public class OrderProcessor
                {
                    private AppDbContext Db => new AppDbContext();

                    public async Task ProcessAsync(IEnumerable<int> orderIds)
                    {
                        await Task.WhenAll(orderIds.Select(id => Db.CountAsync(id)));
                    }
                }
                """;

        await AnalyzerVerifier<DI035_ConcurrentFanOutSharedServiceAnalyzer>.VerifyNoDiagnosticsAsync(
            source
        );
    }
}
