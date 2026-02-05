# DI015 In Action Sample

This sample demonstrates `DI015` (Unresolvable Dependency) with both:

- A broken setup that compiles but fails at runtime.
- A fixed setup that resolves successfully.

## Build (see analyzer warnings)

```bash
dotnet build samples/DI015InAction/DI015InAction.csproj
```

Expected `DI015` warnings come from the intentionally broken registrations in `Program.cs`.

## Run (see runtime behavior)

```bash
dotnet run --project samples/DI015InAction/DI015InAction.csproj
```

If you only have .NET 9/10 runtime installed locally, run with:

```bash
DOTNET_ROLL_FORWARD=Major dotnet run --project samples/DI015InAction/DI015InAction.csproj
```

You will see:

- Broken configuration: service resolution fails with `InvalidOperationException`.
- Fixed configuration: services resolve and execute normally.
