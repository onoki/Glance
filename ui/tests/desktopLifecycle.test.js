import assert from "node:assert/strict";
import {
  InteractionQuiescence,
  restartForPendingRestore
} from "../src/services/desktopLifecycle.js";

const makeDocument = (initiallyInert = false) => {
  const attributes = new Map();
  const body = {
    inert: initiallyInert,
    children: [],
    appendChild(element) {
      this.children.push(element);
      element.remove = () => {
        this.children = this.children.filter((candidate) => candidate !== element);
      };
    }
  };
  return {
    body,
    documentElement: {
      setAttribute: (name, value) => attributes.set(name, value),
      removeAttribute: (name) => attributes.delete(name)
    },
    createElement: () => ({ dataset: {}, style: {}, setAttribute() {} }),
    attributes
  };
};

{
  const documentRef = makeDocument();
  const quiescence = new InteractionQuiescence(documentRef);
  quiescence.lock("first", "backup");
  quiescence.lock("second", "close");
  assert.equal(quiescence.locked, true);
  assert.equal(documentRef.body.inert, true, "the note UI is inert while any operation is active");
  assert.equal(documentRef.body.children.length, 1, "nested operations share one blocking overlay");

  quiescence.release("first");
  assert.equal(documentRef.body.inert, true, "one completion cannot unlock another operation");
  quiescence.release("second");
  assert.equal(quiescence.locked, false);
  assert.equal(documentRef.body.inert, false);
  assert.equal(documentRef.body.children.length, 0);
}

{
  const documentRef = makeDocument(true);
  const quiescence = new InteractionQuiescence(documentRef);
  quiescence.lock("close", "close");
  quiescence.release("close");
  assert.equal(documentRef.body.inert, true, "an existing application-level inert state is preserved");
}

{
  let receiver = null;
  let lastMessage = null;
  globalThis.window = {
    external: {
      receiveMessage(callback) {
        receiver = callback;
      },
      sendMessage(rawMessage) {
        lastMessage = JSON.parse(rawMessage);
      }
    },
    addEventListener() {},
    removeEventListener() {},
    alert() {}
  };

  const restarting = restartForPendingRestore();
  assert.equal(lastMessage.type, "restartForRestore");
  assert.ok(lastMessage.requestId, "restart initiation uses a correlated desktop request");
  receiver(JSON.stringify({
    type: "restartForRestoreResult",
    requestId: lastMessage.requestId,
    ok: true
  }));
  assert.equal(await restarting, true, "the UI waits for native acknowledgement");

  const rejectedRestart = restartForPendingRestore();
  receiver(JSON.stringify({
    type: "restartForRestoreResult",
    requestId: lastMessage.requestId,
    ok: false,
    message: "Another close is active"
  }));
  assert.equal(await rejectedRestart, false, "native refusal is surfaced to the restore UI");
  delete globalThis.window;
}
