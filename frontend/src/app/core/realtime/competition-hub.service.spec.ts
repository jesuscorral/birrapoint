import { TestBed } from '@angular/core/testing';
import type { HubConnection } from '@microsoft/signalr';
import { HubConnectionBuilder, HubConnectionState } from '@microsoft/signalr';
import Keycloak from 'keycloak-js';

import { APP_CONFIG } from '../config/app-config.model';
import {
  COMPETITION_HUB_CONNECTION_FACTORY,
  CompetitionHubService,
} from './competition-hub.service';
import type { HubConnectionFactory } from './competition-hub.service';

/** Hand-rolled fake matching only the HubConnection surface this service calls. */
function createFakeConnection() {
  const handlers = new Map<string, (...args: unknown[]) => void>();
  let reconnectedCallback: (() => void) | undefined;
  let closeCallback: (() => void) | undefined;

  const fake = {
    state: HubConnectionState.Disconnected,
    start: jest.fn().mockImplementation(async () => {
      fake.state = HubConnectionState.Connected;
    }),
    stop: jest.fn().mockImplementation(async () => {
      fake.state = HubConnectionState.Disconnected;
    }),
    invoke: jest.fn().mockResolvedValue(undefined),
    on: jest.fn((method: string, callback: (...args: unknown[]) => void) => {
      handlers.set(method, callback);
    }),
    off: jest.fn((method: string) => {
      handlers.delete(method);
    }),
    onreconnected: jest.fn((callback: () => void) => {
      reconnectedCallback = callback;
    }),
    onreconnecting: jest.fn(),
    onclose: jest.fn((callback: () => void) => {
      closeCallback = callback;
    }),
    // Test helpers, not part of the real HubConnection surface.
    __emit: (method: string, payload: unknown) => handlers.get(method)?.(payload),
    __triggerReconnected: () => reconnectedCallback?.(),
    __triggerClose: () => closeCallback?.(),
  };

  return fake;
}

describe('CompetitionHubService', () => {
  let service: CompetitionHubService;
  let fakeConnection: ReturnType<typeof createFakeConnection>;

  beforeEach(() => {
    fakeConnection = createFakeConnection();

    TestBed.configureTestingModule({
      providers: [
        {
          provide: COMPETITION_HUB_CONNECTION_FACTORY,
          useValue: () => fakeConnection as unknown as HubConnection,
        },
      ],
    });
    service = TestBed.inject(CompetitionHubService);
  });

  it('start() builds the connection once and starts it', async () => {
    await service.start();
    await service.start();

    expect(fakeConnection.start).toHaveBeenCalledTimes(1);
    expect(service.state()).toBe(HubConnectionState.Connected);
  });

  it('joining and leaving groups invokes the matching hub methods', async () => {
    await service.start();

    await service.joinCompetitionAsOrganizer('comp-1');
    expect(fakeConnection.invoke).toHaveBeenCalledWith('JoinCompetitionAsOrganizer', 'comp-1');

    await service.joinTable('table-1');
    expect(fakeConnection.invoke).toHaveBeenCalledWith('JoinTable', 'table-1');

    await service.leaveTable('table-1');
    expect(fakeConnection.invoke).toHaveBeenCalledWith('LeaveTable', 'table-1');

    await service.leaveCompetition('comp-1');
    expect(fakeConnection.invoke).toHaveBeenCalledWith('LeaveCompetition', 'comp-1');
  });

  it('rejoins every currently-tracked group on reconnect, but not a group already left', async () => {
    await service.start();
    await service.joinCompetitionAsOrganizer('comp-1');
    await service.joinTable('table-1');
    await service.joinTable('table-2');
    await service.leaveTable('table-2');
    fakeConnection.invoke.mockClear();

    fakeConnection.__triggerReconnected();
    await Promise.resolve(); // flush the async rejoin

    expect(fakeConnection.invoke).toHaveBeenCalledWith('JoinCompetitionAsOrganizer', 'comp-1');
    expect(fakeConnection.invoke).toHaveBeenCalledWith('JoinTable', 'table-1');
    expect(fakeConnection.invoke).not.toHaveBeenCalledWith('JoinTable', 'table-2');
  });

  it('on() emits payloads the fake connection dispatches, and off() on unsubscribe', async () => {
    await service.start();
    const received: unknown[] = [];
    const subscription = service.on('TableClosed').subscribe((payload) => received.push(payload));

    fakeConnection.__emit('TableClosed', { tableId: 't1' });
    expect(received).toEqual([{ tableId: 't1' }]);

    subscription.unsubscribe();
    expect(fakeConnection.off).toHaveBeenCalledWith('TableClosed', expect.any(Function));
  });

  it('joining a group before start() throws', async () => {
    await expect(service.joinTable('table-1')).rejects.toThrow();
  });

  it('a failed start() clears the connection so a retry rebuilds instead of wedging', async () => {
    fakeConnection.start.mockRejectedValueOnce(new Error('handshake failed'));
    await expect(service.start()).rejects.toThrow('handshake failed');

    await service.start();
    expect(fakeConnection.start).toHaveBeenCalledTimes(2);
    expect(service.state()).toBe(HubConnectionState.Connected);
  });

  it('onclose (automatic reconnect exhausted) clears the connection so a later start() rebuilds', async () => {
    await service.start();
    fakeConnection.__triggerClose();
    expect(service.state()).toBe(HubConnectionState.Disconnected);

    await service.start();
    expect(fakeConnection.start).toHaveBeenCalledTimes(2);
  });

  it('one rejoin failing on reconnect does not prevent the others or throw unhandled', async () => {
    await service.start();
    await service.joinTable('table-1');
    await service.joinTable('table-2');
    fakeConnection.invoke.mockClear();
    fakeConnection.invoke.mockImplementation((method: string, id: string) =>
      method === 'JoinTable' && id === 'table-1'
        ? Promise.reject(new Error('table-closed'))
        : Promise.resolve(undefined),
    );

    fakeConnection.__triggerReconnected();
    await Promise.resolve();
    await Promise.resolve(); // flush Promise.allSettled over the rejected + resolved invokes

    expect(fakeConnection.invoke).toHaveBeenCalledWith('JoinTable', 'table-1');
    expect(fakeConnection.invoke).toHaveBeenCalledWith('JoinTable', 'table-2');
  });
});

describe('COMPETITION_HUB_CONNECTION_FACTORY (default factory)', () => {
  function factoryFor(apiBaseUrl: string): HubConnectionFactory {
    TestBed.configureTestingModule({
      providers: [
        { provide: Keycloak, useValue: { token: 'a-token' } },
        {
          provide: APP_CONFIG,
          useValue: { keycloak: { url: '', realm: '', clientId: '' }, apiBaseUrl },
        },
      ],
    });
    return TestBed.inject(COMPETITION_HUB_CONNECTION_FACTORY);
  }

  // .build() resolves the URL via an anchor tag and only does so when @microsoft/signalr detects
  // a real browser (Platform.isBrowser) — under Jest/jsdom it always evaluates to false (Node's
  // own `process` global trips its isNode check), so a relative URL would throw at .build() time
  // even though it works fine in an actual browser. Stubbing .build() and asserting on the raw
  // string passed to .withUrl() instead exercises this service's own logic without depending on
  // that browser check.
  function captureRequestedUrl(apiBaseUrl: string): string {
    const withUrlSpy = jest.spyOn(HubConnectionBuilder.prototype, 'withUrl');
    const buildSpy = jest
      .spyOn(HubConnectionBuilder.prototype, 'build')
      .mockReturnValue({} as HubConnection);

    factoryFor(apiBaseUrl)();
    const [url] = withUrlSpy.mock.calls[0];

    withUrlSpy.mockRestore();
    buildSpy.mockRestore();
    return url;
  }

  it('builds the connection against an absolute apiBaseUrl', () => {
    expect(captureRequestedUrl('http://localhost:5121')).toBe(
      'http://localhost:5121/hubs/competition',
    );
  });

  it('builds the connection against a relative path for a same-origin ("") apiBaseUrl', () => {
    expect(captureRequestedUrl('')).toBe('/hubs/competition');
  });
});
