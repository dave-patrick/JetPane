# Releases

1. Update project Version and CHANGELOG. Update source help and copy it to docs/help.html.
2. Run `pwsh ./scripts/build.ps1 -Verify` on Windows and `node scripts/check-site.mjs`.
3. Review staged files for personal settings, paths, credentials or logs. Keep generated binaries outside Git.
4. Commit and push. Create a GitHub release tagged `vX.Y.Z`, attaching artifacts/JetPane-portable.zip and SHA256SUMS.txt. Use a release notes file rather than shell-built multiline text.
5. Confirm the release’s assets and the GitHub Pages deployment. The initial release is a prerelease while docking/login/third-party compatibility coverage remains limited.

```powershell
gh release create v1.1.0 artifacts/JetPane-portable.zip artifacts/SHA256SUMS.txt --prerelease --title 'JetPane 1.1.0' --notes-file documentation/RELEASE-NOTES-1.1.0.md
```

The tag-push release workflow builds with -Verify and publishes portable prerelease assets for future `v*` tags. Builds from ordinary source pushes are retained as workflow artifacts. Review the release notes and update the website's versioned link when promoting a new version.

The Pages workflow needs GitHub Pages configured for GitHub Actions. The website uses a versioned initial download URL so prerelease status does not break download links; GitHub's `releases/latest` endpoint excludes prereleases.
