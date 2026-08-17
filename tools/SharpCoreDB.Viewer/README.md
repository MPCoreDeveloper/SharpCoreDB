# SharpCoreDB.Viewer — DEPRECATED

> **⚠️ DEPRECATED** — This Avalonia desktop viewer is no longer the recommended tool.
>
> Use **[SharpCoreDB.WebViewer](../SharpCoreDB.WebViewer/README.md)** as the primary database studio.

## Why deprecated

The WebViewer offers:

- Same cross-platform reach (Windows / Linux / macOS) via any modern browser
- CSP A+ security via SafeWebCore (no inline scripts, no unsafe-eval)
- SSMS-style dark/light theming, menu bar, and keyboard shortcuts
- SQL syntax-highlighted editor (vanilla JS, CSP-safe — no dependencies)
- Table Designer and CSV Import workflows
- SharpDispatch-based background task dispatching
- Server + local connection modes

This project is retained for reference only and may be removed in a future major release.

## Known issue

The project currently does **not build** due to a pre-existing `AttachDevTools` error caused by the `Avalonia.Diagnostics` (11.3.18) and `Avalonia` (12.1.0) version mismatch in `Directory.Packages.props`. This further confirms the deprecation: no new work will be invested in fixing it. Use the WebViewer instead.
