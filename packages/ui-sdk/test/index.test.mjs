import test from "node:test";
import assert from "node:assert/strict";
import {
  YasnClient,
  normalizeSchemaItem,
  parseLegacyType,
  isValueAssignableToType,
  formatTypeNode,
} from "../src/index.js";

test("normalizeSchemaItem keeps v2 fields", () => {
  const item = normalizeSchemaItem({
    name: "sum",
    params: [
      {
        name: "items",
        typeNode: {
          kind: "list",
          element: { kind: "primitive", name: "Цел" },
        },
      },
    ],
    returnTypeNode: {
      kind: "primitive",
      name: "Цел",
    },
    isAsync: false,
    isPublicApi: true,
    schemaVersion: 2,
  });

  assert.equal(item.name, "sum");
  assert.equal(item.params[0].typeNode.kind, "list");
  assert.equal(item.params[0].typeNode.element.name, "Цел");
  assert.equal(item.returnTypeNode.name, "Цел");
  assert.equal(item.schemaVersion, 2);
  assert.equal(typeof item.signature, "string");
});

test("legacy type parser and assignability", () => {
  const node = parseLegacyType("Список<Цел>");
  assert.equal(formatTypeNode(node), "Список<Цел>");

  assert.equal(isValueAssignableToType([1, 2, 3], node), true);
  assert.equal(isValueAssignableToType([1, 2.5], node), false);
});

test("YasnClient.call sends named_args payload", async () => {
  let capturedUrl = "";
  let capturedInit = null;

  const client = new YasnClient({
    baseUrl: "http://127.0.0.1:8000",
    fetchImpl: async (url, init) => {
      capturedUrl = url;
      capturedInit = init;
      return {
        ok: true,
        status: 200,
        headers: {
          get(name) {
            return name.toLowerCase() === "content-type"
              ? "application/json"
              : "";
          },
        },
        async json() {
          return {
            ok: true,
            data: {
              result: 3,
            },
          };
        },
      };
    },
  });

  const result = await client.call("sum", { a: 1, b: 2 }, {
    resetState: true,
    awaitResult: false,
  });

  assert.equal(result, 3);
  assert.equal(capturedUrl, "http://127.0.0.1:8000/call");
  assert.equal(capturedInit.method, "POST");

  const body = JSON.parse(capturedInit.body);
  assert.equal(body.function, "sum");
  assert.equal(body.reset_state, true);
  assert.equal(body.await_result, false);
  assert.deepEqual(body.named_args, { a: 1, b: 2 });
  assert.equal(body.args, undefined);
});

test("YasnClient.call rejects invalid argument shape", async () => {
  const client = new YasnClient({
    fetchImpl: async () => {
      throw new Error("fetch should not be called");
    },
  });

  await assert.rejects(() => client.call("sum", 123), (err) => {
    assert.equal(err.code, "invalid_call_args");
    return true;
  });
});
