import { TestBed } from '@angular/core/testing';
import { By } from '@angular/platform-browser';
import { ActivatedRoute, convertToParamMap } from '@angular/router';
import { of } from 'rxjs';

import { EntriesApiService } from '../../core/api/entries-api.service';
import { TableBoardComponent } from './table-board.component';
import { TableManagementApiService } from './table-management-api.service';
import { TableManagementComponent } from './table-management.component';

describe('TableManagementComponent', () => {
  function createComponent(id = 'c1') {
    TestBed.configureTestingModule({
      providers: [
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
});
