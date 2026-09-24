import { APP_CONFIG_STORAGE_KEY, loadAppConfig } from './app-config.loader';
import type { AppConfigStorage } from './app-config.loader';
import type { AppConfig } from './app-config.model';

function fakeFetch(response: Partial<Response> & { json?: () => Promise<unknown> }) {
  return jest.fn().mockResolvedValue(response as Response);
}

function fakeFailingFetch(error: unknown) {
  return jest.fn().mockRejectedValue(error);
}

function fakeStorage(initial: Record<string, string> = {}): AppConfigStorage {
  const store = new Map(Object.entries(initial));
  return {
    getItem: jest.fn((key: string) => (store.has(key) ? (store.get(key) as string) : null)),
    setItem: jest.fn((key: string, value: string) => {
      store.set(key, value);
    }),
  };
}

function throwingStorage(): AppConfigStorage {
  return {
    getItem: jest.fn(() => {
      throw new Error('storage unavailable');
    }),
    setItem: jest.fn(() => {
      throw new Error('storage unavailable');
    }),
  };
}

// This file's tests that don't pass an explicit `storage` fall back to jsdom's real
// `window.localStorage`, which persists across `it` blocks within the same file — clear it so a
// config cached by one test never leaks into another test's "no cache" expectations.
afterEach(() => {
  localStorage.clear();
});

describe('loadAppConfig', () => {
  it('fetches /config.json with cache: no-cache and returns the parsed config', async () => {
    const config: AppConfig = {
      keycloak: { url: 'http://localhost:8081', realm: 'birrapoint', clientId: 'birrapoint-spa' },
      apiBaseUrl: 'http://localhost:5121',
    };
    const fetchFn = fakeFetch({ ok: true, status: 200, json: () => Promise.resolve(config) });

    const result = await loadAppConfig(fetchFn as unknown as typeof fetch);

    expect(fetchFn).toHaveBeenCalledWith('/config.json', { cache: 'no-cache' });
    expect(result).toEqual(config);
  });

  it('accepts an empty apiBaseUrl (same-origin production deployment)', async () => {
    const config: AppConfig = {
      keycloak: {
        url: 'https://auth.example.com',
        realm: 'birrapoint',
        clientId: 'birrapoint-spa',
      },
      apiBaseUrl: '',
    };
    const fetchFn = fakeFetch({ ok: true, status: 200, json: () => Promise.resolve(config) });

    const result = await loadAppConfig(fetchFn as unknown as typeof fetch);

    expect(result.apiBaseUrl).toBe('');
  });

  it('strips a trailing slash from apiBaseUrl and keycloak.url', async () => {
    const fetchFn = fakeFetch({
      ok: true,
      status: 200,
      json: () =>
        Promise.resolve({
          keycloak: { url: 'https://auth.example.com/', realm: 'birrapoint', clientId: 'spa' },
          apiBaseUrl: 'https://api.example.com/',
        }),
    });

    const result = await loadAppConfig(fetchFn as unknown as typeof fetch);

    expect(result.keycloak.url).toBe('https://auth.example.com');
    expect(result.apiBaseUrl).toBe('https://api.example.com');
  });

  it('throws a clear error when the response is not ok', async () => {
    const fetchFn = fakeFetch({ ok: false, status: 500 });

    await expect(loadAppConfig(fetchFn as unknown as typeof fetch)).rejects.toThrow(
      /config\.json/i,
    );
  });

  it('throws when keycloak.url is missing', async () => {
    const fetchFn = fakeFetch({
      ok: true,
      status: 200,
      json: () =>
        Promise.resolve({
          keycloak: { realm: 'birrapoint', clientId: 'spa' },
          apiBaseUrl: '',
        }),
    });

    await expect(loadAppConfig(fetchFn as unknown as typeof fetch)).rejects.toThrow();
  });

  it('throws when keycloak.realm is an empty string', async () => {
    const fetchFn = fakeFetch({
      ok: true,
      status: 200,
      json: () =>
        Promise.resolve({
          keycloak: { url: 'http://localhost:8081', realm: '', clientId: 'spa' },
          apiBaseUrl: '',
        }),
    });

    await expect(loadAppConfig(fetchFn as unknown as typeof fetch)).rejects.toThrow();
  });

  it('throws when keycloak.clientId is missing', async () => {
    const fetchFn = fakeFetch({
      ok: true,
      status: 200,
      json: () =>
        Promise.resolve({
          keycloak: { url: 'http://localhost:8081', realm: 'birrapoint' },
          apiBaseUrl: '',
        }),
    });

    await expect(loadAppConfig(fetchFn as unknown as typeof fetch)).rejects.toThrow();
  });

  it('throws when apiBaseUrl is not a string (e.g. missing entirely)', async () => {
    const fetchFn = fakeFetch({
      ok: true,
      status: 200,
      json: () =>
        Promise.resolve({
          keycloak: { url: 'http://localhost:8081', realm: 'birrapoint', clientId: 'spa' },
        }),
    });

    await expect(loadAppConfig(fetchFn as unknown as typeof fetch)).rejects.toThrow();
  });

  it('throws when the body is not an object at all', async () => {
    const fetchFn = fakeFetch({ ok: true, status: 200, json: () => Promise.resolve(null) });

    await expect(loadAppConfig(fetchFn as unknown as typeof fetch)).rejects.toThrow();
  });

  it('defaults to the global fetch when no fetchFn is supplied', async () => {
    const config: AppConfig = {
      keycloak: { url: 'http://localhost:8081', realm: 'birrapoint', clientId: 'birrapoint-spa' },
      apiBaseUrl: 'http://localhost:5121',
    };
    const globalFetch = fakeFetch({ ok: true, status: 200, json: () => Promise.resolve(config) });
    const original = globalThis.fetch;
    globalThis.fetch = globalFetch as unknown as typeof fetch;

    try {
      const result = await loadAppConfig();
      expect(result).toEqual(config);
    } finally {
      globalThis.fetch = original;
    }
  });
});

// Last-known-good fallback (R-08): a judge opening the installed PWA offline must not hit the
// fatal error screen just because /config.json couldn't be (re)fetched — a previously validated
// config cached in localStorage should be used instead. Invalid config from the server (a 200
// that fails validation) is a real server misconfiguration, not an offline scenario, and must
// still throw rather than silently falling back to a stale cache.
describe('loadAppConfig offline fallback', () => {
  const config: AppConfig = {
    keycloak: { url: 'http://localhost:8081', realm: 'birrapoint', clientId: 'birrapoint-spa' },
    apiBaseUrl: 'http://localhost:5121',
  };

  it('falls back to the cached config when the network request fails', async () => {
    const storage = fakeStorage({ [APP_CONFIG_STORAGE_KEY]: JSON.stringify(config) });
    const fetchFn = fakeFailingFetch(new TypeError('Failed to fetch'));

    const result = await loadAppConfig(fetchFn as unknown as typeof fetch, storage);

    expect(result).toEqual(config);
  });

  it('falls back to the cached config when the response is not ok', async () => {
    const storage = fakeStorage({ [APP_CONFIG_STORAGE_KEY]: JSON.stringify(config) });
    const fetchFn = fakeFetch({ ok: false, status: 500 });

    const result = await loadAppConfig(fetchFn as unknown as typeof fetch, storage);

    expect(result).toEqual(config);
  });

  it('throws the network error when there is no cached config', async () => {
    const storage = fakeStorage();
    const networkError = new TypeError('Failed to fetch');
    const fetchFn = fakeFailingFetch(networkError);

    await expect(loadAppConfig(fetchFn as unknown as typeof fetch, storage)).rejects.toThrow(
      networkError,
    );
  });

  it('throws when the response is not ok and there is no cached config', async () => {
    const storage = fakeStorage();
    const fetchFn = fakeFetch({ ok: false, status: 500 });

    await expect(loadAppConfig(fetchFn as unknown as typeof fetch, storage)).rejects.toThrow(
      /config\.json/i,
    );
  });

  it('persists a successfully validated config to storage', async () => {
    const storage = fakeStorage();
    const fetchFn = fakeFetch({ ok: true, status: 200, json: () => Promise.resolve(config) });

    await loadAppConfig(fetchFn as unknown as typeof fetch, storage);

    expect(storage.setItem).toHaveBeenCalledWith(APP_CONFIG_STORAGE_KEY, JSON.stringify(config));
  });

  it('still throws on a 200 response with invalid config, even with a valid cache available', async () => {
    const storage = fakeStorage({ [APP_CONFIG_STORAGE_KEY]: JSON.stringify(config) });
    const fetchFn = fakeFetch({
      ok: true,
      status: 200,
      json: () => Promise.resolve({ keycloak: { realm: 'birrapoint' }, apiBaseUrl: '' }),
    });

    await expect(loadAppConfig(fetchFn as unknown as typeof fetch, storage)).rejects.toThrow();
  });

  it('works even when storage access throws (private browsing / unavailable storage)', async () => {
    const storage = throwingStorage();
    const fetchFn = fakeFetch({ ok: true, status: 200, json: () => Promise.resolve(config) });

    const result = await loadAppConfig(fetchFn as unknown as typeof fetch, storage);

    expect(result).toEqual(config);
  });

  it('rethrows the network error when reading a throwing storage as fallback', async () => {
    const storage = throwingStorage();
    const networkError = new TypeError('Failed to fetch');
    const fetchFn = fakeFailingFetch(networkError);

    await expect(loadAppConfig(fetchFn as unknown as typeof fetch, storage)).rejects.toThrow(
      networkError,
    );
  });

  it('ignores a corrupted cache entry and throws as if there were no cache', async () => {
    const storage = fakeStorage({ [APP_CONFIG_STORAGE_KEY]: '{not json' });
    const networkError = new TypeError('Failed to fetch');
    const fetchFn = fakeFailingFetch(networkError);

    await expect(loadAppConfig(fetchFn as unknown as typeof fetch, storage)).rejects.toThrow(
      networkError,
    );
  });
});
