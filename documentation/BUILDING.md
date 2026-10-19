# Building and verifying

Use Windows x64 with .NET 10 SDK and PowerShell 7. The app has no third-party NuGet dependencies; publishing downloads official .NET runtime packs.

```powershell
pwsh ./scripts/build.ps1 -Verify
```

Outputs are ignored under artifacts/: a self-contained single-file executable, help, README, portable ZIP and SHA256SUMS.txt. Source is distributed by GitHub’s automatic source archive rather than committing binaries into Git history.

`-Verify` first launches `--state-test` for deterministic state regressions, then `--self-test`: geometry/configuration/shortcut/update tests plus temporary native test forms that move only themselves. It also runs `--smoke-test` in a separate folder. The latter uses its own mutex and a dedicated Ctrl+Alt+Shift+F24 test binding, checks registration, then releases it at exit. Each test has a 60-second timeout. Fresh build directories prevent stale reports from being packaged. Neither enables startup or manages normal user windows. Updater integration tests install only into a marked temporary fixture, wait for an owned parent probe to exit, and restart exclusively in isolated smoke mode. Tests require a Windows desktop environment; a failure in a noninteractive host must not be reported as a product test pass.

```powershell
node scripts/check-site.mjs
```

This checks local website assets, anchors and expected publication files. Preview docs/ with any static server; browser UI validation is separate from structural checks.

GitHub Actions builds on windows-latest with -Verify and retains the portable artifact. Tag releases require the same verification. The Pages workflow publishes docs/ only. See [RELEASING](RELEASING.md) for publishing downloads.
