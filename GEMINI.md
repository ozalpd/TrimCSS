# TrimCSS — Gemini Agent Guide

## 1. Project Overview

**TrimCSS** is a Windows desktop utility that scans web project source files (Razor `.cshtml`, HTML, JavaScript) to detect unused and ambiguous CSS selectors, now also detects unused CSS files and provides a line‑level CSS diff generator for safe refactoring.

The solution is architecturally divided into:
- **`TrimCSS.Core`** (`TrimCSS.Core.dll`): headless, cross‑platform .NET 10 scanning and classification engine. Includes `CssDiffGenerator` and stylesheet reference tracking (`StylesheetReference`, `UnusedCssFileInfo`).
- **`TrimCSS.GUI`** (`TrimCSS.exe` / `TrimCSS.dll`): native Windows desktop client built with WPF (`net10.0-windows`). Offers a virtualized DataGrid, CSS Files view, metric cards, and diff preview dialogs.
- **`TrimCSS.Tests`**: xUnit test suite covering parsers, project scanning, CSS file audit, and the diff generator.

## 2. Solution Structure & Projects

```
TrimCSS.slnx
├── .github/copilot-instructions.md
├── AGENTS.md                     # Developer & AI agent guide
├── Design.md                     # UI design spec and tokens
├── GEMINI.md                     # Gemini agent rules
├── README.md                     # Repository documentation
└── src/
    ├── TrimCSS.Core/            # Core engine (net10.0)
    │   ├── Contracts/           # ICssParser, ICssScanner, ISourceScanner
    │   ├── Engine/              # ProjectScanner, CssDiffGenerator
    │   ├── Models/              # CssSelectorInfo, ScannedToken, ScanResult, ScanOptions, StylesheetReference, CssFileReferenceStatus, UnusedCssFileInfo
    │   └── Parsers/             # CssParser, HtmlRazorScanner, JsScanner
    ├── TrimCSS.GUI/             # WPF desktop app (net10.0-windows)
    │   ├── App.xaml
    │   ├── App.xaml.cs
    │   ├── MainWindow.xaml
    │   ├── MainWindow.xaml.cs
    │   ├── Models/              # SelectorRowItem, CssFileRowItem
    │   └── Services/            # ReportExporter
    └── TrimCSS.Tests/           # xUnit tests (net10.0)
        ├── CssDiffGeneratorTests.cs
        ├── CssFileReferenceTests.cs
        ├── HtmlRazorScannerTests.cs
        ├── JsScannerTests.cs
        ├── ProjectScannerTests.cs
        └── Tests/CssParserTests.cs
```

## 3. Architecture & Design Rules

### Target Framework Boundary
- **Keep `TrimCSS.Core` strictly UI‑agnostic and cross‑platform** (`net10.0`).
- **Only `TrimCSS.GUI` targets `net10.0-windows`.**
- Do not introduce IPC layers, WebViews, or local HTTP servers; the GUI calls `ProjectScanner` directly via `Task.Run(...)`.

### Core Scanning Engine (`TrimCSS.Core`)
- `ProjectScanner` orchestrates file discovery, concurrent scanning, token extraction, and selector classification.
- **Automatic exclusions**: `bin`, `obj`, `node_modules`, `.git`, `.vs`, `dist`, `build`, and `*.min.css`.
- **Configurable exclusions** via `ScanOptions` (folder/file patterns) – exposed in the GUI Exclusion Rules bar.
- **Stylesheet reference tracking**: `StylesheetReference` captures `<link>` / `@import` relationships. `UnusedCssFileInfo` and `CssFileReferenceStatus` report unused or ambiguous CSS files.
- **Diff generator**: `CssDiffGenerator` provides a Myers/LCS diff with context collapsing for safe CSS refactoring.

### WPF Desktop Application (`TrimCSS.GUI`)
- UI must remain responsive; scanning runs on background threads.
- Virtualized `DataGrid` is load‑bearing; keep row/column virtualization enabled.
- New **CSS Files** view presents file‑level audit data.
- **ReportExporter** now supports CSV and JSON export of selector and file audit results.

---

## 4. Build, Run, and Test Commands

| Action | Command |
|---|---|
| **Build Solution** | `dotnet build` |
| **Run Tests** | `dotnet test` |
| **Launch Desktop App** | `dotnet run --project src/TrimCSS.GUI` |
| **Publish GUI** | `dotnet publish src/TrimCSS.GUI -c Release` |

> **Note on .NET Telemetry in Sandboxed Environments:** If running `dotnet` inside a restricted sandbox, prefix commands with `$env:DOTNET_CLI_TELEMETRY_OPTOUT=1` (PowerShell) or `set DOTNET_CLI_TELEMETRY_OPTOUT=1` (cmd).

---

## 5. Coding Conventions

- C# 12+, .NET 10, file‑scoped namespaces, nullable reference types.
- Asynchronous `async/await` throughout scanning APIs.
- Every new parser rule, exclusion filter, stylesheet reference, or diff feature must have corresponding xUnit tests.
- Do not commit temporary workspace files such as `session-state/`.
