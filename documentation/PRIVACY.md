# Local data and privacy

JetPane contains no analytics. Only manual update checks and downloads make application network requests, to the public dave-patrick/JetPane GitHub release feed and GitHub download servers. These requests include normal HTTP metadata and the JetPane version; they do not send settings, profiles, application lists or logs. No update request runs on startup. Profile entries store process names, executable paths, pixel geometry, state, stack order and observation timestamps. Locations store rectangles, monitor geometry and names. Settings also contain preferences, exclusions and shortcuts.

Data is stored as settings.json beside the executable, with settings.json.bak and an optional pre-import backup. crash.log is created only for exceptions and remains local. Manual updates use a temporary Windows folder for downloads and installer files. Recovery copies remain beside the app in .jetpane-backup-* folders; update.log records installer failures locally. Review these files before sharing; paths or diagnostic text may be private. They are ignored by Git and never included in normal release packages.

Optional login startup adds a current-user Windows Run entry with the executable path. Disable it before moving/removing the folder. No account or cloud storage is needed by the app. GitHub hosting and downloads have their own service policies.
