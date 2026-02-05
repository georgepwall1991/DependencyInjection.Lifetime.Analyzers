# Contributing to DependencyInjection.Lifetime.Analyzers

Thank you for your interest in contributing! This document provides guidelines for contributing to the project.

## Getting Started

### Prerequisites

- .NET 10.0 SDK (the repository includes a `global.json` pin)
- Visual Studio 2022, JetBrains Rider, or VS Code with C# extension

### Building

```bash
git clone https://github.com/georgepwall1991/DependencyInjection.Lifetime.Analyzers.git
cd DependencyInjection.Lifetime.Analyzers
dotnet build
```

### Running Tests

```bash
dotnet test
```

## Project Structure

```
├── src/
│   └── DependencyInjection.Lifetime.Analyzers/
│       ├── Rules/              # Analyzer implementations
│       ├── CodeFixes/          # Code fix providers
│       ├── Infrastructure/     # Shared utilities
│       └── DiagnosticIds.cs    # Diagnostic ID constants
├── tests/
│   └── DependencyInjection.Lifetime.Analyzers.Tests/
│       ├── Rules/              # Analyzer tests
│       ├── CodeFixes/          # Code fix tests
│       └── Infrastructure/     # Test utilities
└── samples/
    └── SampleApp/              # Example diagnostics
```

## Adding a New Analyzer

1. **Add Diagnostic ID** in `DiagnosticIds.cs`:
   ```csharp
   public const string MyNewRule = "DI0XX";
   ```

2. **Add Diagnostic Descriptor** in `DiagnosticDescriptors.cs`:
   ```csharp
   public static readonly DiagnosticDescriptor MyNewRule = new(
       id: DiagnosticIds.MyNewRule,
       title: "My new rule title",
       messageFormat: "Message format with {0} placeholders",
       category: Category,
       defaultSeverity: DiagnosticSeverity.Warning,
       isEnabledByDefault: true,
       description: "Detailed description.");
   ```

3. **Create Analyzer** in `Rules/DI0XX_MyNewRuleAnalyzer.cs`:
   - Inherit from `DiagnosticAnalyzer`
   - Implement `SupportedDiagnostics` and `Initialize`
   - Add `[DiagnosticAnalyzer(LanguageNames.CSharp)]` attribute

4. **Create Tests** in `Tests/Rules/DI0XX_MyNewRuleAnalyzerTests.cs`:
   - Test positive cases (should report diagnostic)
   - Test negative cases (should NOT report diagnostic)

5. **Update Release Tracking** in `AnalyzerReleases.Unshipped.md`:
   ```markdown
   DI0XX | DependencyInjection | Warning | My new rule description
   ```

6. **Update README.md** with documentation for the new rule

7. **If adding a code fix**, create it in `CodeFixes/` with corresponding tests

## Code Style

- Follow existing code patterns and naming conventions
- Use XML documentation on all public members
- Keep analyzers focused and single-purpose
- Use the shared `WellKnownTypes` infrastructure for type checking

## Testing Guidelines

- Write tests for both positive (reports diagnostic) and negative (no diagnostic) cases
- Use the `AnalyzerVerifier<T>` and `CodeFixVerifier<T, U>` helpers
- Test edge cases and common patterns
- Ensure code fixes produce valid, compilable code

## Pull Request Process

1. Fork the repository and create a feature branch
2. Make your changes with clear, descriptive commits
3. Ensure all tests pass: `dotnet test`
4. Update documentation as needed
5. Submit a pull request with:
   - Clear description of the change
   - Link to any related issues
   - Test coverage for new functionality

## Reporting Issues

When reporting issues, please include:

- .NET version and IDE being used
- Minimal code sample that reproduces the issue
- Expected vs actual behavior
- Any relevant error messages

## Questions?

Feel free to open an issue for questions or discussions about potential features.
