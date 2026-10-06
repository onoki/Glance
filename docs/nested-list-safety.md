# Nested subcontent deletion regression

The reported selection-delete / Backspace sequence was reproduced with real ProseMirror documents through the shared task key handler before applying the fix. The test failed because parent and sibling text disappeared.

## Cause and fix

Three handlers confused a nested list containing one empty item with an entirely empty subcontent document: task-level Backspace/Delete cleanup, last-item Enter, and the editor Backspace fallback. They called whole-document setContent. Besides discarding unrelated nodes, that path bypassed normal transaction-based editing updates.

Empty-item removal now deletes only the selected item, or its list wrapper if it was the final child, through a regular transaction. Backspace resolves the caret backward to the end of the preceding text line, including a parent or nested sibling; it falls forward only when there is no preceding line. Whole-subcontent cleanup requires the sole outer list; Enter-to-new-task applies only at the final outer list. A cleared parent with populated children is not empty.

An adjacent regression also showed that appending a list to a non-list document replaced existing paragraphs. The fallback now appends without replacing populated blocks.

## Verification

- Shared task-key-handler regression: delete selected nested text, then Backspace/Delete/Enter.
- 80 deterministic combinations: bullet/task lists, 1-5 levels, sole/first/middle/last item, with/without additional document blocks.
- Structural validity, exact preserved text, rich marks through Undo/Redo, whole-document selections, image-only items, populated descendants, and noncanonical paragraph-based content.
- Browser sequences in Dashboard and People; saved API results checked after navigation/reload. Parent, sibling and unrelated child text survived.

These are bounded regression and structure tests, not a claim of exhaustive malformed-document fuzzing.
