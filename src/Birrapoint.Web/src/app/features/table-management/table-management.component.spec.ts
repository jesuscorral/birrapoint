import Keycloak from 'keycloak-js';
import { TestBed } from '@angular/core/testing';
import { By } from '@angular/platform-browser';
import { ActivatedRoute, convertToParamMap } from '@angular/router';
import { of, throwError, Subject } from 'rxjs';

import type { CompetitionDetail, CompetitionState } from '../../core/api/competitions-api.service';
import { CompetitionsApiService } from '../../core/api/competitions-api.service';
import { EntriesApiService } from '../../core/api/entries-api.service';
import { TableBoardComponent } from './table-board.component';
import { TableManagementApiService } from './table-management-api.service';
import { TableManagementComponent } from './table-management.component';

function competitionFixture(state: CompetitionState): CompetitionDetail {
  return {
    id: 'c1',
    name: 'Golden Ale Cup',
    venue: 'Town Hall',
    startDate: '2026-08-01',
    endDate: '2026-08-02',
    description: null,
    logoUrl: null,
    entryLimit: null,
    registrationStart: null,
    registrationEnd: null,
    state,
  };
}

describe('TableManagementComponent', () => {
  let fakeCompetitionsApi: { getById: jest.Mock };

  beforeEach(() => {
    fakeCompetitionsApi = { getById: jest.fn().mockReturnValue(of(competitionFixture('Active'))) };
  });

  function createComponent(id = 'c1') {
    TestBed.configureTestingModule({
      providers: [
        {
          provide: Keycloak,
          useValue: { tokenParsed: { realm_access: { roles: ['ORGANIZER'] } }, logout: jest.fn() },
        },
        {
          provide: TableManagementApiService,
          useValue: {
            getTables: jest.fn().mockReturnValue(of([])),
            getJudges: jest.fn().mockReturnValue(of([])),
            createTable: jest.fn(),
            updateTable: jest.fn(),
          },
        },
        { provide: EntriesApiService, useValue: { getEntries: jest.fn().mockReturnValue(of([])) } },
        { provide: CompetitionsApiService, useValue: fakeCompetitionsApi },
        {
          provide: ActivatedRoute,
          useValue: { snapshot: { paramMap: convertToParamMap({ id }) } },
        },
      ],
    });
    const fixture = TestBed.createComponent(TableManagementComponent);
    fixture.detectChanges();
    return fixture;
  }

  it("reads the :id route param and passes it as app-table-board's competitionId input", () => {
    const fixture = createComponent('c1');

    const boardDebugEl = fixture.debugElement.query(By.directive(TableBoardComponent));
    expect(boardDebugEl).toBeTruthy();
    expect(boardDebugEl.componentInstance.competitionId()).toBe('c1');
  });

  it('renders app-table-board in the template', () => {
    const fixture = createComponent('c1');

    expect(fixture.nativeElement.querySelector('app-table-board')).toBeTruthy();
  });

  it('wraps the board in the shared page shell', () => {
    const fixture = createComponent('c1');

    expect(fixture.nativeElement.querySelector('bp-page-shell')).toBeTruthy();
  });

  // FR-061 / Session 2026-09-19 clarification: read-only once InEvaluation/Finalized.
  it('renders the board immediately, before the competition state fetch resolves', () => {
    const pending = new Subject<CompetitionDetail>();
    fakeCompetitionsApi.getById.mockReturnValue(pending.asObservable());

    const fixture = createComponent('c1');

    const boardDebugEl = fixture.debugElement.query(By.directive(TableBoardComponent));
    expect(boardDebugEl).toBeTruthy();
    expect(boardDebugEl.componentInstance.readOnly()).toBe(false);
  });

  it('passes readOnly=true once the competition is InEvaluation', () => {
    fakeCompetitionsApi.getById.mockReturnValue(of(competitionFixture('InEvaluation')));
    const fixture = createComponent('c1');

    const boardDebugEl = fixture.debugElement.query(By.directive(TableBoardComponent));
    expect(boardDebugEl.componentInstance.readOnly()).toBe(true);
  });

  it('passes readOnly=true once the competition is Finalized', () => {
    fakeCompetitionsApi.getById.mockReturnValue(of(competitionFixture('Finalized')));
    const fixture = createComponent('c1');

    const boardDebugEl = fixture.debugElement.query(By.directive(TableBoardComponent));
    expect(boardDebugEl.componentInstance.readOnly()).toBe(true);
  });

  it('keeps readOnly=false for Draft/Active competitions', () => {
    fakeCompetitionsApi.getById.mockReturnValue(of(competitionFixture('Active')));
    const fixture = createComponent('c1');

    const boardDebugEl = fixture.debugElement.query(By.directive(TableBoardComponent));
    expect(boardDebugEl.componentInstance.readOnly()).toBe(false);
  });

  it('stays editable if the competition state fetch fails', () => {
    fakeCompetitionsApi.getById.mockReturnValue(throwError(() => new Error('boom')));
    const fixture = createComponent('c1');

    const boardDebugEl = fixture.debugElement.query(By.directive(TableBoardComponent));
    expect(boardDebugEl.componentInstance.readOnly()).toBe(false);
  });
});
