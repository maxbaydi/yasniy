import { useCallback, useEffect, useMemo, useState } from "react";
import { YasnClient } from "./index.js";

export function useYasnClient(options = {}) {
  const stableBaseUrl = options.baseUrl ?? "/api";
  const stableResetState = options.defaultResetState ?? false;
  return useMemo(
    () =>
      new YasnClient({
        ...options,
        baseUrl: stableBaseUrl,
        defaultResetState: stableResetState,
      }),
    [stableBaseUrl, stableResetState, options.fetchImpl]
  );
}

export function useYasnFunctions(client) {
  const state = useYasnResource(client, () => client.functions(), []);
  return {
    ...state,
    functions: state.data,
  };
}

export function useYasnSchema(client) {
  const state = useYasnResource(client, () => client.schema(), []);
  return {
    ...state,
    schema: state.data,
  };
}

export function useYasnCall(client) {
  const [loading, setLoading] = useState(false);
  const [result, setResult] = useState(null);
  const [error, setError] = useState(null);

  const call = useCallback(
    async (functionName, args = [], options = {}) => {
      setLoading(true);
      setError(null);
      try {
        const value = await client.call(functionName, args, options);
        setResult(value);
        return value;
      } catch (err) {
        setError(err);
        throw err;
      } finally {
        setLoading(false);
      }
    },
    [client]
  );

  return {
    call,
    loading,
    result,
    error,
  };
}

function useYasnResource(client, loader, defaultValue) {
  const [data, setData] = useState(defaultValue);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState(null);

  const refresh = useCallback(async () => {
    setLoading(true);
    setError(null);
    try {
      const value = await loader();
      setData(value);
      return value;
    } catch (err) {
      setError(err);
      throw err;
    } finally {
      setLoading(false);
    }
  }, [loader]);

  useEffect(() => {
    if (!client) {
      return;
    }

    refresh().catch(() => undefined);
  }, [client, refresh]);

  return {
    data,
    loading,
    error,
    refresh,
  };
}
