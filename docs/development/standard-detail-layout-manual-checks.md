# Standard / Detail Layout Manual Checks

Use this checklist when changing the B standard layout or C detail layout.

## Standard Layout

- The app opens in the standard layout by default.
- The job rail, document area, AI assist rail, and slim audio player are visible.
- The `原文` / `整文` selector switches the central document area without opening the detail layout.
- In `原文`, segment search, speaker filtering, auto-scroll, playback, previous/next segment, waveform seeking, volume, and playback speed remain reachable.
- In `整文`, playback auto-scroll keeps the readable document near the current segment context when readable blocks are available.
- The standard export button and export menu follow the visible target: `整文` exports the readable document, and `原文` exports raw transcript content.
- The summary export entry remains reachable from the standard AI rail.

## Detail Layout

- Switching to the detail layout keeps the selected job and selected transcript tab.
- Detail tabs for `整文`, `素起こし`, `差分`, and `レビュー候補` remain available.
- The detail layout keeps the full audio player and the existing review panel workflow.
- Returning to the standard layout restores the standard document-focused composition and preserves the standard `原文` / `整文` target.

## Readable Document Visual QA

- 整文ビュー uses the configured readable font size and line height.
- The readable document keeps comfortable 余白 at narrow and wide widths.
- The readable document 本文幅 stays constrained by the readable panel max width and does not stretch into long unreadable lines.
- Title, status, and job メタ情報 remain above the document body and do not overlap the document text.
- The first viewport shows the document area and leaves the slim audio player reachable at the bottom of the standard layout.

## Regression Notes

- Verify these points after changing `MainWindow.xaml`, `ReadablePolishedPanel`, `TranscriptSegmentList`, `TranscriptAudioPlayer`, or export target logic.
- Automated coverage lives mainly in `StandardLayoutShellTests`, `MainWindowViewModelCoreTests`, and `MainWindowViewModelJobTests`.
