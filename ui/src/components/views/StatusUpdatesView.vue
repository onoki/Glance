<template>
  <section class="status-view">
    <header>
      <h2>Project status update</h2>
      <p>Collect a read-only input package, complete its output in your ChatGPT project, then upload it here for validation and Office export.</p>
    </header>

    <div class="status-grid">
      <article class="status-card">
        <h3>1. Collect input</h3>
        <label>Project name <input v-model="projectName" /></label>
        <label>New evidence starts <input v-model="fromLocal" type="datetime-local" /></label>
        <label>Context starts <input v-model="contextLocal" type="datetime-local" /></label>
        <p class="hint">Leave the first date blank to continue from the prior completed report (four weeks on the first run). Context defaults to four weeks. A rerun replaces this day’s earlier package.</p>
        <button class="ghost" :disabled="busy" @click="authenticate">Microsoft sign in / test</button>
        <button class="add-task" :disabled="busy" @click="collect">Collect and replace today’s input</button>
      </article>

      <article class="status-card">
        <h3>2. Complete externally</h3>
        <button class="add-task" :disabled="!overview.latest || busy" @click="downloadJson">Download StatusSummary.json</button>
        <button class="ghost" @click="downloadSchema">Download schema</button>
        <p class="hint">Only edit the output section. Glance verifies the report ID, revision, schema, and unchanged input when it comes back.</p>
      </article>

      <article class="status-card">
        <h3>3. Validate and upload</h3>
        <input ref="fileInput" type="file" accept="application/json,.json" @change="upload" />
        <p class="hint">A superseded same-day input is rejected, preventing accidental export from stale source data.</p>
      </article>

      <article class="status-card">
        <h3>4. Export</h3>
        <button class="add-task" :disabled="overview.latest?.documentStatus !== 'completed' || busy" @click="downloadExcel">Excel (one worksheet)</button>
        <button class="add-task" :disabled="overview.latest?.documentStatus !== 'completed' || busy" @click="downloadPowerPoint">PowerPoint (one slide)</button>
      </article>
    </div>

    <section class="status-details">
      <h3>Current package</h3>
      <p v-if="overview.latest"><strong>{{ overview.latest.reportDate }}</strong> · revision {{ overview.latest.inputRevision }} · {{ overview.latest.documentStatus }}</p>
      <p v-else>No package collected yet.</p>
      <p>Azure DevOps: <span :class="overview.azureDevOpsConfigured ? 'ok' : 'muted'">{{ overview.azureDevOpsMessage }}</span></p>
      <p>Outlook: <span :class="overview.outlookConfigured ? 'ok' : 'muted'">{{ overview.outlookMessage }}</span></p>
      <p v-if="overview.preview" class="source-counts">Collected input: {{ overview.preview.glanceItemCount }} Glance topics · {{ overview.preview.azureWorkItemCount }} work items · {{ overview.preview.outlookMessageCount }} emails</p>
      <p v-if="message" class="operation-message">{{ message }}</p>
    </section>

    <section v-if="overview.latest?.documentStatus === 'completed' && overview.preview" class="output-preview">
      <h3>Validated output</h3>
      <h4>Project status</h4>
      <p class="long-text">{{ overview.preview.output.projectStatus }}</p>
      <h4>Risks</h4>
      <p v-if="!overview.preview.output.risks.length">No risks reported.</p>
      <div v-for="risk in overview.preview.output.risks" :key="risk.id" class="preview-item"><span v-if="risk.isNew" class="new-label">NEW</span> <strong>{{ risk.title }}</strong> — {{ risk.description }}</div>
      <h4>Open questions</h4>
      <p v-if="!overview.preview.output.openQuestions.length">No open questions reported.</p>
      <div v-for="question in overview.preview.output.openQuestions" :key="question.id" class="preview-item"><span v-if="question.isNew" class="new-label">NEW</span> {{ question.question }}</div>
    </section>
  </section>
</template>

<script setup>
import { onMounted, ref } from "vue";
import { authenticateStatusSources, collectStatusInput, downloadStatusExcel, downloadStatusJson, downloadStatusPowerPoint, downloadStatusSchema, fetchStatusOverview, importStatusSummary } from "../../api/statusUpdates.js";

const overview = ref({ latest: null, azureDevOpsConfigured: false, outlookConfigured: false, azureDevOpsMessage: "", outlookMessage: "" });
const projectName = ref("Project");
const fromLocal = ref("");
const contextLocal = ref("");
const busy = ref(false);
const message = ref("");
const fileInput = ref(null);
const toIso = (value) => value ? new Date(value).toISOString() : null;
const reload = async () => {
  overview.value = await fetchStatusOverview();
  if (overview.value.preview?.projectName) projectName.value = overview.value.preview.projectName;
};
const run = async (action, success) => {
  busy.value = true;
  message.value = "";
  try { await action(); message.value = success; await reload(); }
  catch (error) { message.value = error?.message || "The operation failed."; }
  finally { busy.value = false; }
};
const collect = () => run(() => collectStatusInput({ projectName: projectName.value, fromUtc: toIso(fromLocal.value), contextFromUtc: toIso(contextLocal.value) }), "Today’s input package is ready to download.");
const authenticate = () => run(authenticateStatusSources, "Microsoft authentication succeeded.");
const upload = async (event) => {
  const file = event.target.files?.[0];
  if (!file) return;
  await run(() => importStatusSummary(file), "The completed output is valid and ready for export.");
  if (fileInput.value) fileInput.value.value = "";
};
const downloadJson = () => run(downloadStatusJson, "StatusSummary.json downloaded.");
const downloadSchema = () => run(downloadStatusSchema, "Schema downloaded.");
const downloadExcel = () => run(downloadStatusExcel, "Excel file downloaded.");
const downloadPowerPoint = () => run(downloadStatusPowerPoint, "PowerPoint file downloaded.");
onMounted(reload);
</script>

<style scoped>
.status-view { padding: 6px; width: 100%; height: 100%; overflow: auto; box-sizing: border-box; font-size: var(--font-size-body); }
.status-view > * { max-width: 1150px; }
.status-view > header p { max-width: 850px; }
.status-view h2 { margin: 0 0 3px; font-size: var(--font-size-body); font-weight: 400; }
.status-view h3 { margin: 0 0 3px; font-size: var(--font-size-body); font-weight: 400; }
.status-view p { margin: 3px 0; }
.status-grid { display: grid; grid-template-columns: repeat(auto-fit, minmax(245px, 1fr)); gap: 4px; margin: 8px 0; }
.status-card { border: 1px solid var(--border-panel); background: var(--bg-panel); padding: 6px; display: flex; flex-direction: column; align-items: flex-start; gap: 5px; }
.status-card label { display: flex; flex-direction: column; gap: 3px; width: 100%; }
.status-card input { width: 100%; box-sizing: border-box; }
.hint, .muted { color: var(--text-muted); font-size: var(--font-size-meta); }
.ok { color: #386a4a; }
.operation-message { white-space: pre-wrap; padding: 8px; border-left: 3px solid #a86018; }
.output-preview { border-top: 1px solid var(--border-panel); margin-top: 18px; padding-top: 10px; max-width: 900px; }
.long-text { white-space: pre-wrap; }
.preview-item { margin: 6px 0; }
.new-label { font-size: var(--font-size-body); background: #a86018; color: white; padding: 2px 4px; }
</style>
