# JetPane repository context

Maintained Windows .NET 10 WinForms source lives in src/JetPane. Build and package with scripts/build.ps1; -Verify runs native self-tests and an isolated smoke test. Never manage user windows in tests. Preserve schema-1 JSON/.wpilot compatibility. Settings, logs, runtime outputs and graft cache are ignored.

Profiles, snapping and locations are separate modes. Monitor-geometry assignments take startup priority. Startup registry changes must be explicit. Keep source Help.html and docs/help.html identical.

Static GitHub Pages landing page lives in docs, with an illustrative browser-only arrangement demo. Validate using node scripts/check-site.mjs. No backend or dependency installation is needed. Windows build and Pages deploy workflows live in .github/workflows. Portable binaries are release assets, never Git blobs. Preserve the existing MIT license. Approved raster branding is in assets/brand; keep prior explorations in assets/design-archive distinct. Documentation must distinguish tests from unverified docking/login/application behavior.

- The app publisher is Atomic 22 Industries. Keep publisher.json, package metadata and first-party app/site credits consistent; preserve existing licence and third-party attribution.

- Public landing pages use dark app-specific compositions with real approved brand assets; shared Atomic 22 navigation links to the blue Notes directory. Publish only docs/ via GitHub Pages. Never serve or publish private workspace data.

- StateRules.cs centralizes snap exit, profile shortcut assignment, pending restore consumption and auto-save eligibility. StateTests.cs regression checks run with --state-test and are also included in --self-test. Both build and release workflows require build.ps1 -Verify, fresh evidence and bounded test processes. Smoke uses its own mutex, isolated settings and a dedicated test hotkey.
