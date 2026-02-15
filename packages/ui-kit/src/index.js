import React, { useEffect, useMemo, useState } from "react";
import { useYasnCall, useYasnSchema } from "@yasn/ui-sdk/react";
import { Button, Skeleton } from "./primitives/index.js";
import {
  FunctionForm,
  FunctionSelect,
  CallResult,
  TaskHandleStatus,
  ErrorPanel,
} from "./yasn-blocks/index.js";

export {
  Button,
  Input,
  Select,
  Checkbox,
  Textarea,
  Card,
  Badge,
  Modal,
  Toast,
  ToastContainer,
  Tabs,
  TabsPanel,
  Spinner,
  Skeleton,
  EmptyState,
  Tooltip,
  Dropdown,
  DropdownItem,
  DropdownDivider,
} from "./primitives/index.js";
export { FunctionForm, FunctionSelect, SchemaExplorer, CallResult, TaskHandleStatus, ErrorPanel } from "./yasn-blocks/index.js";
export { AppShell, Sidebar, Toolbar, Table, Kanban } from "./layout/index.js";

export function YasnPlayground({
  client,
  title = "YASN Playground",
  submitLabel = "Run",
  resetState = false,
  awaitResult = true,
  showOnlyPublic = true,
}) {
  const { schema, loading: schemaLoading, error: schemaError, refresh } =
    useYasnSchema(client);
  const { call, loading: callLoading, result, error: callError, reset } =
    useYasnCall(client);

  const functions = useMemo(
    () =>
      showOnlyPublic
        ? schema.filter((item) => item.isPublicApi !== false)
        : schema,
    [schema, showOnlyPublic]
  );

  const [selectedFunction, setSelectedFunction] = useState("");
  const currentSchema = useMemo(
    () => functions.find((item) => item.name === selectedFunction) ?? null,
    [functions, selectedFunction]
  );

  useEffect(() => {
    if (functions.length === 0) {
      if (selectedFunction) setSelectedFunction("");
      return;
    }
    if (!selectedFunction || !functions.some((item) => item.name === selectedFunction)) {
      setSelectedFunction(functions[0].name);
    }
  }, [functions, selectedFunction]);

  const handleSubmit = async (namedArgs) => {
    if (!selectedFunction) return;
    await call(selectedFunction, namedArgs, { resetState, awaitResult });
  };

  function SchemaSkeleton() {
    return React.createElement(
      "div",
      { style: { display: "grid", gap: "var(--yasn-space-3)" } },
      React.createElement(Skeleton, { height: "2em", width: "60%" }),
      React.createElement(Skeleton, { height: "8em", width: "100%" }),
      React.createElement(Skeleton, { height: "2em", width: "40%" })
    );
  }

  const isTaskHandle = useMemo(() => {
    if (result === null || result === undefined || callError) return false;
    return typeof result === "object" && typeof result.task_id === "number";
  }, [result, callError]);

  return React.createElement(
    "section",
    { className: "yasn-playground" },
    React.createElement("h2", {
      style: {
        margin: 0,
        marginBottom: "var(--yasn-space-3)",
        fontSize: "var(--yasn-fs-3xl)",
        color: "var(--yasn-text)",
      },
    }, title),
    React.createElement(
      "div",
      { className: "yasn-playground__toolbar" },
      React.createElement(FunctionSelect, {
        functions,
        value: selectedFunction,
        onChange: setSelectedFunction,
      }),
      React.createElement(Button, {
        variant: "secondary",
        onClick: () => refresh().catch(() => undefined),
      }, "Refresh schema"),
      React.createElement(Button, { variant: "secondary", onClick: reset }, "Clear result")
    ),
    schemaLoading
      ? React.createElement(SchemaSkeleton)
      : null,
    schemaError
      ? React.createElement(ErrorPanel, { error: schemaError })
      : null,
    currentSchema
      ? React.createElement(FunctionForm, {
          schema: currentSchema,
          submitLabel,
          loading: callLoading,
          onSubmit: handleSubmit,
        })
      : null,
    !schemaLoading && functions.length === 0
      ? React.createElement(
          "p",
          { className: "yasn-status" },
          "No public API functions in schema. Export functions to expose them in UI."
        )
      : null,
    isTaskHandle
      ? React.createElement(TaskHandleStatus, { handle: result })
      : React.createElement(CallResult, {
          result,
          error: callError,
        })
  );
}

export function YasnFunctionSelect(props) {
  return React.createElement(FunctionSelect, props);
}

export function YasnAutoForm(props) {
  return React.createElement(FunctionForm, props);
}

export function YasnResultCard({ result, error }) {
  return React.createElement(CallResult, { result, error });
}
