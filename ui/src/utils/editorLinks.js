import { Extension } from "@tiptap/core";
import { Plugin } from "prosemirror-state";
import { Decoration, DecorationSet } from "prosemirror-view";

export const compactUrlLabel = (text, href) => {
  if (text.length <= 60) return null;
  try {
    const url = new URL(href);
    if (!/^https?:$/.test(url.protocol)) return null;
    if (text !== href && text !== href.replace(/^https?:\/\//, "")) return null;
    return `${url.protocol}//${url.host}/…`;
  } catch { return null; }
};

// A presentation-only decoration: saved text, clipboard data and href stay lossless.
export const CompactLinks = Extension.create({
  name: "compactLinks",
  addProseMirrorPlugins() {
    return [new Plugin({
      props: {
        decorations(state) {
          const ranges = [];
          state.doc.descendants((node, pos) => {
            const link = node.isText && node.marks.find((mark) => mark.type.name === "link");
            if (!link) return;
            ranges.push({ from: pos, to: pos + node.nodeSize, text: node.text, href: link.attrs.href });
          });
          return DecorationSet.create(state.doc, ranges.flatMap((range) => {
            const label = compactUrlLabel(range.text, range.href);
            return label ? [Decoration.inline(range.from, range.to, {
              class: "compact-url", "data-short-label": label, title: range.href
            })] : [];
          }));
        }
      }
    })];
  }
});

export const insertOutsideLink = (view, from, to, text) => {
  if (from !== to) return false;
  const { state } = view;
  const linkType = state.schema.marks.link;
  if (!linkType) return false;
  const position = state.doc.resolve(from);
  const before = position.nodeBefore?.marks.find((mark) => mark.type === linkType);
  const after = position.nodeAfter?.marks.find((mark) => mark.type === linkType);
  if ((!before && !after) || (before && after && before.eq(after))) return false;
  const marks = (state.storedMarks || position.marks()).filter((mark) => mark.type !== linkType);
  const tr = state.tr.insertText(text, from, to).removeMark(from, from + text.length, linkType);
  tr.setStoredMarks(marks).setMeta("preventAutolink", true);
  view.dispatch(tr);
  return true;
};
