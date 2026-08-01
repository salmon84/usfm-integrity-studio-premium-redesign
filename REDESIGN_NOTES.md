# UIS Premium Redesign

This workspace is an isolated UI redesign of USFM Integrity Studio Premium.

## Safety boundary

- `Premium/Models`, `Shared`, and the regression behavior remain aligned with the stable UIS Premium workspace.
- UI work is confined to Avalonia views, styles, assets, and presentation metadata.
- The redesign uses the separate `UISPremiumRedesign` executable name.
- The stable UIS Premium workspace is not modified by redesign builds or tests.

## Run

```bash
cd Premium
DOTNET_CLI_TELEMETRY_OPTOUT=1 AVALONIA_TELEMETRY_OPTOUT=1 dotnet run
```

## Verify

```bash
dotnet build Premium/UsfmIntegrityStudio.csproj
dotnet run --project Tests/PunctuationRegressionTests/PunctuationRegressionTests.csproj
```
