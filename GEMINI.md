# TrimCSS — Gemini Agent Guide

Context, architecture rules, and development guidelines for Gemini models and AI coding assistants working in the TrimCSS repository.

---

## 1. Project Overview

**TrimCSS** is a Windows desktop utility that scans web project source files (Razor `.cshtml`, HTML, JavaScript) to detect unused and ambiguous CSS selectors and refactor bloated stylesheets. It is designed to handle large enterprise web projects (450+ files, 30,000+ selectors) with near-instant responsiveness and zero UI freezing.

The project is architecturally divided into:
- **`TrimCSS.Core`** (`TrimCSS.Core.dll`): A headless, cross-platform .NET 10 scanning and classification engine.
- **`TrimCSS.GUI`** (`TrimCSS.exe` / `TrimCSS.dll`): A native Windows desktop presentation layer built with WPF (`net10.0-windows`), featuring a developer-focused dark theme and a virtualized data grid.
- **`TrimCSS.Tests`**: Comprehensive automated xUnit test suite.

---

## 2. Solution Structure & Projects

```
TrimCSS.slnx
├── .github/copilot-instructions.md
├── AGENTS.md                          # General agent instructions
├── Design.md                          # Visual tokens, native typography, and WPF UI layout spec
├── GEMINI.md                          # Gemini-specific guidance & rules
├── README.md                          # Repository documentation
└── src/
    ├── TrimCSS.Core/                  # Headless Core Engine (net10.0)
    │   ├── TrimCSS.Core.csproj             # AssemblyName: TrimCSS.Core
    │   ├── Contracts/                 # ICssParser, ICssScanner, ISourceScanner
    │   ├── Engine/                    # ProjectScanner (traversal & classification orchestration)
    │   ├── Models/                    # CssSelectorInfo, ScannedToken, ScanResult
    │   └── Parsers/                   # CssParser, HtmlRazorScanner, JsScanner
    ├── TrimCSS.GUI/                   # Native WPF Desktop Client (net10.0-windows)
    │   ├── TrimCSS.GUI.csproj         # WinExe, UseWPF=true, AssemblyName: TrimCSS
    │   ├── App.xaml / App.xaml.cs     # WPF entry point & startup URI
    │   ├── MainWindow.xaml / .cs      # Dark theme UI, OpenFolderDialog, virtualized grid
    │   └── Models/
    │       └── SelectorRowItem.cs     # Row view-model with INotifyPropertyChanged
    └── TrimCSS.Tests/                 # xUnit Test Suite (net10.0)
        ├── TrimCSS.Tests.csproj
        ├── CssParserTests.cs
        ├── HtmlRazorScannerTests.cs
        ├── JsScannerTests.cs
        └── ProjectScannerTests.cs
```

### Key NuGet Packages
- **Core Engine:** `AngleSharp` (HTML/DOM), `AngleSharp.Css` (CSS AST parser), `Esprima` (JavaScript AST parser).
- **GUI:** Zero third-party UI libraries. Uses native WPF controls and `Microsoft.Win32.OpenFolderDialog`.
- **Testing:** `xUnit`, `Microsoft.NET.Test.Sdk`, `coverlet.collector`.

---

## 3. Architecture & Design Rules

### Target Framework Boundary
- **Keep `TrimCSS.Core` strictly UI-agnostic and cross-platform:** `TrimCSS.Core` must target standard `net10.0` with zero Windows/WPF dependencies.
- Only `TrimCSS.GUI` targets `net10.0`.
- Do not introduce IPC layers, WebViews (WebView2), or local HTTP servers; the GUI calls `ProjectScanner` directly in-process via `Task.Run(...)`.

### Core Scanning Engine (`TrimCSS.Core`)
- **`ProjectScanner` (`ICssScanner`):** Orchestrates file discovery, concurrent file scanning, token extraction, and selector classification.
- **Automatic Exclusions:**
  - Folders: `bin`, `obj`, `node_modules`, `.git`, `.vs`, `dist`, `build`.
  - Files: `*.min.css` (vendor minified stylesheets are excluded from dead-code pruning).
- **Token Extraction:**
  - `HtmlRazorScanner`: Parses `.html`, `.htm`, and `.cshtml`. Extracts exact class names from `class="..."` attributes and Razor `@class` expressions.
  - `JsScanner`: Parses `.js` AST via `Esprima`. Extracts string literals (`'btn-primary'`) as exact tokens, and template literal prefixes/suffixes (e.g., `` `alert-${type}` `` -> `alert-`) as dynamic tokens.
- **Selector Classification Rules:**
  - **Ignored:** Selectors with no class requirements (`TargetClasses.Count == 0`, e.g., pure element `div` or ID `#header` selectors).
  - **Used:** All classes in `TargetClasses` exist in scanned exact tokens.
  - **Ambiguous:** Selectors where at least one class partially matches an exact token or matches a dynamic template literal prefix/suffix. This prevents accidental false-positive deletions of dynamically generated classes.
  - **Unused:** Selectors with zero static or dynamic references found anywhere in the codebase.

### WPF Desktop Application (`TrimCSS.GUI`)
- **UI Responsiveness:** `MainWindow` must never run synchronous scans on the WPF Dispatcher thread. Always offload `_scanner.ScanProjectAsync(...)` onto a background thread via `Task.Run(...)`.
- **UI Virtualization is Load-Bearing:**
  - Large projects can produce 25,000+ flagged selectors (especially when full CSS frameworks are used).
  - `ResultsGrid` in `MainWindow.xaml` **MUST** keep:
    - `EnableRowVirtualization="True"`
    - `EnableColumnVirtualization="True"`
    - `VirtualizingPanel.VirtualizationMode="Recycling"`
  - **NEVER** replace `ResultsGrid` with a non-virtualized container (such as `StackPanel` or un-virtualized `ItemsControl`).
- **Data Filtering:** Instant filtering (status pills, CSS file selector dropdown, keyword search) is performed using `ICollectionView` (`CollectionViewSource.GetDefaultView`) backed by `List<SelectorRowItem>`.
- **Design System & Theme Tokens:** For UI, XAML, and styling changes in `TrimCSS.GUI`, follow `Design.md`.

---

## 4. Build, Run, and Test Commands

| Action | Command |
|---|---|
| **Build Solution** | `dotnet build` |
| **Run Tests** | `dotnet test` |
| **Launch Desktop App** | `dotnet run --project src/TrimCSS.GUI` |
| **Publish GUI (Self-Contained / Release)** | `dotnet publish src/TrimCSS.GUI -c Release` |

> **Note on .NET Telemetry in Sandboxed Environments:**  
> If running `dotnet` inside restricted sandbox environments that limit Windows registry access, prefix commands with `$env:DOTNET_CLI_TELEMETRY_OPTOUT=1` (PowerShell) or `set DOTNET_CLI_TELEMETRY_OPTOUT=1` (cmd).

---

## 5. Coding Conventions

- **Language:** C# 12+ on .NET 10+.
- **Language Features:** File-scoped namespaces (`namespace TrimCSS.Core;`), nullable reference types enabled, implicit usings.
- **Asynchronous Code:** Use `async`/`await` end-to-end with cancellation token support (`CancellationToken`) across scanning APIs.
- **Unit Testing:**
  - Any new parsing rule, exclusion pattern, or token extraction heuristic in `TrimCSS.Core` **must** have corresponding xUnit tests in `TrimCSS.Tests`.
  - Always run `dotnet test` to confirm tests pass before completing changes.
- **Workspace Hygiene:**
  - Never commit temporary scratch artifacts or agent state (such as `session-state/`).

