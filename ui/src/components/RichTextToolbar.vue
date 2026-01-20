<template>
  <div class="toolbar">
    <button
      type="button"
      class="tool"
      :class="{ active: editor?.isActive('bold') }"
      @mousedown.prevent
      @click="toggleBold"
    >
      B
    </button>
    <button
      type="button"
      class="tool"
      :class="{ active: editor?.isActive('italic') }"
      @mousedown.prevent
      @click="toggleItalic"
    >
      I
    </button>
    <span class="tool-sep"></span>
    <button
      type="button"
      class="tool"
      :class="{ active: editor?.isActive('highlight', { color: 'green' }) }"
      @mousedown.prevent
      @click="toggleHighlight('green')"
    >
      Green
    </button>
    <button
      type="button"
      class="tool"
      :class="{ active: editor?.isActive('highlight', { color: 'yellow' }) }"
      @mousedown.prevent
      @click="toggleHighlight('yellow')"
    >
      Yellow
    </button>
    <button
      type="button"
      class="tool"
      :class="{ active: editor?.isActive('highlight', { color: 'red' }) }"
      @mousedown.prevent
      @click="toggleHighlight('red')"
    >
      Red
    </button>
  </div>
</template>

<script setup>
const props = defineProps({
  editor: {
    type: Object,
    default: null
  }
});

const toggleBold = () => {
  props.editor?.chain().focus().toggleBold().run();
};

const toggleItalic = () => {
  props.editor?.chain().focus().toggleItalic().run();
};

const toggleHighlight = (color) => {
  props.editor?.chain().focus().toggleHighlight({ color }).run();
};
</script>
