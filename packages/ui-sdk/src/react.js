import { useCallback, useEffect, useMemo, useState } from "react";
import { YasnClient, YasnApiError } from "./index.js";

const EMPTY_ARRAY = Object.freeze([]);

export function useYasnClient(options = {}) {
  const stableBaseUrl = options.baseUrl ?? "/api";
  const stableResetState = options.defaultResetState ?? false;
  const stableAwaitResult = options.defaultAwaitResult ?? true;

  return useMemo(
    () =>
      new YasnClient({
        ...options,
        baseUrl: stableBaseUrl,
        defaultResetState: stableResetState,
        defaultAwaitResult: stableAwaitResult,
      }),
    [stableBaseUrl, stableResetState, stableAwaitResult, options.fetchImpl]
  );
}

export function useYasnFunctions(client) {
  const loader = useCallback(() => {
    if (!client) {
      return Promise.resolve([]);
    }

    return client.functions();
  }, [client]);

  const state = useYasnResource(client, loader, EMPTY_ARRAY);
  return {
    ...state,
    functions: state.data,
  };
}

export function useYasnSchema(client) {
  const loader = useCallback(() => {
    if (!client) {
      return Promise.resolve([]);
    }

    return client.schema();
  }, [client]);

  const state = useYasnResource(client, loader, EMPTY_ARRAY);
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
    async (functionName, argsOrNamed = [], options = {}) => {
      if (!client) {
        const err = new YasnApiError("YASN client is not initialized", {
          code: "client_unavailable",
        });
        setError(err);
        throw err;
      }

      setLoading(true);
      setError(null);
      try {
        const value = await client.call(functionName, argsOrNamed, options);
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

  const reset = useCallback(() => {
    setResult(null);
    setError(null);
  }, []);

  return {
    call,
    loading,
    result,
    error,
    reset,
  };
}

function useYasnResource(client, loader, defaultValue) {
  const [data, setData] = useState(defaultValue);
  const [loading, setLoading] = useState(Boolean(client));
  const [error, setError] = useState(null);

  const refresh = useCallback(async () => {
    if (!client) {
      setData(defaultValue);
      setLoading(false);
      setError(null);
      return defaultValue;
    }

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
  }, [client, defaultValue, loader]);

  useEffect(() => {
    if (!client) {
      setData(defaultValue);
      setLoading(false);
      setError(null);
      return;
    }

    refresh().catch(() => undefined);
  }, [client, defaultValue, refresh]);

  return {
    data,
    loading,
    error,
    refresh,
  };
}
