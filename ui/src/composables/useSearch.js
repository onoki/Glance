import { ref } from "vue";
import { searchTasks } from "../api/search.js";
import { tokenizeQuery } from "../utils/searchUtils.js";
import { highlightTask, normalizeTask } from "../utils/taskUtils.js";

export const useSearch = () => {
  const searchQuery = ref("");
  const searchResults = ref([]);
  const hasSearched = ref(false);
  const isSearching = ref(false);
  const searchInputRef = ref(null);

  const searchTasks = async () => {
    const query = searchQuery.value.trim();
    if (!query) {
      hasSearched.value = false;
      searchResults.value = [];
      return;
    }
    isSearching.value = true;
    try {
      const data = await searchTasks(query);
      const terms = tokenizeQuery(data.query || query);
      searchResults.value = data.results.map((result) => {
        const task = highlightTask(normalizeTask(result.task), terms);
        return { ...result, task };
      });
      hasSearched.value = true;
    } catch (error) {
      const message = error instanceof Error ? error.message : "Search failed";
      window.alert(message);
    } finally {
      isSearching.value = false;
    }
  };

  return {
    hasSearched,
    isSearching,
    searchInputRef,
    searchQuery,
    searchResults,
    searchTasks
  };
};
