# JetPane 1.1.3

Fixes a reproduced UI freeze when another application is unresponsive. Cross-thread show, resize and stacking requests are queued instead of waiting on that application's message loop. This covers startup restoration, automatic restoration, profiles, snapping, swapping, locations and clean-desktop minimization.

Window scans now query executable paths with limited process access instead of inspecting the main module, and reuse process identity within each scan.

An owned stalled-window reproduction blocked the UI for 3,781 ms in 1.1.2 and returned in 12 ms with the fix on the development machine. Seven new regressions cover stalled targets, normal external placement, and minimized/maximized state. Total local self-tests: 57 across two monitors, including the existing 19 deterministic state checks. Portable startup trials did not establish a reliable packaging benefit, so the existing self-contained distribution format is retained.

The reported computer was not remotely inspected. These measurements demonstrate the reproduced defect and its fix, not that computer's launch time. Extract the full ZIP into a writable folder. Exit the old version and preserve settings.json when replacing it.
