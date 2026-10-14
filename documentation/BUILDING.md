# Building and verifying

Use Windows x64 with .NET 10 SDK and PowerShell 7. The app has no third-party NuGet dependencies; publishing downloads official .NET runtime packs.

```powershell
pwsh ./scripts/build.ps1 -Verify
```

Outputs are ignored under artifacts/: a self-contained single-file executable, help, README, portable ZIP and SHA256SUMS.txt. Source is distributed by GitHub’s automatic source archive rather than committing binaries into Git history.

`-Verify` launches `--self-test`: geometry/configuration/shortcut tests plus temporary native test forms that move only themselves. It also runs `--smoke-test` in a separate folder. The latter briefly registers configured defaults and releases them at exit. Neither enables startup or manages normal user windows. Tests require a Windows desktop environment; a failure in a noninteractive host must not be reported as a product test pass.

```powershell
node scripts/check-site.mjs
```

This checks local website assets, anchors and expected publication files. Preview docs/ with any static server; browser UI validation is separate from structural checks.

GitHub Actions builds on windows-latest and retains the portable artifact. The Pages workflow publishes docs/ only. See [RELEASING](RELEASING.md) for publishing downloads.
