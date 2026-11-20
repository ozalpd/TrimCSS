# TrimCSS ✂️

> A lightweight .NET 10 desktop utility that scans web project source files (Razor `.cshtml`, HTML, JavaScript) to detect **unused and ambiguous CSS selectors**, now also **detects unused CSS files**, and provides a **line‑level CSS diff generator** for safe refactoring.

---

## 📌 Overview

**TrimCSS** helps developers clean up bloated stylesheets in web applications. It parses CSS stylesheets into ASTs and scans static HTML markup, Razor templates, and JavaScript for class references. The core engine classifies selectors into:

- **Unused** – no references anywhere.
- **Ambiguous** – matched only via dynamic template literals or partial names.
- **Unused CSS Files** – stylesheet files that are never referenced by any view, another stylesheet (`@import`), or JavaScript/TypeScript module.

The solution separates the scanning logic from the UI:

- **TrimCSS.Core** – a pure, cross‑platform class library (`net10.0`). Includes the new `CssDiffGenerator` utility for side‑by‑side diff previews.
- **TrimCSS.GUI** – a native Windows desktop client built with WPF (`net10.0-windows`). Adds a *CSS Files* view, metric cards for file‑level audit, and diff preview dialogs.
- **TrimCSS.Tests** – xUnit test suite covering selector scanning, file‑level audit, and the diff generator.

---

## ✨ Features

- Multi‑Language Source Scanning (HTML, Razor, JavaScript)
- Smart Classification (Unused, Ambiguous selectors)
- **Configurable folder and file exclusion patterns** (`ScanOptions` & Exclusion Rules bar)
- **Unreferenced/unused CSS stylesheet detection** (`CssFileReferenceStatus` / `UnusedCssFileInfo`)
- **Line‑level CSS Diff Generator** (`CssDiffGenerator` with context collapsing)
- **Exporting filtered scan reports to CSV and JSON** (`ReportExporter`)
- High‑Performance Virtualized WPF Grid
- Instant Filtering by status, file, and search keywords

---

## 🚀 Getting Started

### Prerequisites

- [.NET 10 SDK](https://dotnet.microsoft.com/) or later
- Windows 10/11 (required for the GUI; core library runs cross‑platform)

### Building & Running

```bash
# Clone the repo
git clone https://github.com/ozalpd/TrimCSS.git
cd TrimCSS

# Build the whole solution
dotnet build

# Run the test suite (covers the new diff generator)
dotnet test

# Launch the desktop GUI (Debug)
dotnet run --project src/TrimCSS.GUI
```

---

## 🗺️ Roadmap

- [x] Custom folder and file exclusion patterns
- [x] Exporting filtered scan reports to CSV and JSON
- [ ] CSS rule pruning & removal (modal confirmation, `.bak` backups)
- [ ] Light theme variant for the UI
- [ ] Export diff previews to HTML for richer reporting

---

## 🛠️ Tech Stack

- **Language & Runtime:** C# 12+, .NET 10.0
- **Desktop UI:** WPF (`net10.0-windows`) – pure XAML, no third‑party UI packages.
- **Parsing:** AngleSharp & AngleSharp.Css for HTML/CSS, Esprima.NET for JavaScript.
- **Testing:** xUnit

---

## 🤝 Contributing

Contributions, issue reports, and feature requests are welcome! Feel free to open an issue or submit a pull request.

---

## 📄 License

This project is licensed under the MIT License – see the `LICENSE` file for details.
