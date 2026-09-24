import { loadAppConfig } from './app-config.loader';
import type { AppConfig } from './app-config.model';

function fakeFetch(response: Partial<Response> & { json?: () => Promise<unknown> }) {
  return jest.fn().mockResolvedValue(response as Response);
}

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
