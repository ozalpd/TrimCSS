# TrimCSS — AI Developer Guide

Context for AI coding agents working in this repository. Read this before making changes.

## What this project is

TrimCSS is a Windows desktop utility that scans web project source files (Razor `.cshtml`, HTML, JavaScript) to detect unused and ambiguous CSS selectors and refactor stylesheets. It is split into a headless .NET 10 scanning engine (`TrimCSS.Core.dll`) and a native WPF desktop GUI (`TrimCSS.dll` / `TrimCSS.exe`).

## Solution layout

```

TrimCSS.slnx
├── AGENTS.md
├── README.md
└── src/
├── TrimCSS.Core/             # Core engine, plain class library (net10.0)
│   ├── Contracts/            # ICssParser, ICssScanner, ISourceScanner
│   ├── Engine/               # ProjectScanner (orchestrates file discovery & matching)
│   ├── Models/               # CssSelectorInfo, ScannedToken, ScanResult
│   └── Parsers/              # CssParser (AngleSharp.Css), HtmlRazorScanner, JsScanner (Esprima)
├── TrimCSS.GUI/              # Native WPF desktop app (WinExe, net10.0-windows, UseWPF=true)
│   ├── App.xaml              # Application entry point & startup URI
│   ├── App.xaml.cs
│   ├── MainWindow.xaml       # Dark developer theme UI + virtualized DataGrid
│   ├── MainWindow.xaml.cs    # Folder picker, async scan execution, ICollectionView filtering
│   └── Models/
│       └── SelectorRowItem.cs # Row view-model wrapping CssSelectorInfo with INotifyPropertyChanged
└── TrimCSS.Tests/            # xUnit test suite (net10.0)
├── CssParserTests.cs
├── HtmlRazorScannerTests.cs
├── JsScannerTests.cs
└── ProjectScannerTests.cs

```

Key NuGet packages:
- `TrimCSS.Core`: `AngleSharp` (HTML/DOM parsing), `AngleSharp.Css` (CSS AST parsing), `Esprima` (JS AST parsing).
- `TrimCSS.GUI`: Zero third-party UI packages — uses built-in WPF (`net10.0-windows`) and `Microsoft.Win32.OpenFolderDialog`.

## Build, run, test

```powershell
dotnet build                                  # Build entire solution
dotnet run --project src/TrimCSS.GUI          # Launch the WPF GUI (Debug)
dotnet test                                   # Run all xUnit tests

```

In Visual Studio, run tests via Test Explorer (`Ctrl+R, A`). The GUI executable outputs to `src/TrimCSS.GUI/bin/Debug/net10.0-windows/TrimCSS.exe`.

**Target Framework boundary:** Keep `TrimCSS.Core` and `TrimCSS.Tests` on plain `net10.0` so the scanning engine remains completely UI-agnostic and cross-platform. Only `TrimCSS.GUI` targets `net10.0-windows`.

## Architecture & key design rules

### 1. Core scanning engine (`TrimCSS.Core`)

* `ProjectScanner` implements `ICssScanner` and coordinates directory traversal, stylesheet parsing, source token extraction, and classification.
* **Exclusions:** Automatically skips `bin`, `obj`, `node_modules`, `.git`, `.vs`, `dist`, and `build` directories, as well as `*.min.css` files.
* **Classification:**
* Pure ID or element selectors (`TargetClasses.Count == 0`) are ignored.
* Selectors whose `TargetClasses` all exist in exact tokens (`HtmlClassAttribute`, `RazorExpression`, `JsStringLiteral`) are treated as **used**.
* Selectors where at least one class matches an exact token or a `JsTemplateLiteral` prefix/suffix (e.g., `alert-` matching `.alert-danger`) are flagged as **ambiguous**.
* Selectors with zero exact or dynamic matches are flagged as **unused**.



### 2. WPF GUI (`TrimCSS.GUI`)

* **UI Thread responsiveness:** `MainWindow` invokes `_scanner.ScanProjectAsync(...)` inside `Task.Run(...)` so large projects (450+ files / 30,000+ selectors) never block the WPF dispatcher thread.
* **Virtualization is load-bearing:** Real-world web projects routinely surface 25,000+ flagged selectors (especially when vendor stylesheets like Bootstrap are present). Keep `EnableRowVirtualization="True"`, `EnableColumnVirtualization="True"`, and `VirtualizingPanel.VirtualizationMode="Recycling"` enabled on `ResultsGrid` in `MainWindow.xaml`. Never replace `ResultsGrid` with a non-virtualized `ItemsControl` or `StackPanel`.
* **Filtering:** Status pills (`All Flagged`, `Unused Only`, `Ambiguous Only`), text search, and the CSS file dropdown filter via `ICollectionView` (`CollectionViewSource.GetDefaultView`) over `_allItems` (`List<SelectorRowItem>`).
* **Theme tokens:** Maintain the dark developer palette (`#0F0F0F` primary background, `#1A1A1A` secondary panels, `#2A2A2A` borders, `#7C5CFC` accent, `#EF4444` unused status, `#F59E0B` ambiguous status, `Consolas` for code/paths).

## Conventions

* C# 12+ / .NET 10: implicit usings, nullable reference types enabled, file-scoped namespaces.
* Every new parser rule, exclusion filter, or CSS refactoring feature in `TrimCSS` must have corresponding unit/integration tests in `TrimCSS.Tests`.
* Do not commit temporary workspace scratch folders such as `session-state/`.

## Session artifacts

`session-state/` is agent workspace scratch (not source). Do not commit it.
