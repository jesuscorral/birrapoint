import { TestBed } from '@angular/core/testing';
import { of, throwError } from 'rxjs';

import { ApiError } from '../../core/api/api-error';
import { EntriesApiService } from '../../core/api/entries-api.service';
import type { EntryListItem } from '../../core/api/entries-api.service';
import { TableBoardComponent } from './table-board.component';
import { TableManagementApiService } from './table-management-api.service';
import type {
  JudgeListItem,
  TableMutationResult,
  TableSummary,
} from './table-management-api.service';

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
        abvPercent: 5.2,
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
      abvPercent: 5.2,
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
      abvPercent: 6.8,
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

describe('TableBoardComponent', () => {
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
      ],
    });
  });

  function createComponent(competitionId = 'c1') {
    const fixture = TestBed.createComponent(TableBoardComponent);
    fixture.componentRef.setInput('competitionId', competitionId);
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

    const input = fixture.nativeElement.querySelector('input#new-table-name') as HTMLInputElement;
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

    const input = fixture.nativeElement.querySelector('input#new-table-name') as HTMLInputElement;
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

    const input = fixture.nativeElement.querySelector('input#new-table-name') as HTMLInputElement;
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

  it('opens the beer detail modal with modal content resolved from the entries list, including real ABV%', () => {
    const fixture = createComponent();

    fixture.componentInstance.onBeerClicked('e2');
    fixture.detectChanges();

    const text = fixture.nativeElement.textContent as string;
    expect(text).toContain('CD34');
    expect(text).toContain('American IPA');
    expect(text).toContain('6.8%');
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

  it('uses the competitionId input rather than reading it from the route', () => {
    createComponent('other-competition');

    expect(fakeApi.getTables).toHaveBeenCalledWith('other-competition');
  });

  describe('heading (configurable so an embedding parent, e.g. the wizard, controls level/copy)', () => {
    it('defaults to an <h1>"Table management" heading — byte-for-byte the standalone-route markup E2E locks on', () => {
      const fixture = createComponent();

      const h1 = fixture.nativeElement.querySelector('h1');
      expect(h1?.textContent?.trim()).toBe('Table management');
      expect(fixture.nativeElement.querySelector('h2')).toBeNull();
    });

    it('renders an <h2> with the given text when headingLevel=2/heading is overridden', () => {
      const fixture = TestBed.createComponent(TableBoardComponent);
      fixture.componentRef.setInput('competitionId', 'c1');
      fixture.componentRef.setInput('headingLevel', 2);
      fixture.componentRef.setInput('heading', 'Mesas');
      fixture.detectChanges();

      expect(fixture.nativeElement.querySelector('h1')).toBeNull();
      const h2 = fixture.nativeElement.querySelector('h2');
      expect(h2?.textContent?.trim()).toBe('Mesas');
    });
  });

  it("associates the add-table label with its input via for/id (bp-input wiring, pinned for getByLabel('New table name'))", () => {
    const fixture = createComponent();

    const label = fixture.nativeElement.querySelector(
      'label[for="new-table-name"]',
    ) as HTMLLabelElement | null;
    expect(label?.textContent?.trim()).toBe('New table name');
    const input = fixture.nativeElement.querySelector('#new-table-name');
    expect(input?.tagName).toBe('INPUT');
  });

  it('exposes exactly one role="status" region, containing a Dismiss button, when the BOS banner is shown (pinned for getByRole("status"))', () => {
    const fixture = createComponent();
    const result: TableMutationResult = {
      ...tableFixture({ id: 't2' }),
      bosFlaggedEntryIds: ['e5'],
    };
    fakeApi.createTable.mockReturnValue(of(result));

    const input = fixture.nativeElement.querySelector('input#new-table-name') as HTMLInputElement;
    input.value = 'Mesa 2';
    input.dispatchEvent(new Event('input'));
    fixture.detectChanges();
    buttonWithText(fixture.nativeElement, 'Add table').click();
    fixture.detectChanges();

    const statusRegions = fixture.nativeElement.querySelectorAll('[role="status"]');
    expect(statusRegions.length).toBe(1);
    const dismissButton = [...statusRegions[0].querySelectorAll('button')].find(
      (button: HTMLButtonElement) => button.textContent?.trim() === 'Dismiss',
    );
    expect(dismissButton).toBeTruthy();
  });

  describe('dirtyChange (FR-007: an un-submitted "Add table" name must not be silently discarded)', () => {
    it('emits false on init', () => {
      const fixture = TestBed.createComponent(TableBoardComponent);
      const emitted: boolean[] = [];
      fixture.componentInstance.dirtyChange.subscribe((value) => emitted.push(value));
      fixture.componentRef.setInput('competitionId', 'c1');
      fixture.detectChanges();

      expect(emitted).toEqual([false]);
    });

    it('emits true once the organizer types a non-blank table name, and false again once cleared', () => {
      const fixture = createComponent();
      const emitted: boolean[] = [];
      fixture.componentInstance.dirtyChange.subscribe((value) => emitted.push(value));

      const input = fixture.nativeElement.querySelector('input#new-table-name') as HTMLInputElement;
      input.value = 'Mesa en progreso';
      input.dispatchEvent(new Event('input'));
      fixture.detectChanges();
      expect(emitted).toEqual([true]);

      input.value = '';
      input.dispatchEvent(new Event('input'));
      fixture.detectChanges();
      expect(emitted).toEqual([true, false]);
    });

    it('treats a whitespace-only name as not dirty', () => {
      const fixture = createComponent();
      const emitted: boolean[] = [];
      fixture.componentInstance.dirtyChange.subscribe((value) => emitted.push(value));

      const input = fixture.nativeElement.querySelector('input#new-table-name') as HTMLInputElement;
      input.value = '   ';
      input.dispatchEvent(new Event('input'));
      fixture.detectChanges();

      // The effect re-runs on any newTableName signal write (even '' -> '   ', still trim()==='')
      // and may re-emit `false` again -- what matters is that `true` is never emitted for
      // whitespace-only content.
      expect(emitted.every((value) => value === false)).toBe(true);
    });

    it('emits false again once the table is created and the name field clears', () => {
      const fixture = createComponent();
      fakeApi.createTable.mockReturnValue(
        of({ ...tableFixture({ id: 't2', judges: [], samples: [] }), bosFlaggedEntryIds: [] }),
      );
      const emitted: boolean[] = [];
      fixture.componentInstance.dirtyChange.subscribe((value) => emitted.push(value));

      const input = fixture.nativeElement.querySelector('input#new-table-name') as HTMLInputElement;
      input.value = 'Mesa 2';
      input.dispatchEvent(new Event('input'));
      fixture.detectChanges();
      expect(emitted).toEqual([true]);

      buttonWithText(fixture.nativeElement, 'Add table').click();
      fixture.detectChanges();

      expect(emitted).toEqual([true, false]);
    });
  });
});
