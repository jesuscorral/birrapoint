import { TestBed } from '@angular/core/testing';
import type { HubConnection } from '@microsoft/signalr';
import { HubConnectionState } from '@microsoft/signalr';

import {
  COMPETITION_HUB_CONNECTION_FACTORY,
  CompetitionHubService,
} from './competition-hub.service';

/** Hand-rolled fake matching only the HubConnection surface this service calls. */
function createFakeConnection() {
  const handlers = new Map<string, (...args: unknown[]) => void>();
  let reconnectedCallback: (() => void) | undefined;

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
    onclose: jest.fn(),
    // Test helpers, not part of the real HubConnection surface.
    __emit: (method: string, payload: unknown) => handlers.get(method)?.(payload),
    __triggerReconnected: () => reconnectedCallback?.(),
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
});
