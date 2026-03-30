using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using Microsoft.CodeAnalysis;

namespace DependencyInjection.Lifetime.Analyzers.Infrastructure;

internal static class OrderedRegistrationOrdering
{
    public static ImmutableArray<OrderedRegistration> SortBySourceLocation(IEnumerable<OrderedRegistration> registrations) =>
        registrations
            .Select(registration =>
            {
                var lineSpan = registration.Location.GetLineSpan();
                var path = lineSpan.Path;

                if (string.IsNullOrWhiteSpace(path))
                    path = registration.Location.SourceTree?.FilePath ?? string.Empty;

                return new
                {
                    Registration = registration,
                    Path = path ?? string.Empty,
                    Line = lineSpan.StartLinePosition.Line,
                    Column = lineSpan.StartLinePosition.Character,
                    DiscoveryOrder = registration.Order
                };
            })
            .OrderBy(item => item.Path, StringComparer.OrdinalIgnoreCase)
            .ThenBy(item => item.Line)
            .ThenBy(item => item.Column)
            .ThenBy(item => item.DiscoveryOrder)
            .Select(item => item.Registration)
            .ToImmutableArray();
}
