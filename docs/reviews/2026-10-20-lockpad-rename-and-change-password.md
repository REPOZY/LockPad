# LockPad rename and Change master password: review record

Branch `feature/change-master-password` (it includes `feature/rename-lockpad`). Range `813de35..472497f`. All agents were AI-assisted (this is not a third-party audit).

| Pass | Result |
|---|---|
| Rename (`ce9beba`) | 889 tests passed; task review: Spec PASS, Quality PASS |
| Change master password (`fd7baad`, `2fe2019`) | 940 passed |
| Fix round 1 (`a6ea284`): wrong-current-password back-off, half-swap message | 949 passed |
| Task review | Spec PASS, Quality FAIL: 3 Important |
| Red team | 0 Critical, 2 High, 2 Medium, 5 Low; no lock-out sequence found |
| Fix round 2 (`d70e3c4`, `da0d00a`, `888e685`) | 975 passed |
| Re-review | Spec PASS, Quality PASS (5 Minor) |
| Re-attack | 0 Critical, 0 High, 1 Medium, 3 Low |
| Fix round 3 (`472497f`) | 982 passed, 0 failed, 0 skipped |

## Findings and dispositions
| Id | Sev | Finding | Disposition |
|---|---|---|---|
| Review B1 | Important | The change still ran after the idle or Windows lock obscured the editor while the dialog was open (walk-up re-key) | **Fixed** `d70e3c4`: obscuring abandons the dialog (boxes cleared, credentials disposed) and the editor re-checks after the dialog returns |
| RT H2 | High | A session end with the dialog open did not save dirty notes | **Fixed** `d70e3c4`: only the write phase counts as saving |
| RT H1 / Review B3 | High | "Re-locked at your next save" was false: a clean Ctrl+S writes nothing | **Fixed** `da0d00a`: a pending re-lock forces the next save (Save, lock, close, session end); the warning text is now true |
| RT M1 / Review B2 | Medium | A half-swap in the second write was misreported, and the in-memory copy could be dropped | **Fixed** `da0d00a`: a distinct status; the notebook stays pending, so a lock holds it |
| RT M2 | Medium | A `.bak` briefly held between the writes became an old-key `.bak.kept-*` under "Changed" | **Fixed** `da0d00a`: the previous key proves own-`.bak` ownership until the re-lock succeeds |
| Implementer | - | The instant current-password check allowed fast guessing | **Fixed** `a6ea284`: a 1/2/4/8 s back-off per notebook session |
| RT RM1 / Review m1 | Medium | Save As during a pending re-lock silently left an old-key `.bak` | **Fixed** `472497f`: a warning naming the file; nothing is deleted automatically |
| Review m4 | Minor | The half-swap text did not mention Save As | **Fixed** `472497f` |
| Review m3 | Minor | The below-floor test did not prove the early policy ordering | **Fixed** `472497f`: the redundant step was removed; the test is now an outcome test |
| Review A1, A2, B5, B7 | Minor | Weak tests, a case-sensitive guard, a back-off bypass via the public constructor | **Fixed** `da0d00a` / `888e685` |
| Review open question | - | The same mistyped new password twice locks out both copies (the `.bak` is re-locked) | **Mitigated** `888e685`: Caps Lock warning |
| RT L4 | Low | A change makes notes deleted earlier unrecoverable from the `.bak` | **Mitigated** `888e685`: a note in the dialog |
| RT L1 / Review B6 | Low | A changed key file is reported as a wrong password | **Documented** (README) |
| Accepted | Low | LockPad killed before the re-lock save: the old-key `.bak` is kept aside as `.bak.kept-*` with the "not created by this notebook" notice (the id is inside the encrypted payload) | Accepted; pinned by a test |
| Accepted | Low | m2: a non-sharing I/O or ACL error on the old-key `.bak` keeps it aside; m5/L-R1: "unsaved changes" wording while only a re-lock is pending; L-R2, L-R3; RT L2 (strength estimator strings); RT L3 (two instances); RT L5 (rename resets settings) | Accepted: the same class as normal saves, safe-side wording, harmless, or out of scope |

## Not covered by automated tests
These are in the manual checklist:
- dialog layout, focus and the Caps Lock key path;
- the wait cursor;
- the new warning and error dialogs;
- a real Win32 half-way `File.Replace`;
- KeePassXC showing the master-key change date.
