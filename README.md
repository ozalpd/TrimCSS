# TrimCSS ✂️

> A lightweight .NET 10 desktop utility that scans web project source files (Razor, HTML, JavaScript) to detect unused and ambiguous CSS selectors via a native, virtualized WPF interface.

---

## 📌 Overview

**TrimCSS** helps developers identify and clean up bloated stylesheets in web applications. By parsing CSS stylesheets into ASTs and scanning static HTML markup, Razor templates (`.cshtml`), and JavaScript files for class references, TrimCSS classifies selectors into **Unused** and **Ambiguous** (such as dynamically concatenated classes in JS template literals) without slowing down on large codebases.

The solution separates core analysis from presentation: all scanning, AST parsing, and classification logic lives in a standalone, cross-platform class library (`TrimCSS.Core.dll`), paired with a fast native Windows desktop UI (`TrimCSS.exe`) built with **WPF**. The GUI assembly is `TrimCSS.dll`.

---

## ✨ Features

* **Multi-Language Source Scanning:** Extracts class references from `.html`, `.htm`, Razor `.cshtml` (including Razor expressions), and `.js` files.
* **Smart Classification:**
  * **Unused:** Selectors whose target classes have zero static or dynamic references across the project.
  * **Ambiguous:** Selectors partially matched or matched against dynamic JavaScript template literal prefixes/suffixes (e.g., `` `alert-${type}` `` matching `.alert-danger`), preventing accidental false-positive deletions.
* **Automatic Noise Filtering:** Automatically skips `bin`, `obj`, `node_modules`, `.git`, `.vs`, `dist`, and `build` directories, as well as pre-minified `*.min.css` files.
* **High-Performance Virtualized WPF Grid:** Built with row and column UI virtualization (`VirtualizingPanel.VirtualizationMode="Recycling"`) to render and filter 30,000+ flagged selectors smoothly.
* **Instant Filtering:** Filter results in real time by status (`All Flagged`, `Unused Only`, `Ambiguous Only`), specific CSS file, or keyword search across selectors, classes, and file paths.

---

## 🏗️ Architecture & Solution Structure


```

TrimCSS.slnx
├── AGENTS.md                     # Developer & AI agent architecture guide
├── README.md
└── src/
├── TrimCSS.Core/                  # Core Engine Class Library (net10.0)
│   ├── Contracts/            # ICssParser, ICssScanner, ISourceScanner
│   ├── Engine/               # ProjectScanner (file discovery & classification)
│   ├── Models/               # CssSelectorInfo, ScannedToken, ScanResult
│   └── Parsers/              # CssParser, HtmlRazorScanner, JsScanner
├── TrimCSS.GUI/              # Native WPF Desktop App (net10.0-windows)
│   ├── App.xaml              # Application entry point & startup URI
│   ├── App.xaml.cs
│   ├── MainWindow.xaml       # Dark developer theme & virtualized DataGrid
│   ├── MainWindow.xaml.cs    # OpenFolderDialog, async scanning, ICollectionView filtering
│   └── Models/
│       └── SelectorRowItem.cs # Row view-model wrapping CssSelectorInfo
└── TrimCSS.Tests/            # xUnit Test Suite (net10.0)
├── CssParserTests.cs
├── HtmlRazorScannerTests.cs
├── JsScannerTests.cs
└── ProjectScannerTests.cs

```

### Key Components

* **`TrimCSS.Core` (`net10.0`):** Headless scanning engine using **AngleSharp** / **AngleSharp.Css** for HTML and CSS AST parsing and **Esprima** for JavaScript AST traversal.
* **`TrimCSS.GUI` (`net10.0-windows`):** Zero-dependency native WPF desktop client featuring a dark developer theme, background `Task.Run` scan execution, native `OpenFolderDialog` folder selection, and live `ICollectionView` filtering.
* **`TrimCSS.Tests` (`net10.0`):** Automated xUnit test suite covering CSS rule extraction, HTML/Razor scanning, JS literal/template detection, and end-to-end project scans.

---

## 🚀 Getting Started

### Prerequisites

* [.NET 10.0 SDK](https://dotnet.microsoft.com/) or later
* Windows 10 / 11 (for `TrimCSS.GUI`; the core `TrimCSS.Core` library and tests run cross-platform)

### Building & Running

1. **Clone the repository:**
   ```bash
   git clone [https://github.com/ozalpd/TrimCSS.git](https://github.com/ozalpd/TrimCSS.git)
   cd TrimCSS

```

2. **Build the solution:**
```bash
dotnet build

```


3. **Run the test suite:**
```bash
dotnet test

```


4. **Launch the desktop GUI:**
```bash
dotnet run --project src/TrimCSS.GUI

```



---

## 🗺️ Roadmap

* [ ] Custom folder and file exclusion patterns (e.g., ignoring third-party vendor stylesheets like `bootstrap.css`)
* [ ] Safe CSS rule pruning with automatic file backups and side-by-side diff preview
* [ ] Exporting filtered scan reports to CSV and JSON

---

## 🛠️ Tech Stack

* **Language & Runtime:** C# 12+ / .NET 10.0
* **Desktop UI:** WPF (`net10.0-windows`)
* **CSS & HTML Parsing:** [AngleSharp](https://anglesharp.github.io/) & [AngleSharp.Css](https://github.com/AngleSharp/AngleSharp.Css)
* **JavaScript AST Parsing:** [Esprima .NET](https://github.com/sebastienros/esprima-dotnet)
* **Testing:** [xUnit](https://xunit.net/)

---

## 🤝 Contributing

Contributions, issue reports, and feature requests are welcome! Feel free to open an issue or submit a pull request.

---

## 📄 License

This project is licensed under the MIT License — see the `LICENSE` file for details.