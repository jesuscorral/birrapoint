// Server -> client event payloads, contracts/signalr-hub.md. Where the judge-group and
// organizer-group variants of the same event differ, the richer (organizer) shape is used and
// the extra fields marked optional — additive fields are non-breaking per the contract, and a
// client only ever belongs to one relevant group at a time.

export interface TableOrderFixedEvent {
  tableId: string;
  orderedSamples: { beerEntryId: string; blindCode: string; sequenceOrder: number }[];
  fixedByDisplayName: string;
}

export interface DiscrepancyRaisedEvent {
  alertId: string;
  tableId: string;
  blindCode: string;
  involvedJudgeIds: string[];
}

export interface DiscrepancyResolvedEvent {
  alertId: string;
  tableId: string;
  blindCode: string;
}

export interface TableClosedEvent {
  tableId: string;
  consolidatedScores?: { blindCode: string; mean: number }[];
}

export interface JudgeRemovedEvent {
  tableId: string;
  judgeId: string;
  judgeDisplayName?: string;
}

export interface EvaluationCompletedEvent {
  tableId: string;
  blindCode: string;
  judgeDisplayName: string;
  tableProgress: { completed: number; expected: number; percent: number };
}

export interface CompetitionStateChangedEvent {
  competitionId: string;
  state: string;
}

export interface DispatchProgressEvent {
  jobType: string;
  status: string;
  detail?: string;
}

export interface CompetitionHubServerEvents {
  TableOrderFixed: TableOrderFixedEvent;
  DiscrepancyRaised: DiscrepancyRaisedEvent;
  DiscrepancyResolved: DiscrepancyResolvedEvent;
  TableClosed: TableClosedEvent;
  JudgeRemoved: JudgeRemovedEvent;
  EvaluationCompleted: EvaluationCompletedEvent;
  CompetitionStateChanged: CompetitionStateChangedEvent;
  DispatchProgress: DispatchProgressEvent;
}
