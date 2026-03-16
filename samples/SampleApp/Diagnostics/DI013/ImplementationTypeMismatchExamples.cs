using Microsoft.Extensions.DependencyInjection;

namespace SampleApp.Diagnostics.DI013;

public interface IRepository { }
public class SqlRepository : IRepository { }
public class WrongType { }
public abstract class AbstractRepository : IRepository { }

public static class ImplementationTypeMismatchExamples
{
    public static void Register(IServiceCollection services)
    {
        // ✅ GOOD: Correct implementation type
        services.AddSingleton(typeof(IRepository), typeof(SqlRepository));

        // ⚠️ BAD: WrongType does not implement IRepository
        // This will throw an ArgumentException at runtime
        services.AddSingleton(typeof(IRepository), typeof(WrongType));
        
        // ⚠️ BAD: String does not implement IRepository
        services.AddScoped(typeof(IRepository), typeof(string));

        // ⚠️ BAD: Abstract types and interface self-registrations cannot be activated
        services.AddTransient(typeof(IRepository));
        services.AddSingleton(typeof(IRepository), typeof(AbstractRepository));
    }
}
