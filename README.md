# TrimCSS ✂️

> A lightweight C# desktop application that scans web project source files (Razor, HTML, JS) to detect unreferenced CSS selectors and safely prune dead styles via an interactive InfiniFrame GUI.

---

## 📌 Overview

**TrimCSS** is a developer utility designed to clean up bloated stylesheets in web applications. By analyzing static HTML markup, Razor templates (`.cshtml`), and JavaScript/TypeScript source files, TrimCSS identifies unused CSS classes and provides a safe, interactive interface for previewing and removing dead CSS rules.

The project is architected with strict separation of concerns, housing all scanning, AST parsing, matching, and file refactoring logic inside a standalone class library (`TrimCSS.dll`), paired with a desktop GUI host (`TrimCSS.GUI.exe`) powered by **InfiniFrame**.

---

## 🏗️ Architecture & Solution Structure

```
TrimCSS.slnx
├── src/
│   ├── TrimCSS/              # Core Class Library (.NET 10)
│   │   ├── Models/           # Selector data models & scan results
│   │   ├── Parsers/          # CSS, HTML, Razor, and JS AST parsers
│   │   └── Refactoring/      # AST mutator & side-by-side diff generator
│   │
│   └── TrimCSS.GUI/          # Desktop Application (.NET 10 WinExe)
│       ├── Program.cs        # InfiniFrame window initialization
│       └── wwwroot/          # HTML/CSS/JS Desktop UI assets
```

### Key Components

* **`TrimCSS` (Class Library):** The engine responsible for parsing stylesheets into ASTs, scanning source files for class references, calculating selector usage confidence, and safely refactoring CSS files.
* **`TrimCSS.GUI` (InfiniFrame App):** A desktop GUI wrapper providing a responsive dashboard for selecting projects, inspecting unused selectors, viewing live code diffs, and executing cleanups.

---

## 🚀 Getting Started

### Prerequisites

* [.NET 10.0 SDK](https://dotnet.microsoft.com/) or later

### Building & Running

1. **Clone the repository:**
   ```bash
   git clone https://github.com/your-username/TrimCSS.git
   cd TrimCSS
   ```

2. **Restore dependencies & build:**
   ```bash
   dotnet build
   ```

3. **Run the GUI application:**
   ```bash
   dotnet run --project src/TrimCSS.GUI
   ```

---

## 🛠️ Tech Stack

* **Language & Runtime:** C# / .NET 10.0
* **GUI Framework:** [InfiniFrame](https://www.nuget.org/packages/InfiniLore.InfiniFrame)
* **Target Output:** Desktop Windows Application (`WinExe`)

---

## 🤝 Contributing

Contributions, issue reports, and feature requests are welcome! Feel free to check the issues tab if you would like to contribute.

---

## 📄 License

This project is licensed under the MIT License - see the `LICENSE` file for details.