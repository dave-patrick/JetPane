# JetPane 1.1.4

Adds Check for updates to the tray menu and Settings → About & help. Checks run only on request and include published prereleases. Download update verifies the GitHub archive's SHA-256 checksum and executable version. Restart and install saves settings, closes JetPane, installs through a separate helper and restarts it.

The installer replaces only JetPane.exe, Help.html, README.md and LICENSE. Settings, profiles and unrelated files stay in place. A .jetpane-backup-* folder preserves the previous app and a settings recovery copy; interrupted file replacement is rolled back. Update failures remain local in update.log. Downloads are cancellable and use a temporary Windows folder. No background startup checks or telemetry.

Validation includes release/version selection, cancellation and HTTP failures, bad checksums, unsafe/incomplete archives, settings preservation, rollback, staged-file tampering, and a real helper handoff/restart into an isolated smoke instance. Versions before 1.1.4 need one manual upgrade to gain the updater. Keep the executable named JetPane.exe and its folder writable. In-app installation runs with normal user permissions; elevated instances and protected folders need a manual upgrade.
