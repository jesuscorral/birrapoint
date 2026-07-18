import { Injectable, InjectionToken, inject, signal } from '@angular/core';
import { HubConnectionBuilder, HubConnectionState } from '@microsoft/signalr';
import type { HubConnection } from '@microsoft/signalr';
import Keycloak from 'keycloak-js';
import { Observable } from 'rxjs';

import { environment } from '../../../environments/environment';
import type { CompetitionHubServerEvents } from './competition-hub.events';

export type HubConnectionFactory = () => HubConnection;

/**
 * Injectable seam so tests substitute a hand-rolled fake HubConnection instead of a mocking
 * library (same "real/fake collaborator over mock" convention as the backend's T011/T015 tests).
 * The default factory captures the live Keycloak instance (provided by keycloak-angular, T019)
 * so the hub handshake authenticates via `?access_token=`, per contracts/signalr-hub.md.
 */
export const COMPETITION_HUB_CONNECTION_FACTORY = new InjectionToken<HubConnectionFactory>(
  'COMPETITION_HUB_CONNECTION_FACTORY',
  {
    providedIn: 'root',
    factory: () => {
      const keycloak = inject(Keycloak);
      return () =>
        new HubConnectionBuilder()
          .withUrl(`${environment.apiBaseUrl}/hubs/competition`, {
            accessTokenFactory: () => keycloak.token ?? '',
          })
          .withAutomaticReconnect()
          .build();
    },
  },
);

/**
 * CompetitionHub client (T020): connection lifecycle + group membership tracking so a dropped
 * connection re-joins every group the caller had joined once `onreconnected` fires — "clients
 * re-join their groups on onreconnected" per contracts/signalr-hub.md. Re-fetching state after
 * reconnect (events are notifications, not the source of truth) is each feature's job, not this
 * service's.
 */
@Injectable({ providedIn: 'root' })
export class CompetitionHubService {
  private readonly connectionFactory = inject(COMPETITION_HUB_CONNECTION_FACTORY);
  private connection: HubConnection | null = null;

  private readonly joinedCompetitions = new Set<string>();
  private readonly joinedTables = new Set<string>();

  readonly state = signal(HubConnectionState.Disconnected);

  async start(): Promise<void> {
    if (this.connection) {
      return;
    }

    const connection = this.connectionFactory();
    connection.onreconnecting(() => this.state.set(HubConnectionState.Reconnecting));
    connection.onclose(() => this.state.set(HubConnectionState.Disconnected));
    connection.onreconnected(() => {
      this.state.set(HubConnectionState.Connected);
      void this.rejoinTrackedGroups(connection);
    });

    this.connection = connection;
    await connection.start();
    this.state.set(HubConnectionState.Connected);
  }

  async stop(): Promise<void> {
    await this.connection?.stop();
    this.connection = null;
    this.joinedCompetitions.clear();
    this.joinedTables.clear();
    this.state.set(HubConnectionState.Disconnected);
  }

  async joinCompetitionAsOrganizer(competitionId: string): Promise<void> {
    await this.requireConnection().invoke('JoinCompetitionAsOrganizer', competitionId);
    this.joinedCompetitions.add(competitionId);
  }

  async leaveCompetition(competitionId: string): Promise<void> {
    await this.requireConnection().invoke('LeaveCompetition', competitionId);
    this.joinedCompetitions.delete(competitionId);
  }

  async joinTable(tableId: string): Promise<void> {
    await this.requireConnection().invoke('JoinTable', tableId);
    this.joinedTables.add(tableId);
  }

  async leaveTable(tableId: string): Promise<void> {
    await this.requireConnection().invoke('LeaveTable', tableId);
    this.joinedTables.delete(tableId);
  }

  on<K extends keyof CompetitionHubServerEvents>(
    event: K,
  ): Observable<CompetitionHubServerEvents[K]> {
    return new Observable((subscriber) => {
      const connection = this.requireConnection();
      const handler = (payload: CompetitionHubServerEvents[K]) => subscriber.next(payload);
      connection.on(event, handler);
      return () => connection.off(event, handler);
    });
  }

  private async rejoinTrackedGroups(connection: HubConnection): Promise<void> {
    await Promise.all([
      ...[...this.joinedCompetitions].map((id) =>
        connection.invoke('JoinCompetitionAsOrganizer', id),
      ),
      ...[...this.joinedTables].map((id) => connection.invoke('JoinTable', id)),
    ]);
  }

  private requireConnection(): HubConnection {
    if (!this.connection) {
      throw new Error(
        'CompetitionHubService: call start() before joining groups or subscribing to events.',
      );
    }

    return this.connection;
  }
}
