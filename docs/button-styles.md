# Compact visual system

The accepted style follows the IBM Plex Sans / Notepad font comparison. Glance uses visual hierarchy, restrained curves and thin separators while retaining its information density.

## Typography and surfaces

- Use IBM Plex Sans at 7 CSS px and 1.35 line height in all views. Plain task titles and subcontent share identical typography. Genuine rich-text bold/italic remains available; synthesis stays disabled.
- Section, column, person and History date headings use the genuine 600 weight at the same size, with sentence case and normal letter spacing. Settings subsection headings use 500. Ordinary controls and navigation remain 400, including selected tabs, so selection cannot change text width or wrapping.
- Retain the Notepad palette: #fafafa chrome, #ffffff task surfaces, #1a1a1a text, #5a5a5a secondary text, #cccccc control borders, #e5e5e5 selection and #202020 focus/link accents. Quieter structural separators use #e3e3e3. Semantic warning, danger, tag and rich-text highlight colors retain their meaning.
- Use white task surfaces with subtle column/header separators. History uses quiet chart gridlines; zero-completion days have zero-height bars, while positive counts retain a visible minimum. Helper windows retain their slightly darker header and plain Helper window label.

## Shared controls

Use the same base geometry with three variants:

1. **Neutral** for ordinary commands, menus and task tools: a thin border and small 3px corner radius; task tools and text fields use 2px corners.
2. **Emphasized** for main commands or selected controls. Main commands retain legible light text on a dark hover background. Selected navigation uses pale fill and an inset 2px bottom rule; inactive navigation drops its visible border without changing its dimensions.
3. **Danger** for destructive actions: red text/border and a restrained pale-red hover, with the same confirmation rules.

Keep 16px minimum command targets, existing 20px header icons, and existing button padding. Navigation and header tools retain 1px gaps. Zoom remains a single compact group. Text/search fields and selects align to the same 16px control height; the search field's former excess padding is reduced. All hover rules exclude disabled controls; keyboard focus remains explicit.

Menus use the same small radius and a subtle shadow. Menu headings use 600 weight; recipient and command labels stay regular. Send confirmation stays outside its scrolling recipient body. Keep the empty-editor insertion target distinct from commands and preserve its 64px hit area.

## Task ownership and density

The focused task has a 2px left rail connected to its existing full-width 1px toolbar line. Both are decorations outside layout; focus, blur, dragging, pending saves and menu state must not change task width, height, wrapping or hit targets. The task bar still appears only for the focused task and still has backgrounds only behind its date and controls.

Task row padding is 1px vertically in Dashboard, People, Search and History. Read-only rows lose their former extra padding. Subcontent lists share a 12px indent per nesting level in every tab, reclaiming the former 27px Dashboard/People indent. Visible completion checkboxes are 10px within their unchanged 12px hit area, centered on the first title line. The narrow whole-task grip remains 8px.

Do not add whitespace to achieve a modern appearance: preserve task gaps, line height, column widths, header heights and Settings group indentation. Narrow windows must not inherit the legacy 20px content padding or 12px navigation gap. Do not transform text or add text-origin correction.

## Maintenance and verification

`compact.css` owns font/palette tokens; `app.css` owns the existing layout; `controls.css` owns common geometry and interaction; `surfaces.css` owns the shared visual hierarchy. Put cross-view styling in these shared files rather than adding component-specific font families, extra padding or new button variants.

For visual changes, verify the computed font, row/header dimensions, checkbox/title centers, focus decorations and all five views using isolated data. Include a narrow window, check that disabled controls do not highlight, and retain connected focus/blur/drag behavior. Avoid tests that merely repeat CSS declarations; use actual layout checks and existing interaction regressions.
