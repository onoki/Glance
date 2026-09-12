import assert from "node:assert/strict";
import { isAllowedExternalTarget, normalizeExternalTarget } from "../src/utils/externalLinks.js";

assert.equal(normalizeExternalTarget("https://internal.example.com/app?id=42"), "https://internal.example.com/app?id=42");
assert.equal(normalizeExternalTarget("www.example.com/path"), "https://www.example.com/path");
assert.equal(normalizeExternalTarget("example.com/path"), "https://example.com/path");
assert.equal(normalizeExternalTarget("P:\\Reports\\Weekly report.pdf"), "file:///P:/Reports/Weekly%20report.pdf");
assert.equal(normalizeExternalTarget("\\\\company-server\\shared docs\\Status ü.pdf"), "file://company-server/shared%20docs/Status%20%C3%BC.pdf");
assert.equal(normalizeExternalTarget("file:///C:/Docs/Report.pdf"), "file:///C:/Docs/Report.pdf");

assert.equal(isAllowedExternalTarget("javascript:alert(1)"), false);
assert.equal(isAllowedExternalTarget("data:text/html,hello"), false);
assert.equal(isAllowedExternalTarget("relative/file.pdf"), false);
assert.equal(isAllowedExternalTarget("C:\\Tools\\run.exe"), false);
assert.equal(isAllowedExternalTarget("\\\\server\\share\\script.ps1"), false);
assert.equal(isAllowedExternalTarget("file:///C:/Docs/report.pdf#fragment"), false);
assert.equal(isAllowedExternalTarget("\\\\?\\C:\\Windows\\file.txt"), false);

