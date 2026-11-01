// Compatibility only: preserve retired report attributes when old notes are edited.
import { Extension } from "@tiptap/core";

const renderMetadata = (attributes) => {
  const html = {};
  if (attributes.glanceId) html["data-glance-id"] = attributes.glanceId;
  if (attributes.statusInputAtUtc) html["data-status-input-at-utc"] = attributes.statusInputAtUtc;
  return html;
};

export const StatusMetadata = Extension.create({
  name: "statusMetadata",
  addGlobalAttributes() {
    return [{
      types: ["paragraph", "listItem"],
      attributes: {
        glanceId: {
          default: null,
          parseHTML: (element) => element.getAttribute("data-glance-id"),
          renderHTML: renderMetadata
        },
        statusInputAtUtc: {
          default: null,
          parseHTML: (element) => element.getAttribute("data-status-input-at-utc"),
          renderHTML: renderMetadata
        }
      }
    }];
  }
});
