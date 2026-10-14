# Architecture

`src/JetPane` is a .NET 10 WinForms application, with Windows API interop and no third-party runtime services.

| File | Role |
| --- | --- |
| Program.cs | Configuration, tray lifecycle, scanning, Win32 placement, profiles, snap slots, startup, persistence and self-tests |
| Preview.cs | Recorded display geometry, schematic canvas and arrangement preview |
| LocationCanvas.cs | Interactive region drawing, moving and eight-handle resizing |
| Locations.cs | Named locations, editing and active-window placement |
| Shortcuts.cs | Action catalogue, modifier/key parsing and duplicate detection |
| Help.html | Offline command guide shipped beside the executable |

The scanner polls about once per second. Windows are identified by process name for profiles. Placement uses normal bounds plus minimized/maximized state; off-screen coordinates are clamped to a connected work area. DPI awareness is declared in app.manifest. No elevation is requested.

Profiles and snapping are separate controlling modes. Locations issue an explicit one-window move. Monitor signatures use sorted screen rectangles. Assigned monitor profiles take startup precedence. Profile application launching uses saved executable paths, never recovered document/session state.

Schema 1 is additive and backwards-compatible with the earlier WindowPilot build. Configuration is atomic-replaced through a temporary file and a prior-save backup. Imports validate before replacement and keep the OS startup preference. Only an explicit startup menu action writes the current-user Run entry.

Custom shortcuts have stable action IDs. Locations use generated IDs, independent of their names. Profiles use name-based IDs that are updated on rename. Global registration uses MOD_NOREPEAT and reports OS conflicts. The prior WindowPilot mutex name intentionally remains to prevent competing old/new managers.

The website is static HTML/CSS/JS. Its demo uses synthetic example layouts and does not communicate with the native application. Production branding is separate from the exploratory design archive.
