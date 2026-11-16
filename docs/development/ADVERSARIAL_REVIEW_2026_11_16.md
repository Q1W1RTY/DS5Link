# Adversarial review — 2026-11-16

Follow-up: R1–R4 were fixed after this review. The two commands below now run regression checks and should pass with the fixes in place. The original observations are retained below.

Scope: current working tree, main Avalonia application, native bridge control/input/effects paths, installer/update/uninstall, diagnostics and test entry points. Existing engine modifications and untracked preview projects were present before review. No release, installed application, real controller configuration or production service was changed. Experimental WPF/Web/AvaloniaPreview implementations were not audited as release code.

This is a bounded review, not proof that every defect has been found. Production fixes are not included. Two isolated reproductions below intentionally demonstrate existing bad behavior; their successful exit does **not** mean the product passes these requirements.

## Findings, ordered by impact

### R1 — P2: an obsolete reboot marker stops normal update recovery

Location: `installer/Install.ps1:59`, `:67`, `:149`, `:215`.

Trigger: an earlier driver installation returned 3010 and created `ProgramData/DS5Link/reboot-required.txt`; Windows has since rebooted; the user runs the in-app update. `UpdateOnly` checks existence only, sets `requiresReboot`, refreshes the old marker timestamp, stops the running bridge during replacement and skips both `Start-Service` and the control endpoint check. Setup returns 3010 again. The application then opens without a running bridge until another reboot/start. Subsequent updates repeat this because this branch never removes the marker.

Evidence: executed the actual assignment extracted from the installer with `RebootRequired=false`, `UpdateOnly=true` and mocked marker existence. Result: true, regardless of marker age. `WindowsDiagnostics.Reboot` already distinguishes markers predating boot, but the installer does not use that distinction. No real installation or reboot was performed.

Reproduce: `powershell -NoProfile -File scripts/review-reboot-marker.ps1`.

Fix direction: compare marker time with the current boot before honoring it; clear an obsolete marker and do not renew its age unless a new dependency installation requests reboot. Cover pre-boot, post-boot and absent-marker cases.

### R2 — P2: delayed touch events bypass the 150 ms freshness limit

Location: `src/DS5Link.Avalonia/InputControls.cs:47-51`.

`PollInput` starts its stopwatch **after** awaiting the response. The service's `AgeMs` describes age when the response was generated. Time between generation and consumption, including a delayed UI continuation, is omitted. An old action can therefore inject a key into a currently eligible foreground application, particularly when focus changes between applications using the common profile. State generation does not change merely because foreground focus changed.

Evidence: an isolated named pipe returned an already serialized event after 350 ms. The actual bridge deserializer and `InputEventCursor`, followed by the same age gate as `PollInput`, accepted the event at 386 ms. No keyboard input was emitted; foreground behavior is established by source analysis, not hardware acceptance.

Reproduce: `dotnet run --project tests/DS5Link.Tests/DS5Link.Tests.csproj -c Release -- --adversarial-review`.

Fix direction: capture a monotonic timestamp before the request and reject delayed batches conservatively; also track foreground identity across the request. `PollMouse` already measures request duration. Add delayed response/continuation and focus-change regression tests.

### R3 — P2: a damaged profiles file permanently disables in-app recovery

Locations: `src/DS5Link.Avalonia/GameProfileRuntime.cs:68,86`; `src/DS5Link.Avalonia/GameProfileEditor.cs:21-22,29`.

Trigger: invalid JSON or an invalid schema/content in `game-profiles.json`. Initialization records the error and returns before starting the profile timer. The settings window disables both the profile bar and settings tabs. Import explicitly refuses to operate while the profile bar is disabled. Consequently a valid exported profile cannot be imported to repair the installation; the user must repair/rename the file externally and restart. There is no error-clear/reload path in this runtime.

Evidence: isolated malformed-file loading confirmed rejection. The permanent UI lockout and unavailable import are confirmed by control flow; the user's real profile file was not altered. The same managed review command includes the isolated loading check.

Fix direction: preserve the damaged file, expose explicit recovery/import, clear the load error after successful validation, and start/restart profile application. Do not silently replace user data with defaults.

### R4 — P3: resetting effects preserves the previous audio source

Location: `src/DS5Link.Avalonia/EffectSettings.cs:76-89,110`.

`ResetEffects` passes default options to `LoadEffectDraft`, but that method does not set `AudioDevice.SelectedItem`. Saving immediately reads the old selection into `AudioDeviceId`. Choosing a fixed device or game-only audio, then resetting effects, therefore leaves that source in the persisted profile. Re-enabling audio later still uses it instead of the default Windows output; a removed device can remain selected after reset.

Evidence: source-confirmed; not a physical audio-device test. Profile switching handles the selection separately through `LoadAudioDevices`, while reset does not.

Fix direction: reset the source selection along with the other fields, suppressing intermediate autosaves; verify reset from explicit-device and game-only selections.

## Verification performed

- `scripts/test.ps1` passed: 13/13 native tests; managed output reports 57 protocol/diagnostics/input/waveform/auto-apply scenarios, 13 profile scenarios, three partial-touch cases and additional mouse/updater/localization checks.
- Avalonia Release build: zero warnings and errors. UI QA report `ok:true`: all six settings pages, 14 state/mode combinations, battery states, language variants and window restoration. These are synthetic UI checks.
- Dependency tests passed with a mocked driver inventory; 21 ownership-removal checks and backup-retention scenarios passed. The existing ownership test intentionally retains fixtures in TEMP; no cleanup outside project artifacts was performed.
- `scripts/build.ps1 -NativeOnly` completed after the baseline test run; Ninja found no native compilation work beyond build-info generation. Native checks therefore correspond to the current compiled source state.
- New opt-in delayed-input reproduction and obsolete-marker reproduction both confirmed the defects.

## Remaining acceptance gaps

No real Bluetooth/USB reconnect, sleep/resume, controller power cycle, multi-hour haptics session, service crash injection, installation rollback, live update/UAC or physical audio-device switching was performed. Existing passing tests do not substitute for those checks. No claim of absence of security bugs or exhaustive coverage is made. Manual-effects polling races and install rollback failure paths received source inspection but were not promoted to findings without sufficient concrete evidence.

Changes made for this review: this report, an opt-in managed reproduction file/entry point, and the read-only installer-expression reproduction script. No product fixes or new release packages.
