# TrimCSS — AI Developer Guide

Context for AI coding agents working in this repository. Read this before making changes.

## What this project is

TrimCSS is a Windows desktop utility that scans web project source files (Razor `.cshtml`, HTML, JavaScript) to detect unused and ambiguous CSS selectors and refactor stylesheets. It now also detects unused CSS files and provides a line‑level CSS diff generator.

## Solution layout

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

## Build, run, test

```powershell
dotnet build                                  # Build the entire solution
dotnet run --project src/TrimCSS.GUI          # Launch the WPF GUI (Debug)
dotnet test                                   # Run all xUnit tests
```

## Key components

- **Core engine (`TrimCSS.Core`)** – Scans files, extracts tokens, classifies selectors, and now includes stylesheet‑level audit (`UnusedCssFileInfo`) and a diff generator (`CssDiffGenerator`).
- **GUI (`TrimCSS.GUI`)** – Virtualized DataGrid UI with new CSS Files view, metric cards, and diff preview dialog. Uses native WPF, no third‑party UI packages.
- **Tests (`TrimCSS.Tests`)** – Comprehensive unit tests covering parsers, project scanning, CSS file reference detection, and the diff generator.

## New feature highlights

- **Configurable exclusion patterns** via `ScanOptions` (folder/file bar).
- **Unused CSS file detection** (`CssFileReferenceStatus`, `UnusedCssFileInfo`).
- **Line‑level CSS diff generator** (`CssDiffGenerator`).
- **Report exporting** to CSV/JSON (`ReportExporter`).

---

## Conventions

- C# 12+, .NET 10, file‑scoped namespaces, nullable reference types.
- Every new parser rule, exclusion filter, or refactoring feature must have corresponding tests.
- Do not commit temporary workspace folders such as `session-state/`.
