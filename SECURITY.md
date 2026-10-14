# Security

Do not publish credentials, private logs, window titles or personal configuration in issues. For sensitive reports, use GitHub’s private vulnerability reporting if it is enabled; otherwise contact the repository owner without posting exploit details publicly.

JetPane runs as the current user. Do not disable Windows security controls or elevate the whole application solely to move another application’s elevated windows. Imported profiles can contain executable paths that launch when loaded: import only files you trust. The application does not execute shell command strings from configuration.

Portable release binaries are currently unsigned. Releases include SHA-256 checksums for byte integrity; checksums do not provide a signing identity. Startup uses a current-user Run entry only after an explicit menu choice.
