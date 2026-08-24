# SheetLite

A fast, portable CSV and XLSX workbook editor for Windows, built with WinForms on .NET 9.

![GitHub Release](https://img.shields.io/github/v/release/holdmysocks/SheetLite?label=version) ![Platform](https://img.shields.io/badge/platform-Windows%20x64-lightgrey) ![.NET](https://img.shields.io/badge/.NET-9.0-purple)

SheetLite is distributed as a single self-contained executable. It has no installer, requires no separate .NET runtime, stores no recent-file history, and sends no telemetry. On startup it makes one request to GitHub Releases to check whether a newer version is available.

## Features

- Edit CSV, TSV, TXT, and XLSX files.
- Work with multiple workbooks and worksheets.
- Use formulas, sorting, filtering, find/replace, undo/redo, and split view.
- Query the current workbook with a small read-only SQL dialect.
- Copy and paste CSV, tabular text, Markdown, HTML, JSON, and SQL values.
- Preserve XLSX formulas, cached results, basic colors, bold text, and frozen panes.
- Run as a portable, self-contained Windows x64 executable.

Current limits: SheetLite does not support merged cells, charts, conditional formatting, validation, macros, named ranges, or advanced XLSX styling. Column widths are session-only.

## Download

Download the newest `SheetLite-*-win-x64.zip` from [GitHub Releases](https://github.com/holdmysocks/SheetLite/releases/latest), extract it, and run `SheetLite.exe`.

The executable is currently unsigned, so Windows SmartScreen may show a warning on first launch. A SHA-256 checksum is included with every release.

## Build

Requirements: Windows 10/11 x64 and the .NET 9 SDK.

```powershell
dotnet build -c Release
dotnet run -c Release
```

Run the dependency-free test suite with:

```powershell
dotnet run --project tests/SheetLite.Core.Tests -c Release
```

Create the self-contained executable with:

```powershell
dotnet publish -c Release
```

The executable is written to `bin\Release\net9.0-windows\win-x64\publish\SheetLite.exe`.

## Releases

`SheetLite.csproj` is the source of truth for the app version. To publish a release:

1. Change `<Version>` to a new `major.minor.patch` value.
2. Commit and push the change to `main`.

The release workflow runs the tests, publishes the self-contained Windows app, creates a ZIP and SHA-256 checksum, tags the commit, and creates a GitHub release with generated notes. If that version already exists, the workflow exits without replacing it. It can also be started manually from the Actions tab.

## Update checks and privacy

After the main window opens, SheetLite requests the latest public release metadata from `api.github.com`. If a newer stable release exists, it asks before opening the release page in the default browser. Failed checks are silent during startup, and no files or usage data are uploaded. You can run the check manually from **Help → Check for updates**.

## Project layout

```text
SheetLite.csproj                 App metadata and publish settings
Program.cs                       Entry point and error handling
MainForm*.cs                     Windows UI and application commands
CellModel.cs                     Workbook and worksheet models
Formula*.cs                      Formula parsing, evaluation, and references
CsvCodec.cs / XlsxCodec.cs       File format readers and writers
SqlQueryEngine.cs                Read-only workbook SQL
UpdateChecker.cs                 GitHub release update check
Assets/                          App icon and window graphics
tests/SheetLite.Core.Tests/      Dependency-free test suite
.github/workflows/release.yml    Automated GitHub releases
```

## License

No license file is currently included. Unless the repository owner adds one, normal copyright restrictions apply.
