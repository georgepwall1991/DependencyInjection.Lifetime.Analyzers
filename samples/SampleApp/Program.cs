using Microsoft.Extensions.DependencyInjection;

// Build the service provider
var services = new ServiceCollection();

var serviceProvider = services.BuildServiceProvider();

Console.WriteLine("╔══════════════════════════════════════════════════════════════════╗");
Console.WriteLine("║  DI Lifetime Analyzer - Sample Application                       ║");
Console.WriteLine("╠══════════════════════════════════════════════════════════════════╣");
Console.WriteLine("║  Build this project to see analyzer warnings in the Error List  ║");
Console.WriteLine("║  or in the IDE inline with the code.                            ║");
Console.WriteLine("╚══════════════════════════════════════════════════════════════════╝");
Console.WriteLine();
Console.WriteLine("Expected warnings in Diagnostics/ folder:");
Console.WriteLine("  - DI001/: Scope not disposed");
Console.WriteLine("  - DI002/: Service escapes scope (return and field)");
Console.WriteLine("  - DI003/: Captive dependencies (3 warnings)");
Console.WriteLine("  - DI004/: Use after dispose");
Console.WriteLine("  - DI005/: CreateScope in async method");
Console.WriteLine("  - DI006/: Static provider cache (4 warnings)");
Console.WriteLine("  - DI007/: Service locator anti-pattern");
Console.WriteLine("  - DI008/: Disposable transient service");
Console.WriteLine("  - DI009/: Open generic captive dependency");
Console.WriteLine("  - DI010/: Constructor over-injection");
Console.WriteLine("  - DI011/: ServiceProvider injection");
Console.WriteLine("  - DI012/: Conditional registration misuse");
Console.WriteLine("  - DI013/: Implementation type mismatch");
Console.WriteLine("  - DI014/: Root provider not disposed");
Console.WriteLine("  - DI015/: Unresolvable dependency");
Console.WriteLine("  - DI016/: BuildServiceProvider misuse during registration");
Console.WriteLine();
Console.WriteLine("Run 'dotnet build' to see all warnings.");
