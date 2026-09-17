# Button inventory and harmonization proposal

Status: proposal for review, not a replacement for the current visual requirements.
Inventory based on the Glance source, September 2026.

## Current styles

- **Navigation tabs** (`.tab`, `.person-tab`): compact text, thin square border, transparent background; selected tabs have a pale filled background. Person tabs add a grip and tag dots. Base and compact styles both define these rules.
- **Filled action buttons** (`.add-task`): dark fill, light text, no border, 3px × 6px padding, usually weight 600. Used for Search, backups, update installation, status downloads and adding a person. Settings reduces their size; People now uses the common app font at weight 400.
- **Outlined text actions** (`.ghost`): square thin border, transparent fill, 12px text and 2px × 6px padding. Used for Expand, Rename, Archive, refresh and other secondary actions. Settings overrides them to 10px with 1px × 4px padding. The Tags disclosure has separate rules to imitate this style.
- **Task icon buttons** (`.task-icon-button` / `.task-tool`): 18px squares, panel-colored fill, thin border, small muted icons. Status and sent indicators add their own active colors.
- **Menu action buttons** (`.category-option`): panel fill, thin border, left-aligned 11.2px text, 2px × 6px padding. Used for category selection and send/history menu commands.
- **Small borderless actions** (`.tiny-action`): 10px muted text with 1px × 2px padding, used for Rename/Delete inside the tag menu.
- **Weekday toggles** (`.weekday-chip`): 11.2px text, panel fill, thin border, 2px × 6px padding. Selected days use dark fill and light text, unlike selected navigation tabs.
- **Destructive action** (`.danger-button`): red text and border, transparent fill, 10px text with 1px × 4px padding; used for whole-state restore. Task deletion and tag deletion currently use different visual treatments.
- **Warning/retry actions**: the warning-dismiss control is borderless and bold; task save/retry buttons have panel fill, a border and their own inherited 11.52px typography.
- **Empty-list entry target** (`.empty-new`): dashed border, a larger click area and a text cursor. It looks like a button but acts as an empty editor, so its distinct appearance has a purpose.

There are also dormant variants: the rich-text toolbar's `.tool` buttons (larger padding and inverted active state; toolbar is disabled by default), and the unreferenced `TaskItemActions.vue` component's older `.task-action` / drag-handle styles.

## Recommended shared system

Use one base component/style with **three visual variants**:

1. **Neutral** — the default for normal actions, tabs, menus and task tools. Thin square border, panel background, common app font, regular weight.
2. **Emphasized** — a filled variation for a selected tab/toggle or an occasional main action. Keep the same font, border width and dimensions as neutral; do not use bold to convey selection.
3. **Danger** — the same base geometry with red text/border for destructive or whole-state replacement actions. Keep the existing confirmation behavior.

Use one compact height (suggestion: 20px) and `--font-size-meta` for button text throughout, including Settings. An icon-only button is just a square shape option of the same base style, with an accessible name and tooltip. Menu entries can stretch horizontally without becoming another visual style. The Tags disclosure should use the same base rules as Rename and Archive.

Share hover, keyboard-focus, pressed, disabled and loading rules. Preserve clear selected/pressed semantics; remove the duplicated per-view font and padding overrides. Keep the empty-editor target distinct because it represents an insertion point, not a command. Retire unused button styles after checking their references.

This would keep the compact appearance while eliminating accidental differences in weight, typography, spacing and active colors. No app-wide restyling has been applied as part of this proposal.
