# Contributing

Open an issue describing the problem, Windows version, display geometry/scaling, affected process name, expected result and actual result. Never post personal settings, document titles, executable paths or logs without reviewing/redacting them.

Use .NET 10 on Windows. Build with `pwsh ./scripts/build.ps1 -Verify`. The self-test opens and moves only its own test windows. Manually validate changes to startup, monitor switching or third-party windows on your own test desktop before claiming those paths verified.

Keep profiles, snapping and locations distinct. Preserve schema-1 settings and `.wpilot` import compatibility. Startup changes must remain explicit user actions. Do not add telemetry or background networking. See architecture notes before changing Win32 behavior.

For a pull request, explain the concrete before/after behavior and relevant verification. Include synthetic screenshots for UI changes, never private desktop captures. Update help and documentation when behavior changes.
