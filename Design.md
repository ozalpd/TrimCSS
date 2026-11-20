---
name: "TrimCSS Precision"
description: "A compact, high-density .NET 10 WPF desktop developer design system engineered for CSS scanning, AST analysis, and selector audits using zero external font or UI packages."
---

## Overview

TrimCSS is a native **.NET 10 WPF desktop application** built for scanning web projects, pruning dead stylesheets, and resolving ambiguous selectors across complex codebases. Designed for Windows desktop environments with native XAML styles, the design language prioritizes high information density, rapid ocular scanning, low visual friction, and structural clarity.

The visual style embodies precision developer tools:
- **Platform & Runtime**: Built for **.NET 10 WPF (`net10.0-windows`)** utilizing `VirtualizingStackPanel`‑backed `DataGrid` controls (`VirtualizationMode="Recycling"`) with zero third‑party UI dependencies.
- **Compact & High‑Density**: Tight gutters, disciplined row heights, and inline monospaced indicators that keep tens of thousands of selectors legible and responsive.
- **Obsidian Dark Theme (Current)**: Deep obsidian surfaces (`#0F0F0F`, `#1A1A1A`, `#1E1E1E`) engineered to minimize eye strain during extended audits, punctuated with vivid electric purple primary actions (`#7C5CFC`) and high‑visibility semantic status pills.
- **Architectural Clarity**: Subtle `1px` borders (`#2A2A2A`), purposeful dividers, and monospaced code blocks eliminate ambiguity between selectors, target classes, line numbers, and relative file paths.

---

## Colors

TrimCSS uses semantic role pairings tuned for high scannability across dense data grids. The **Dark** palette reflects the active `MainWindow.xaml` implementation; the **Light** palette is reserved for future `ResourceDictionary` theme switching.

- **Primary**: Electric purple (`#7C5CFC`, hover `#6A4DE8`) with crisp `#FFFFFF` foreground text (`>= 4.5:1` contrast) used for dominant actions ("Scan Project") and active filter pills.
- **Status Unused (Error/Flagged)**: Crimson badge styling (`#EF4444` text on `#2B1414` fill with `#662222` border in dark mode) to tag unreferenced CSS rules.
- **Status Ambiguous (Warning)**: High‑contrast amber badge styling (`#F59E0B` text on `#2B2111` fill with `#664400` border in dark mode) to flag selectors matched against dynamic JS template literals or partial class references.
- **Status Clean (Success)**: Emerald styling (`#10B981` in dark mode) for confirmed clean/optimized states.

```yaml
colors:
  primary: "#7C5CFC"
  primary-hover: "#6A4DE8"
  unused: "#EF4444"
  unused-light: "#B91C1C"
  unused-bg-dark: "#2B1414"
  unused-border-dark: "#662222"
  unused-bg-light: "#FEF2F2"
  unused-border-light: "#FECACA"
  ambiguous: "#F59E0B"
  ambiguous-light: "#B45309"
  ambiguous-bg-dark: "#2B2111"
  ambiguous-border-dark: "#664400"
  ambiguous-bg-light: "#FFFBEB"
  ambiguous-border-light: "#FDE68A"
  success: "#10B981"
  success-light: "#047857"
  success-bg-dark: "#0C2318"
  success-border-dark: "#14532D"
  success-bg-light: "#ECFDF5"
  success-border-light: "#A7F3D0"
  dark:
    primary: "#7C5CFC"
    on-primary: "#FFFFFF"
    primary-container: "#2A224D"
    on-primary-container: "#F0F0F0"
    surface: "#0F0F0F"
    on-surface: "#F0F0F0"
    surface-container-low: "#161616"
    surface-container: "#1A1A1A"
    surface-container-high: "#1E1E1E"
    surface-hover: "#262626"
    on-surface-variant: "#888888"
    outline: "#666666"
    outline-variant: "#2A2A2A"
  light: # Planned (ResourceDictionary theme switching)
    primary: "#6A4DE8"
    on-primary: "#FFFFFF"
    primary-container: "#EDE9FE"
    on-primary-container: "#312E81"
    surface: "#F8FAFC"
    on-surface: "#0F172A"
    surface-container-low: "#FFFFFF"
    surface-container: "#F1F5F9"
    surface-container-high: "#E2E8F0"
    surface-hover: "#E2E8F0"
    on-surface-variant: "#475569"
    outline: "#94A3B8"
    outline-variant: "#CBD5E1"
```

---

## Layout & Window Structure

`MainWindow.xaml` uses a vertical multi‑row `Grid` (`1280×760` default, `900×540` minimum):

1. **Top Action Bar (`Row 0`)**: Displays the active project folder path (`Consolas`, ellipsis‑trimmed) on the left and primary actions (`Select Folder`, `Scan Project`, `Export Report`) on the right.
2. **Metrics Strip (`Row 1`)**: Four equal‑width KPI cards separated by `1px` `#2A2A2A` dividers (`TOTAL CSS SELECTORS`, `UNUSED SELECTORS`, `AMBIGUOUS SELECTORS`, and `SCANNED FILES` with execution time).
3. **Exclusion Rules Bar (`Row 2`)**: Configurable ignored folders and wildcard file patterns plus a quick `Exclude vendor CSS` toggle.
4. **Filter Controls (`Row 3`)**: Segmented status filter pills (`All Flagged`, `Unused Only`, `Ambiguous Only`), keyword `SearchBox`, CSS file dropdown (`ComboBox`), and live visible result counter.
5. **Virtualized Results Grid / Empty State (`Row 4`, `*` height)**:
   * **Empty / Status State**: Centered prompt (`"Select a project folder and click Scan Project to detect unused CSS selectors."`) shown before scanning or when filters yield zero matches.
   * **Virtualized `DataGrid`**: Columns for Row Selection (`CheckBox`), `STATUS` badge, `SELECTOR`, `TARGET CLASSES`, `CSS FILE`, and `LOCATION` (`Line:Column`), backed by row/column recycling virtualization (`EnableRowVirtualization="True"`, `VirtualizingPanel.VirtualizationMode="Recycling"`).

---

## Roadmap UI Additions (Planned — Phase 3+)

The following design elements are planned for future iterations and are **not yet implemented** in `TrimCSS.GUI`:

- **CSS Rule Removal & Diff Preview**: Action bar button (`Remove Selected`) opening a side‑by‑side diff confirmation dialog. The **diff engine** (`CssDiffGenerator`) is already implemented in `TrimCSS.Core`; the GUI modal and automatic `.bak` file backups remain planned.
- **Theme Switcher (`DarkTheme.xaml` / `LightTheme.xaml`)**: Runtime `ResourceDictionary` swap supporting the Soft‑Slate Light palette defined above.
- **Custom `WindowChrome` & Bottom `StatusBar`**: Compact custom titlebar and dedicated `28px` footer bar for live scan progress and active exclusion indicators.

---
