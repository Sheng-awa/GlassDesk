# A19: precompiled 0.2.0 SDK plugin

`LegacySdk/Archived` contains byte-identical copies of the three contract files from
`artifacts/release/0.2.0/GlassDesk-0.2.0-source.zip`. `source-origin.json` records
the protected archive and per-entry SHA-256 values. The original ZIP is read only.

`LegacySdk` builds an independent assembly named `GlassDesk.Core`, version
`0.2.0.0`, with the old contract and without `CompanionVisualState.AppearanceId`.
`LegacyPlugin` references that project exclusively. It never references the current
`src/GlassDesk.Core` project. Its six/eight-argument state constructors,
five/seven-argument scene constructors, records, contributions and context calls
therefore enter the test as old compiled member references.

`LegacyBinaryCompatibilityChecks.RunAsync(root, Check)` packages only that DLL and
its manifest, imports it through the current `PluginHost`, and exercises the actual
collectible load context. It also checks the IL metadata and verifies third-party
color alpha, appearance defaults and configuration are not overridden by built-in
appearance preferences. The host never loads the archived SDK DLL.

The fixture writes configuration with `Utf8JsonWriter`. Passing a plugin-defined
anonymous payload to the default `JsonSerializer` options would place that
collectible type into a shared metadata cache and turn this SDK check into an
unrelated serializer-cache lifetime test. No reflection cache clearing or extra
forced GC is used to make unloading pass.

Root integration: add the following call in `tests/GlassDesk.PluginChecks/Program.cs`
before its final check count; the helper is automatically compiled by that project:

```csharp
await LegacyBinaryCompatibilityChecks.RunAsync(root, Check);
```

Run sequentially from the GlassDesk root with the existing .NET 10.0.401 toolchain:

```powershell
dotnet restore .\tests\Fixtures\LegacyPlugin\GlassDesk.LegacyPlugin.csproj --locked-mode
dotnet build .\tests\Fixtures\LegacyPlugin\GlassDesk.LegacyPlugin.csproj -c Release --no-restore -m:1 -p:BuildInParallel=false
dotnet build .\tests\GlassDesk.PluginChecks\GlassDesk.PluginChecks.csproj -c Release --no-restore -m:1 -p:BuildInParallel=false
dotnet run --project .\tests\GlassDesk.PluginChecks\GlassDesk.PluginChecks.csproj -c Release --no-build --no-restore
```

These commands write only test `bin/obj` outputs and isolated
`artifacts/plugin-checks-legacy/<guid>` data. They do not publish, install, launch a
GUI, alter the running candidate or modify the protected release directory.
