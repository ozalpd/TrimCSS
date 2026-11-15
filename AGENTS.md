# TrimCSS — AI Developer Guide

Context for AI coding agents working in this repository. Read this before making changes.

## What this project is

TrimCSS is a Windows desktop utility that scans web project source files (Razor `.cshtml`, HTML, JS/TS)
to find unreferenced CSS selectors and prune dead styles. It consists of a headless scanning engine
(class library) and an InfiniFrame-hosted desktop GUI whose UI is plain HTML/CSS/JS running in a
WebView2 window.

## Solution layout

```
TrimCSS.slnx
└── src/
	├── TrimCSS/              # Core engine, plain class library, net10.0
	│   ├── Contracts/        # ICssParser, ICssScanner, ISourceScanner
	│   ├── Engine/           # ProjectScanner (orchestrates a scan)
	│   ├── Models/           # CssSelectorInfo, ScannedToken, ScanResult
	│   └── Parsers/          # CssParser, HtmlRazorScanner, JsScanner
	├── TrimCSS.GUI/          # Desktop app, WinExe, net10.0-windows (Windows Forms enabled)
	│   ├── Program.cs        # Entry point: builds InfiniFrameWindow, registers bridge
	│   ├── Bridge/
	│   │   └── GuiMessageBridge.cs   # JS <-> C# IPC bridge (see "Messaging protocol" below)
	│   └── wwwroot/          # Front-end: index.html, styles.css, app.js (vanilla JS, no build step)
	└── TrimCSS.Tests/        # xUnit tests, net10.0-windows
```

Key NuGet packages:
- `TrimCSS`: AngleSharp + AngleSharp.Css (CSS/DOM parsing), Esprima (JS AST).
- `TrimCSS.GUI`: **InfiniLore.InfiniFrame 0.62.1** (+ `.SingleFile`) — WebView2-based windowing, a Photino fork/rewrite.

## Build, run, test

```powershell
dotnet build                                              # whole solution
dotnet run --project src/TrimCSS.GUI                      # run the GUI (Debug)
dotnet test                                               # all 37 xUnit tests
```

In Visual Studio, tests run via Test Explorer. The GUI exe lands in
`src/TrimCSS.GUI/bin/Debug/net10.0-windows/TrimCSS.GUI.exe`.

**TFM note:** `TrimCSS.GUI` and `TrimCSS.Tests` target `net10.0-windows` because the GUI uses
Windows Forms for its folder-picker dialog (see pitfalls below). The core `TrimCSS` library stays
on plain `net10.0`. Do not reference the GUI project from anything that must remain cross-platform.

## Architecture: how the front-end talks to C#

`wwwroot/app.js` (browser context) never touches the filesystem or engine directly. Everything goes
through `GuiMessageBridge` using **InfiniFrame's v2 envelope protocol** — this is load-bearing; see
the pitfalls section before touching it.

Channels (message envelope `id`):
- `trimcss` — JS → C# requests. Envelope `data` is a JSON string: `{ "action": string, "payload": any }`.
- `trimcss:response` — C# → JS responses. Envelope `data` is a `BridgeResponse`:
  `{ "action", "success", "data", "errorMessage" }` (camelCase).

Supported actions today:
- `selectFolder` — opens a native folder picker, responds with `{ folderPath: string | null }`
  (`null` when the user cancels).
- `scanProject` — payload `{ folderPath: string }`, responds with a `ScanResult` or an error message.

C# side: `GuiMessageBridge.Register(builder)` hooks `builder.RegisterWebMessagePostHandler("trimcss", ...)`
**before** `builder.Build()`. Responses are sent via `window.Features.WebMessaging.SendWebMessage(...)`
wrapped in a hand-built envelope.

JS side (`app.js`): `sendToCSharp(action, payload)` posts via
`window.infiniframe.messaging.sendMessageToHost(...)`; incoming responses are received through
`window.infiniframe.messaging.assignMessageReceivedHandler('trimcss:response', ...)` and dispatched
by action in `dispatch()`.

### Headless test seam

`GuiMessageBridge.ProcessRequestAsync(BridgeRequest)` processes requests without any window —
that is how `GuiMessageBridgeTests` exercise the bridge. When adding a new action, implement it so
it is reachable from both `HandleMessageAsync` (windowed) and `ProcessRequestAsync` (headless), and
add xUnit tests through `ProcessRequestAsync`.

## ⚠️ InfiniFrame 0.62.1 pitfalls (learned by painful debugging — do not regress)

1. **Plain JSON over `window.external.sendMessage()` goes nowhere.** `OnWebMessageReceived` in
   InfiniFrame only raises the raw `WebMessageReceived` event for Blazor (`__bwv:`) messages;
   anything that isn't a valid v2 envelope (`id` string + `version: 2` + `command: "Post"|"Get"`)
   is logged as "Rejected invalid web message" and silently dropped. Always use
   `window.infiniframe.messaging.sendMessageToHost(...)` (JS) and `RegisterWebMessagePostHandler`
   (C#).

2. **`window.external.receiveMessage` is a registrar, not a handler slot.** In InfiniFrame's JS
   bridge it is `receiveMessage(callback)` — assigning a function to it (Photino-style) breaks the
   bridge. Use `assignMessageReceivedHandler`.

3. **InfiniFrame's own file dialogs are broken on Windows in 0.62.1.** Verified empirically:
   - The sync `ShowOpenFolder()` (which the JS feature bridge
	 `window.infiniframe.window.features.filePickerDialogs` routes to) **deadlocks** when invoked
	 from the web-message handler thread.
   - The async `ShowOpenFolderAsync()` **never completes and never shows a window** — no exception,
	 no response.
   This is why `GuiMessageBridge.HandleSelectFolderAsync` shows a WinForms `FolderBrowserDialog`
   directly on the STA UI thread on Windows (`OperatingSystem.IsWindows()`), keeping the InfiniFrame
   async call only as a non-Windows fallback. If you upgrade InfiniFrame, re-test the native dialog
   before removing the workaround.

4. **The web-message handler runs on the UI thread** (STA, since `Main` is `[STAThread]`). Blocking
   work there freezes the window; `scanProject` offloads to `Task.Run` for this reason.

5. **DEBUG builds expose Chrome DevTools Protocol on port 1888**
   (`builder.SetRemoteDebuggingPort(1888)` + `EnableDevTools()`). Extremely useful for verifying UI
   behavior headlessly: `GET http://localhost:1888/json`, connect to the page's
   `webSocketDebuggerUrl`, then `Runtime.evaluate` to click buttons and read state.

## Conventions

- C# 12+ / .NET 10: implicit usings, nullable enabled, file-scoped namespaces.
- Bridge JSON is camelCase (`JsonNamingPolicy.CamelCase`, case-insensitive reads).
- Front-end is dependency-free vanilla JS with a `// ---` section-banner comment style; keep it
  build-free (no bundlers, no npm).
- `wwwroot/**` is copied to output (`PreserveNewest`); `Program.cs` resolves `index.html` from
  `AppContext.BaseDirectory`.

## Session artifacts

`session-state/` is agent workspace scratch (not source). Do not commit it.
