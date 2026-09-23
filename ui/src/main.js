import { createApp } from "vue";
import App from "./App.vue";
import "./styles.css";
import { installPixelText } from "./utils/pixelText.js";

createApp(App).mount("#app");
const stopPixelText = installPixelText(document.getElementById('app'));
if (import.meta.hot) import.meta.hot.dispose(stopPixelText);
