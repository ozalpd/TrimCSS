# Copilot instructions for TrimCSS

Read **[AGENTS.md](../AGENTS.md)** at the repository root first — it is the single source of truth for architecture, build/test commands, and UI virtualization rules.

Short version:

- **Solution layout:** `TrimCSS.Core` (core scanning engine, `net10.0`), `TrimCSS.GUI` (native WPF desktop app, `net10.0-windows` with `UseWPF=true`), and `TrimCSS.Tests` (xUnit, `net10.0`).
- **Target framework boundary:** Keep `TrimCSS.Core` and `TrimCSS.Tests` on plain `net10.0` with zero Windows/UI dependencies so the core engine stays cross-platform.
- **Direct in-process execution:** `MainWindow` calls `ICssScanner` (`ProjectScanner`) directly via `Task.Run(...)` and uses `Microsoft.Win32.OpenFolderDialog` for folder selection — do not introduce WebView2, InfiniFrame, or IPC bridges.
- **UI virtualization is load-bearing:** Real-world projects routinely flag 25,000+ selectors. Keep row/column virtualization (`VirtualizingPanel.VirtualizationMode="Recycling"`) enabled on `ResultsGrid` in `MainWindow.xaml`, and filter via `ICollectionView`.
- **Testing & validation:** Any new parser rule, exclusion filter, or CSS refactoring logic in `TrimCSS.Core` must have xUnit tests in `TrimCSS.Tests`. Validate with `dotnet build` and `dotnet test` before finishing.
- **Workspace hygiene:** Never commit `session-state/` scratch artifacts.
- **UI & styling:** Follow **[Design.md](../Design.md)** for color tokens, native Windows fonts, and layout conventions when editing `TrimCSS.GUI`.