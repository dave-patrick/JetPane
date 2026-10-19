# JetPane 1.1.2

Fixes two settings restart failures, accidental removal of unrelated profile shortcuts, lost automatic restores during quiet periods, excluded apps being re-saved, and implicit numpad shortcut collisions. Legacy inactive snap locks are cleared on load so affected settings can open again. Imports reject conflicting spellings of the reserved Default profile before replacement.

Profile shortcut assignments are validated before mutation. Swapping panes uses one window snapshot. Release workflows now require verification, use fresh output, enforce test timeouts, and check actual smoke-test hotkey registration with an isolated binding.

Validation: 19 deterministic state regression checks; 50 total self-test checks on a two-monitor desktop; isolated tray smoke test; website/help consistency. Physical docking, login startup and broad third-party application compatibility remain outside this verification. Portable Windows x64, self-contained .NET 10; schema-1 settings and .wpilot compatibility retained.
