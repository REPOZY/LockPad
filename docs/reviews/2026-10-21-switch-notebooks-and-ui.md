# Switch notebooks and UI polish: review record

Branch `feature/switch-notebook-and-ui-polish`. Range `9c229fa..e3c4869`. All agents were AI-assisted (this is not a third-party audit).

| Pass | Result |
|---|---|
| Build: A `a958b7a` (File menu, switching, title); B `71b5109` (themed controls, high contrast, strength meter, empty states) | 1057 passed |
| Task review | A: Spec PASS, Quality PASS. B: Spec FAIL, Quality FAIL (keyboard focus contrast) |
| Red team | 0 Critical, 0 High, 1 Medium, 5 Low (reasoning only) |
| Fix round 1 (`011d655`, `5b3d28f`, `87248d0`) | 1091 passed |
| Re-review | A and B: Spec PASS, Quality PASS (5 Minor) |
| Re-attack | 0 Critical, 0 High, 0 Medium, 5 Low (reasoning only; skeletons turned into tests) |
| Fix round 2 (`e3c4869`) | 1099 passed, 0 failed, 0 skipped |

## Findings and dispositions
| Id | Sev | Finding | Disposition |
|---|---|---|---|
| Review R1 / RT M1 | Important | The keyboard position in menus and ComboBox lists was 1.07-1.45:1 | **Fixed** `011d655`: a 1 px focus ring on the highlighted item (3.7:1 or better) |
| Review R2 | Important | The Light Primary button focus ring was invisible (1.00:1) | **Fixed** `011d655`: a 2 px gap between the ring and the button |
| Review R3 | Important | The contrast test was a hand-picked list | **Fixed** `011d655`: the test parses every trigger brush in Controls.xaml and checks it against its surface |
| RT L1 | Low | A refused chosen path was rejected only after the current notebook was closed | **Fixed** `5b3d28f`: the path is checked before closing |
| Review R5 | Minor | The switch prompt said "Cancel keeps the window open" | **Fixed** `5b3d28f` |
| Review R6 / M2, RT L3 | Minor | The per-second menu refresh could block or throw out of the timer | **Fixed** `5b3d28f`, `e3c4869`: it runs after the lock decision and its exceptions are swallowed |
| Review R7 | Minor | Misleading empty states (filter, read-only) | **Fixed** `5b3d28f` |
| RT L4 | Low | A disposed notebook kept its notes in memory | **Fixed** `5b3d28f`: Dispose clears them; verified that no path saves a disposed notebook |
| Review R4 / RT L3, L4 | Minor | Stale colours between high-contrast schemes; excessive re-applies | **Fixed** `87248d0`, `e3c4869`: re-applied on Windows colour changes, only when the colours changed, with one late re-check |
| RT L1 (re-attack) | Low | A re-entrant close during a session-end save could show a disposed notebook as held | **Fixed** `e3c4869`: a session-save guard; Hold never shows a disposed notebook |
| Review M1 / RT L5 | Minor | Focus rings touched neighbouring sidebar buttons; wrong comment | **Fixed** `e3c4869`: 8 px gaps; comment corrected |
| Accepted | Low | The title shows the notebook file name to other programs while unlocked (requested feature); a teardown exception in TryClose leaves a blank window (existing); a file vanishing between the check and the unlock page; a parent-folder link to a share (existing residual); two rings in an open menu; the App wiring checked as source text; the built-in TextBox context menu stays system-styled | Recorded |

## Not covered by automated tests
These are in the manual checklist:
- real rendering in both themes;
- real high contrast and scheme switches;
- the context menu;
- keyboard focus visibility on screen.
