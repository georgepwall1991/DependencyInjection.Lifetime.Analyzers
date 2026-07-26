using System;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;

namespace Microsoft.AspNetCore.Http
{
    // HttpContext itself is declared alongside the DI020 samples; only the accessor contract is
    // missing, and the analyzer resolves both by metadata name exactly as it resolves the real ones.
    public interface IHttpContextAccessor
    {
        HttpContext? HttpContext { get; set; }
    }
}

namespace SampleApp.Diagnostics.DI034
{
    public sealed class Bad_AuditFromBackgroundWork
    {
        public void Handle(HttpContext context)
        {
            // DI034: the response completes and the context is recycled while this still runs.
            _ = Task.Run(() => Console.WriteLine(context.GetHashCode()));
        }
    }

    public sealed class Good_CopyValuesBeforeBackgroundWork
    {
        public void Handle(HttpContext context)
        {
            var traceId = context.GetHashCode();
            _ = Task.Run(() => Console.WriteLine(traceId));
        }
    }
}
