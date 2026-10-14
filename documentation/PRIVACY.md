# Local data and privacy

JetPane makes no application network calls and contains no analytics. Profile entries store process names, executable paths, pixel geometry, state, stack order and observation timestamps. Locations store rectangles, monitor geometry and names. Settings also contain preferences, exclusions and shortcuts.

Data is stored as settings.json beside the executable, with settings.json.bak and an optional pre-import backup. crash.log is created only for exceptions and remains local. Review these files before sharing; paths or diagnostic text may be private. They are ignored by Git and never included in normal release packages.

Optional login startup adds a current-user Windows Run entry with the executable path. Disable it before moving/removing the folder. No account or cloud storage is needed by the app. GitHub hosting and downloads have their own service policies.
