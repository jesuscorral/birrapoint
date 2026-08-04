import { TestBed } from '@angular/core/testing';
import { By } from '@angular/platform-browser';
import { provideRouter, Router } from '@angular/router';
import { of } from 'rxjs';

import { EntriesApiService } from '../../../core/api/entries-api.service';
import { TableBoardComponent } from '../../table-management/table-board.component';
import { TableManagementApiService } from '../../table-management/table-management-api.service';
import { TablesStepComponent } from './tables-step.component';

function buttonWithText(root: Element, text: string): HTMLButtonElement {
  const buttons = [...root.querySelectorAll('button')] as HTMLButtonElement[];
  const match = buttons.find((button) => button.textContent?.trim() === text);
  if (!match) {
    throw new Error(`No button with text "${text}" found`);
  }
  return match;
}

describe('TablesStepComponent', () => {
  let fakeApi: {
    getTables: jest.Mock;
    getJudges: jest.Mock;
    createTable: jest.Mock;
    updateTable: jest.Mock;
  };
  let fakeEntriesApi: { getEntries: jest.Mock };

  beforeEach(() => {
    fakeApi = {
      getTables: jest.fn().mockReturnValue(of([])),
      getJudges: jest.fn().mockReturnValue(of([])),
      createTable: jest.fn(),
      updateTable: jest.fn(),
    };
    fakeEntriesApi = { getEntries: jest.fn().mockReturnValue(of([])) };

    TestBed.configureTestingModule({
      providers: [
        { provide: TableManagementApiService, useValue: fakeApi },
        { provide: EntriesApiService, useValue: fakeEntriesApi },
        provideRouter([]),
      ],
    });
  });

  function createComponent(competitionId = 'c1') {
    const fixture = TestBed.createComponent(TablesStepComponent);
    fixture.componentRef.setInput('competitionId', competitionId);
    fixture.detectChanges();
    return fixture;
  }

  it('renders app-table-board with the competitionId input and a nested h2 "Mesas" heading (not a second h1)', () => {
    const fixture = createComponent('c1');

    const boardDebugEl = fixture.debugElement.query(By.directive(TableBoardComponent));
    expect(boardDebugEl).toBeTruthy();
    expect(boardDebugEl.componentInstance.competitionId()).toBe('c1');
    expect(fakeApi.getTables).toHaveBeenCalledWith('c1');

    expect(fixture.nativeElement.querySelector('h1')).toBeNull();
    const h2 = fixture.nativeElement.querySelector('h2');
    expect(h2?.textContent?.trim()).toBe('Mesas');
  });

  it('emits dirtyChange(false) on init (forwarded from the embedded table-board)', () => {
    const fixture = TestBed.createComponent(TablesStepComponent);
    const emitted: boolean[] = [];
    fixture.componentInstance.dirtyChange.subscribe((value) => emitted.push(value));
    fixture.componentRef.setInput('competitionId', 'c1');
    fixture.detectChanges();

    expect(emitted).toEqual([false]);
  });

  it('forwards dirtyChange(true) once the embedded table-board has an un-submitted table name (FR-007)', () => {
    const fixture = createComponent();
    const emitted: boolean[] = [];
    fixture.componentInstance.dirtyChange.subscribe((value) => emitted.push(value));

    const input = fixture.nativeElement.querySelector('input#new-table-name') as HTMLInputElement;
    input.value = 'Mesa en progreso';
    input.dispatchEvent(new Event('input'));
    fixture.detectChanges();

    expect(emitted).toEqual([true]);
  });

  it('emits back when "← Volver" is clicked', () => {
    const fixture = createComponent();
    const emitted: void[] = [];
    fixture.componentInstance.back.subscribe(() => emitted.push(undefined));

    buttonWithText(fixture.nativeElement, '← Volver').click();

    expect(emitted.length).toBe(1);
  });

  it('navigates to /organizer/dashboard when "Ir al panel de organizador" is clicked', () => {
    const fixture = createComponent();
    const router = TestBed.inject(Router);
    const navigateSpy = jest.spyOn(router, 'navigateByUrl');

    buttonWithText(fixture.nativeElement, 'Ir al panel de organizador').click();

    expect(navigateSpy).toHaveBeenCalledWith('/organizer/dashboard');
  });
});
