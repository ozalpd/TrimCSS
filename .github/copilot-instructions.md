# Copilot instructions for TrimCSS

Read **[AGENTS.md](../AGENTS.md)** at the repository root first — it is the single source of truth
for architecture, build/test commands, and the messaging protocol between the WebView2 front-end
and the C# host.

Short version:

- Solution: `TrimCSS` (core engine, net10.0), `TrimCSS.GUI` (InfiniFrame desktop app,
  net10.0-windows + WinForms), `TrimCSS.Tests` (xUnit, net10.0-windows).
- JS ↔ C# IPC uses **InfiniFrame's v2 envelope protocol** on channels `trimcss` (requests) and
  `trimcss:response` (responses) — never `window.external.sendMessage` / `WebMessageReceived`,
  which silently drop non-envelope messages in InfiniFrame 0.62.1.
- InfiniFrame 0.62.1's native file dialogs are broken on Windows (sync deadlocks, async hangs);
  the folder picker is intentionally a WinForms `FolderBrowserDialog`. Do not "simplify" it back
  without re-testing against a newer InfiniFrame.
- Bridge actions must work headlessly via `GuiMessageBridge.ProcessRequestAsync` and have xUnit
  coverage in `TrimCSS.Tests`.
- Validate with `dotnet build` and `dotnet test` before finishing.
