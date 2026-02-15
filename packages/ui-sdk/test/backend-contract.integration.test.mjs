import test, { after, before } from "node:test";
import assert from "node:assert/strict";
import { spawn, spawnSync } from "node:child_process";
import net from "node:net";
import path from "node:path";
import { fileURLToPath } from "node:url";

const __filename = fileURLToPath(import.meta.url);
const __dirname = path.dirname(__filename);
const repoRoot = path.resolve(__dirname, "..", "..", "..");
const fixturePath = path.join(repoRoot, "packages", "ui-sdk", "test", "fixtures", "backend_contract.яс");

let baseUrl = "";
let serverProc = null;
let stdoutLog = "";
let stderrLog = "";

before(async () => {
  const port = await getFreePort();
  baseUrl = `http://127.0.0.1:${port}`;
  startBackend(port);
  await waitForHealth();
});

after(async () => {
  await stopBackend();
});

test("backend /schema and /call contract", async () => {
  const schemaRes = await fetch(`${baseUrl}/schema`);
  assert.equal(schemaRes.status, 200);
  const schemaJson = await schemaRes.json();

  assert.equal(schemaJson.ok, true);
  assert.equal(schemaJson.data.schemaVersion, 2);

  const functions = schemaJson.data.functions;
  const names = functions.map((item) => item.name).sort();
  assert.deepEqual(names, ["медленно", "сумма"]);

  const sum = functions.find((item) => item.name === "сумма");
  assert.ok(sum);
  assert.equal(sum.isPublicApi, true);
  assert.equal(sum.params[0].type, "Цел");
  assert.equal(sum.params[0].typeNode.kind, "primitive");

  const namedCall = await callApi({
    function: "сумма",
    named_args: { a: 2, b: 3 },
  });
  assert.equal(namedCall.response.status, 200);
  assert.equal(namedCall.body.ok, true);
  assert.equal(namedCall.body.data.result, 5);

  const asyncHandle = await callApi({
    function: "медленно",
    args: [7],
    await_result: false,
  });
  assert.equal(asyncHandle.response.status, 200);
  const task = asyncHandle.body.data.result;
  assert.equal(typeof task.task_id, "number");
  assert.equal(typeof task.done, "boolean");
  assert.equal(typeof task.canceled, "boolean");
  assert.equal(typeof task.faulted, "boolean");

  const bothArgs = await callApi({
    function: "сумма",
    args: [1, 2],
    named_args: { a: 1, b: 2 },
  });
  assert.equal(bothArgs.response.status, 400);
  assert.equal(bothArgs.body.error.code, "invalid_request");

  const invalidBool = await callApi({
    function: "сумма",
    args: [1, 2],
    reset_state: "yes",
  });
  assert.equal(invalidBool.response.status, 400);
  assert.equal(invalidBool.body.error.code, "invalid_request");

  const invalidType = await callApi({
    function: "сумма",
    named_args: { a: "oops", b: 2 },
  });
  assert.equal(invalidType.response.status, 400);
  assert.equal(invalidType.body.error.code, "invalid_arguments");

  const unknown = await callApi({
    function: "main",
    args: [],
  });
  assert.equal(unknown.response.status, 404);
  assert.equal(unknown.body.error.code, "unknown_function");

  const methodRes = await fetch(`${baseUrl}/call`);
  const methodBody = await methodRes.json();
  assert.equal(methodRes.status, 405);
  assert.equal(methodBody.error.code, "method_not_allowed");
});

async function callApi(payload) {
  const response = await fetch(`${baseUrl}/call`, {
    method: "POST",
    headers: {
      "Content-Type": "application/json",
    },
    body: JSON.stringify(payload),
  });
  const body = await response.json();
  return { response, body };
}

function startBackend(port) {
  serverProc = spawn(
    "dotnet",
    [
      "run",
      "--project",
      "native/yasn-native/yasn-native.csproj",
      "--",
      "serve",
      fixturePath,
      "--host",
      "127.0.0.1",
      "--port",
      String(port),
    ],
    {
      cwd: repoRoot,
      stdio: ["ignore", "pipe", "pipe"],
    }
  );

  serverProc.stdout.setEncoding("utf8");
  serverProc.stderr.setEncoding("utf8");
  serverProc.stdout.on("data", (chunk) => {
    stdoutLog += chunk;
  });
  serverProc.stderr.on("data", (chunk) => {
    stderrLog += chunk;
  });
}

async function stopBackend() {
  if (!serverProc || serverProc.exitCode !== null) {
    return;
  }

  serverProc.kill();
  await delay(300);

  if (serverProc.exitCode === null) {
    if (process.platform === "win32") {
      spawnSync("taskkill", ["/PID", String(serverProc.pid), "/T", "/F"], {
        stdio: "ignore",
      });
      await delay(300);
      return;
    } else {
      serverProc.kill("SIGKILL");
    }
  }

  try {
    await waitForExit(serverProc, 3_000);
  } catch {
    // force-kill path can race with process event delivery
  }
}

function waitForExit(proc, timeoutMs) {
  return new Promise((resolve, reject) => {
    if (!proc || proc.exitCode !== null) {
      resolve();
      return;
    }

    const timer = setTimeout(() => {
      reject(new Error(`Backend process did not exit in ${timeoutMs}ms`));
    }, timeoutMs);

    proc.once("exit", () => {
      clearTimeout(timer);
      resolve();
    });
  });
}

async function waitForHealth() {
  const timeoutMs = 30_000;
  const start = Date.now();

  while (Date.now() - start < timeoutMs) {
    if (serverProc?.exitCode !== null) {
      throw new Error(
        `Backend exited before health check.\nstdout:\n${stdoutLog}\nstderr:\n${stderrLog}`
      );
    }

    try {
      const response = await fetch(`${baseUrl}/health`);
      if (response.status === 200) {
        const body = await response.json();
        if (body?.ok === true) {
          return;
        }
      }
    } catch {
      // server is still starting
    }

    await delay(200);
  }

  throw new Error(
    `Backend did not become healthy in ${timeoutMs}ms.\nstdout:\n${stdoutLog}\nstderr:\n${stderrLog}`
  );
}

function getFreePort() {
  return new Promise((resolve, reject) => {
    const socket = net.createServer();
    socket.on("error", reject);
    socket.listen(0, "127.0.0.1", () => {
      const address = socket.address();
      if (!address || typeof address === "string") {
        socket.close(() => reject(new Error("Failed to allocate free port")));
        return;
      }

      const { port } = address;
      socket.close((err) => {
        if (err) {
          reject(err);
          return;
        }

        resolve(port);
      });
    });
  });
}

function delay(ms) {
  return new Promise((resolve) => setTimeout(resolve, ms));
}
