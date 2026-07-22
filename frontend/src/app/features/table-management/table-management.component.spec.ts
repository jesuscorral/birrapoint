import { TestBed } from '@angular/core/testing';
import { ActivatedRoute, convertToParamMap } from '@angular/router';
import { of, throwError } from 'rxjs';

import { ApiError } from '../../core/api/api-error';
import { EntriesApiService } from '../../core/api/entries-api.service';
import type { EntryListItem } from '../../core/api/entries-api.service';
import { TableManagementApiService } from './table-management-api.service';
import type {
  JudgeListItem,
  TableMutationResult,
  TableSummary,
} from './table-management-api.service';
import { TableManagementComponent } from './table-management.component';

function tableFixture(overrides: Partial<TableSummary> = {}): TableSummary {
  return {
    id: 't1',
    name: 'Mesa 1',
    state: 'Open',
    judges: [{ id: 'j1', email: 'ada@example.com', displayName: 'Ada Lovelace' }],
    samples: [
      {
        beerEntryId: 'e1',
        blindCode: 'AB12',
        styleCode: '4A',
        styleName: 'Munich Helles',
        abvLow: 4.5,
        abvHigh: 5.5,
        notValidForBos: false,
      },
    ],
    progress: { submitted: 0, total: 1 },
    stats: { meanAbv: 5, styleCount: 1, styles: ['Munich Helles'] },
    ...overrides,
  };
}

function entriesFixture(): EntryListItem[] {
  return [
    {
      id: 'e1',
      blindCode: 'AB12',
      styleCode: '4A',
      styleName: 'Munich Helles',
      abvLow: 4.5,
      abvHigh: 5.5,
      beerName: 'Golden Helles',
      notValidForBos: false,
      tastingTableId: 't1',
      tastingTableName: 'Mesa 1',
    },
    {
      id: 'e2',
      blindCode: 'CD34',
      styleCode: '21A',
      styleName: 'American IPA',
      abvLow: 6,
      abvHigh: 7.5,
      beerName: 'Hazy Dream',
      notValidForBos: false,
      tastingTableId: null,
      tastingTableName: null,
    },
  ];
}

function judgesFixture(): JudgeListItem[] {
  return [
    { id: 'j1', email: 'ada@example.com', displayName: 'Ada Lovelace' },
    { id: 'j2', email: 'grace@example.com', displayName: 'Grace Hopper' },
  ];
}

function buttonWithText(root: Element, text: string): HTMLButtonElement {
  const buttons = [...root.querySelectorAll('button')] as HTMLButtonElement[];
  const match = buttons.find((button) => button.textContent?.trim() === text);
  if (!match) {
    throw new Error(`No button with text "${text}" found`);
  }
  return match;
}

describe('TableManagementComponent', () => {
  let fakeApi: {
    getTables: jest.Mock;
    getJudges: jest.Mock;
    createTable: jest.Mock;
    updateTable: jest.Mock;
  };
  let fakeEntriesApi: { getEntries: jest.Mock };

  beforeEach(() => {
    fakeApi = {
      getTables: jest.fn().mockReturnValue(of([tableFixture()])),
      getJudges: jest.fn().mockReturnValue(of(judgesFixture())),
      createTable: jest.fn(),
      updateTable: jest.fn(),
    };
    fakeEntriesApi = {
      getEntries: jest.fn().mockReturnValue(of(entriesFixture())),
    };
    TestBed.configureTestingModule({
      providers: [
        { provide: TableManagementApiService, useValue: fakeApi },
        { provide: EntriesApiService, useValue: fakeEntriesApi },
        {
          provide: ActivatedRoute,
          useValue: { snapshot: { paramMap: convertToParamMap({ id: 'c1' }) } },
        },
      ],
    });
  });

  function createComponent() {
    const fixture = TestBed.createComponent(TableManagementComponent);
    fixture.detectChanges();
    return fixture;
  }

  it('loads tables, entries, and judges on init', () => {
    createComponent();

    expect(fakeApi.getTables).toHaveBeenCalledWith('c1');
    expect(fakeEntriesApi.getEntries).toHaveBeenCalledWith('c1');
    expect(fakeApi.getJudges).toHaveBeenCalledWith('c1');
  });

  it('computes the Unassigned column as the set difference of judges/beers not on any table', () => {
    const fixture = createComponent();

    // j1 is seated at Mesa 1 (table fixture); j2 is not on any table.
    expect(fixture.componentInstance.unassignedJudges()).toEqual([
      { id: 'j2', email: 'grace@example.com', displayName: 'Grace Hopper' },
    ]);
    // e1 is seated at Mesa 1; e2 has tastingTableId === null.
    expect(fixture.componentInstance.unassignedBeers().map((e) => e.id)).toEqual(['e2']);
  });

  it('renders a MesaCard per loaded table', () => {
    const fixture = createComponent();

    expect(fixture.nativeElement.querySelector('[data-table-id="t1"]')).not.toBeNull();
  });

  it('creates a table with an empty judge/entry set and appends it', () => {
    const fixture = createComponent();
    const newTable = tableFixture({ id: 't2', name: 'Mesa 2', judges: [], samples: [] });
    fakeApi.createTable.mockReturnValue(of({ ...newTable, bosFlaggedEntryIds: [] }));

    const input = fixture.nativeElement.querySelector(
      'input[name="newTableName"]',
    ) as HTMLInputElement;
    input.value = 'Mesa 2';
    input.dispatchEvent(new Event('input'));
    fixture.detectChanges();

    buttonWithText(fixture.nativeElement, 'Add table').click();
    fixture.detectChanges();

    expect(fakeApi.createTable).toHaveBeenCalledWith('c1', {
      name: 'Mesa 2',
      judgeIds: [],
      beerEntryIds: [],
    });
    expect(fixture.nativeElement.querySelector('[data-table-id="t2"]')).not.toBeNull();
  });

  it('surfaces a validation error message when table creation fails with 400', () => {
    const fixture = createComponent();
    fakeApi.createTable.mockReturnValue(
      throwError(
        () =>
          new ApiError({
            status: 400,
            title: 'Invalid request',
            urn: 'urn:birrapoint:validation',
            detail: 'A table with this name already exists in this competition.',
          }),
      ),
    );

    const input = fixture.nativeElement.querySelector(
      'input[name="newTableName"]',
    ) as HTMLInputElement;
    input.value = 'Mesa 1';
    input.dispatchEvent(new Event('input'));
    fixture.detectChanges();

    buttonWithText(fixture.nativeElement, 'Add table').click();
    fixture.detectChanges();

    expect(fixture.nativeElement.textContent).toContain(
      'A table with this name already exists in this competition.',
    );
  });

  it('shows the BOS warning banner when a mutation response has non-empty bosFlaggedEntryIds', () => {
    const fixture = createComponent();
    const result: TableMutationResult = {
      ...tableFixture({ id: 't2' }),
      bosFlaggedEntryIds: ['e5', 'e6'],
    };
    fakeApi.createTable.mockReturnValue(of(result));

    const input = fixture.nativeElement.querySelector(
      'input[name="newTableName"]',
    ) as HTMLInputElement;
    input.value = 'Mesa 2';
    input.dispatchEvent(new Event('input'));
    fixture.detectChanges();
    buttonWithText(fixture.nativeElement, 'Add table').click();
    fixture.detectChanges();

    expect(fixture.nativeElement.textContent).toContain('2 entries flagged Not Valid for BOS.');
  });

  it('refetches entries after a successful mutation so BOS flags outside the mutated table stay current', () => {
    // Regression: bosFlaggedEntryIds only reports newly-flagged ids (never unflagged ones), and
    // FR-018 can touch entries far outside the mutated table's own membership — patching `entries`
    // incrementally from the mutation response alone can't stay correct in both directions. Found
    // by T049's E2E: the BOS banner announced correctly but a flagged token's visual state stayed
    // stale until a full reload.
    const fixture = createComponent();
    fakeEntriesApi.getEntries.mockClear();
    const mutationResult: TableMutationResult = {
      ...tableFixture({ judges: [judgesFixture()[0], judgesFixture()[1]] }),
      bosFlaggedEntryIds: ['e5'],
    };
    fakeApi.updateTable.mockReturnValue(of(mutationResult));

    fixture.componentInstance.onJudgeClicked('j2');
    fixture.detectChanges();
    fixture.componentInstance.onModalMove('t1');

    expect(fakeEntriesApi.getEntries).toHaveBeenCalledWith('c1');
  });

  it('opens the beer detail modal with modal content resolved from the entries list', () => {
    const fixture = createComponent();

    fixture.componentInstance.onBeerClicked('e2');
    fixture.detectChanges();

    const text = fixture.nativeElement.textContent as string;
    expect(text).toContain('CD34');
    expect(text).toContain('American IPA');
  });

  it('opens the judge detail modal with modal content resolved from the judges list', () => {
    const fixture = createComponent();

    fixture.componentInstance.onJudgeClicked('j2');
    fixture.detectChanges();

    const text = fixture.nativeElement.textContent as string;
    expect(text).toContain('Grace Hopper');
    expect(text).toContain('grace@example.com');
  });

  it('shows the conflict-of-interest dialog with judge/blind-code resolved messages on a 409', () => {
    const fixture = createComponent();
    fakeApi.updateTable.mockReturnValue(
      throwError(
        () =>
          new ApiError({
            status: 409,
            title: 'Conflict of interest',
            urn: 'urn:birrapoint:conflict-of-interest',
            detail: 'One or more judges have a conflict of interest.',
            extensions: { conflicts: [{ judgeId: 'j2', beerEntryIds: ['e2'] }] },
          }),
      ),
    );

    // Drive the move through the public API a keyboard-only user would use (the detail modal's
    // "Move to" control), rather than simulating a raw CDK drop event.
    fixture.componentInstance.onJudgeClicked('j2');
    fixture.detectChanges();
    fixture.componentInstance.onModalMove('t1');
    fixture.detectChanges();

    const text = fixture.nativeElement.textContent as string;
    expect(text).toContain('Conflict of interest');
    expect(text).toContain('Grace Hopper');
    expect(text).toContain('CD34');
  });

  it('moves an unassigned judge to a table via the keyboard-accessible "Move to" control', () => {
    const fixture = createComponent();
    const mutationResult: TableMutationResult = {
      ...tableFixture({ judges: [judgesFixture()[0], judgesFixture()[1]] }),
      bosFlaggedEntryIds: [],
    };
    fakeApi.updateTable.mockReturnValue(of(mutationResult));

    fixture.componentInstance.onJudgeClicked('j2');
    fixture.detectChanges();
    fixture.componentInstance.onModalMove('t1');

    expect(fakeApi.updateTable).toHaveBeenCalledWith('c1', 't1', {
      name: 'Mesa 1',
      judgeIds: ['j1', 'j2'],
      beerEntryIds: ['e1'],
    });
  });
});
