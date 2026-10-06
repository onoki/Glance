import { NodeSelection } from "prosemirror-state";
import Image from "@tiptap/extension-image";

export const boundedImageWidth = (requestedWidth, naturalWidth) => {
  if (!Number.isFinite(naturalWidth) || naturalWidth <= 0) return null;
  const width = Number.isFinite(requestedWidth) ? requestedWidth : naturalWidth;
  return Math.min(naturalWidth, Math.max(1, Math.round(width)));
};

const ResizableImage = Image.extend({
  addAttributes() {
    return {
      ...this.parent?.(),
      width: {
        default: null,
        parseHTML: (element) => {
          const attr = element.getAttribute("data-width");
          if (attr) {
            const value = Number.parseInt(attr, 10);
            return Number.isNaN(value) ? null : value;
          }
          const styleWidth = element.style?.width;
          if (styleWidth && styleWidth.endsWith("px")) {
            const value = Number.parseInt(styleWidth, 10);
            return Number.isNaN(value) ? null : value;
          }
          return null;
        },
        renderHTML: (attributes) => {
          if (!attributes.width) {
            return {};
          }
          return {
            "data-width": attributes.width,
            style: `width: ${attributes.width}px;`
          };
        }
      }
    };
  },
  addNodeView() {
    return ({ node, editor, getPos }) => {
      const wrapper = document.createElement("span");
      wrapper.className = "image-resize-wrapper";
      wrapper.contentEditable = "false";

      const img = document.createElement("img");
      img.src = node.attrs.src;
      if (node.attrs.alt) {
        img.alt = node.attrs.alt;
      }
      if (node.attrs.title) {
        img.title = node.attrs.title;
      }
      const applyWidth = () => {
        const width = boundedImageWidth(node.attrs.width, img.naturalWidth);
        // Before load, use intrinsic sizing rather than stretching an unknown image.
        wrapper.style.width = width === null ? "" : `${width}px`;
        img.style.width = width === null ? "auto" : "100%";
      };
      img.addEventListener("load", applyWidth);
      applyWidth();

      const handle = document.createElement("span");
      handle.className = "image-resize-handle";

      const updateSelected = () => {
        const selection = editor.state.selection;
        const isSelected = selection instanceof NodeSelection
          && typeof getPos === "function"
          && selection.from === getPos();
        wrapper.classList.toggle("is-selected", isSelected);
      };

      const selectNode = () => {
        if (typeof getPos === "function") {
          editor.chain().focus().setNodeSelection(getPos()).run();
        }
        updateSelected();
      };

      const onSelectionUpdate = () => updateSelected();

      img.addEventListener("click", selectNode);
      handle.addEventListener("click", selectNode);
      editor.on("selectionUpdate", onSelectionUpdate);

      let stopResize = null;
      const onPointerDown = (event) => {
        event.preventDefault();
        event.stopPropagation();
        if (!img.naturalWidth) return;
        stopResize?.();
        selectNode();
        const startX = event.clientX;
        const startWidth = wrapper.getBoundingClientRect().width || img.getBoundingClientRect().width;

        const onPointerMove = (moveEvent) => {
          const delta = moveEvent.clientX - startX;
          const nextWidth = boundedImageWidth(startWidth + delta, img.naturalWidth);
          wrapper.style.width = `${nextWidth}px`;
          if (typeof getPos === "function") {
            editor.commands.command(({ tr }) => {
              tr.setNodeMarkup(getPos(), undefined, { ...node.attrs, width: nextWidth });
              return true;
            });
          }
        };

        const onPointerUp = () => {
          if (typeof getPos === "function") {
            editor.chain().focus().setNodeSelection(getPos()).run();
            updateSelected();
          }
          document.removeEventListener("pointermove", onPointerMove);
          document.removeEventListener("pointerup", onPointerUp);
          document.removeEventListener("pointercancel", onPointerUp);
          stopResize = null;
        };

        stopResize = () => {
          document.removeEventListener("pointermove", onPointerMove);
          document.removeEventListener("pointerup", onPointerUp);
          document.removeEventListener("pointercancel", onPointerUp);
        };

        document.addEventListener("pointermove", onPointerMove);
        document.addEventListener("pointerup", onPointerUp);
        document.addEventListener("pointercancel", onPointerUp);
      };

      handle.addEventListener("pointerdown", onPointerDown);

      wrapper.appendChild(img);
      wrapper.appendChild(handle);

      return {
        dom: wrapper,
        update: (updatedNode) => {
          if (updatedNode.type.name !== node.type.name) {
            return false;
          }
          if (img.getAttribute("src") !== updatedNode.attrs.src) img.src = updatedNode.attrs.src;
          img.alt = updatedNode.attrs.alt || "";
          img.title = updatedNode.attrs.title || "";
          node = updatedNode;
          applyWidth();
          updateSelected();
          return true;
        },
        destroy: () => {
          stopResize?.();
          img.removeEventListener("load", applyWidth);
          handle.removeEventListener("pointerdown", onPointerDown);
          img.removeEventListener("click", selectNode);
          handle.removeEventListener("click", selectNode);
          editor.off("selectionUpdate", onSelectionUpdate);
        }
      };
    };
  }
});

export default ResizableImage;
