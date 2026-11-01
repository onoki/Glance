# Privacy, completion days and safe activation

## Private subcontent

Use the lock in a focused task’s action bar to toggle **Private subcontent**. The title always stays visible. With global privacy on, its body is hidden until that task receives focus or its placeholder is activated. Moving focus to another task/control outside it, or switching to another application, conceals the body again. Returning to Glance alone does not reveal it; clicking or deliberately navigating to the task does. Concealment retains the original row height, including images, to avoid moving other tasks.

The top-bar lock toggles privacy for all flagged tasks in that window. Off reveals their bodies; on conceals unfocused bodies. Every new window starts on. Ordinary tasks are unaffected. Dashboard, People, Search and History share the same presentation. Read-only private results can receive focus to reveal their body.

Privacy survives restart, sent copies, recurring instances and whole-task clipboard operations. Splits/promotions inherit it; combining tasks protects the result if either task was private. Undo/Redo restores the flags with the associated operation. Existing tasks default to public; the additive migration leaves their documents and dates intact.

This protects the screen from casual viewing, not the stored data. Full text remains searchable and present in the local API, database, backups, clipboard and lossless export. Offline HTML exports place private bodies in collapsed disclosures. Anyone with those files can read the content.

## History rules

Checking a task records its completion instant. History lists completed, nondeleted tasks immediately, even while today’s completed tasks still appear in Dashboard or People. Activity shows the last **180 local calendar days including today**, counting each task on the day its completion timestamp falls in the current PC’s timezone. Creation date, scheduled day and archive time do not determine the count. Restoring/unchecking or deleting a task removes its count; checking an already completed task does not redate it.

**Move completed to history** removes today’s completed Dashboard and People notes from active lists. It records a separate archive timestamp and preserves completion time, person, schedule, privacy and documents. Repeating it is harmless. Unchecking a History task clears both completion and archive timestamps and restores it to its original list.

At local midnight, prior-day completions are archived automatically. Focus and visibility checks catch up after sleep or hibernation; a timer also retries after clock changes. Pending saves settle first. Failed saves or archive requests leave the day checkpoint unchanged for retry. Active-list queries also exclude earlier completions at startup. Midnight calculations use the timezone offset at that midnight, rather than the offset later in the day, which can differ on daylight-saving transition days.

Older builds implemented manual archiving by rewriting completion time to the end of yesterday. That explains activity moving to another day. The new archive field prevents this going forward. An already overwritten original completion instant cannot be reliably reconstructed, so the migration preserves existing dates rather than guessing.

## Links and Send

Editable links retain Ctrl+click (or Meta+click); read-only links use a normal click. Middle-click and Enter on a focused anchor also use the external opener. Activation captures the destination before editor focus expands shortened URLs, opens that exact target, then focuses the original editor if appropriate. Native navigation is cancelled, including plain editing clicks; background URL/file drops cannot replace Glance. Failed external opening reports an error without opening inside the app.

Send uses one multi-select menu. People includes **Send to Dashboard** first, followed by people and tag groups. The explicit confirmation sits outside the scrolling recipients, so it remains visible. A batch creates independent copies and audit records in one transaction; failures retain the selection. Clicking names toggles checkboxes and keeps the menu open. Outside click or Escape dismisses it.

## Regression coverage

- `nestedListSafety.test.js`: leading empty bullets, nested neighbours, rich content, caret and native Undo/Redo.
- `taskCategoryMove.test.js`, `privacy.test.js`: shared TaskItem conceal/reveal/save, window blur, global mode, unified Send and source highlight duration.
- `taskSafety.test.js`, `taskClipboard.test.js`: privacy during structural history and whole-task copying.
- `externalLinkActivation.test.js`: pre-focus destinations, native-navigation cancellation, editing, keyboard/middle clicks, background drops and failures.
- `dayRollover.test.js`, `HistoryPrivacyTests.cs`: midnight/wake recovery, retries, DST, archive idempotence, preserved completion days and additive migration.
- `PersonSendingTests.cs`, `PortableExportTests.cs`: batched destinations, copy privacy, full export coverage and collapsed HTML disclosures.
