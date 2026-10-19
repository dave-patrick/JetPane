# Changelog

## 1.1.4 — 2026-10-19

- Add manual GitHub update checks from the tray menu and Settings.
- Download and verify updates without blocking the UI; support published prereleases.
- Install through a separate helper after JetPane exits, preserve settings and unrelated files, and restart.
- Keep recovery backups and roll back partial file replacements on failure.
- Add updater unit tests and an isolated installer/restart integration test.

## 1.1.3 — 2026-10-19

- Prevent stalled applications from blocking JetPane during startup restore, automatic placement, profile changes, snapping and clean switches.
- Queue cross-thread show/resize/reorder requests and keep same-thread verification deterministic.
- Replace repeated process-module inspection with limited-access executable queries, reused per process within each scan.
- Add owned stalled-window and responsive-window regressions, including minimized/maximized state.

## 1.1.2 — 2026-10-18

- Fix settings restart failures from inactive snap locks and conflicting Default profile spelling.
- Preserve unrelated custom shortcuts, defer restores through quiet periods, and keep excluded apps out of auto-save.
- Validate implicit numpad aliases and numeric profile assignments before saving.
- Reuse one window snapshot when swapping panes.
- Add 19 deterministic regressions; require fresh, bounded build/release verification and actual smoke hotkey registration.

## 1.1.1

- Publisher metadata, About/help credits and repository/site records now identify Atomic 22 Industries.


## 1.1.0 — 2026-10-14

Initial JetPane repository release, continuing the local WindowPilot prototype.

- Profiles spanning monitors, startup profile selection and monitor-geometry assignments.
- Halves/thirds/quadrants, slot locks, automatic filling, exclusions and slot memory.
- Read-only arrangement previews with a separate Apply control.
- Visual locations with drawing, dragging, eight resize handles and numeric editing.
- Editable action shortcuts, duplicate checks, registration feedback and AltGr disable switch.
- Portable JSON settings, backups, import/export and offline help.
- Approved folded-plane icon and uppercase wordmark with an integrated flight swoosh.
- Source, landing page, screenshot examples, documentation and build/deployment workflows.

This initial release retains known limitations described in the README and help.
