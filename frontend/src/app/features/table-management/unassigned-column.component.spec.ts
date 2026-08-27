import { TestBed } from '@angular/core/testing';

import type { EntryListItem } from '../../core/api/entries-api.service';
import { UnassignedColumnComponent } from './unassigned-column.component';
import type { JudgeListItem } from './table-management-api.service';

function judgesFixture(): JudgeListItem[] {
  return [{ id: 'j1', email: 'ada@example.com', displayName: 'Ada Lovelace' }];
}

function beersFixture(): EntryListItem[] {
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
      competitionCategoryName: 'Estilos clásicos',
      bjcpCategoryNumber: '4',
      bjcpCategoryName: 'Pale Malty European Lager',
      tastingTableId: null,
      tastingTableName: null,
    },
  ];
}

describe('UnassignedColumnComponent', () => {
  function createComponent() {
    const fixture = TestBed.createComponent(UnassignedColumnComponent);
    fixture.componentRef.setInput('judges', judgesFixture());
    fixture.componentRef.setInput('beers', beersFixture());
    fixture.componentRef.setInput('beersTotal', beersFixture().length);
    fixture.componentRef.setInput('connectedJudgeListIds', ['judges-unassigned']);
    fixture.componentRef.setInput('connectedBeerListIds', ['beers-unassigned']);
    fixture.detectChanges();
    return fixture;
  }

  it('renders unassigned judges and beers as plain-list draggable items', () => {
    const fixture = createComponent();

    expect(fixture.nativeElement.querySelector('[data-judge-id="j1"]')).not.toBeNull();
    expect(fixture.nativeElement.querySelector('[data-entry-id="e1"]')).not.toBeNull();
  });

  it('shows the live unassigned judge/beer counts in the column headings', () => {
    const fixture = createComponent();

    const headings = [...fixture.nativeElement.querySelectorAll('h3')] as HTMLElement[];
    expect(headings[0].textContent?.trim()).toBe('Jueces sin asignar (1)');
    expect(headings[1].textContent?.trim()).toBe('Cervezas sin asignar (1)');
  });

  it('reflects an empty count when there are no unassigned judges/beers', () => {
    const fixture = TestBed.createComponent(UnassignedColumnComponent);
    fixture.componentRef.setInput('judges', []);
    fixture.componentRef.setInput('beers', []);
    fixture.componentRef.setInput('beersTotal', 0);
    fixture.componentRef.setInput('connectedJudgeListIds', ['judges-unassigned']);
    fixture.componentRef.setInput('connectedBeerListIds', ['beers-unassigned']);
    fixture.detectChanges();

    const headings = [...fixture.nativeElement.querySelectorAll('h3')] as HTMLElement[];
    expect(headings[0].textContent?.trim()).toBe('Jueces sin asignar (0)');
    expect(headings[1].textContent?.trim()).toBe('Cervezas sin asignar (0)');
  });

  // T124: this column is the only place wide enough to show what the organizer picks the next
  // beer by, so its tokens render in the detailed `full` variant.
  it('renders unassigned beers in the full token variant, with style, category and ABV', () => {
    const fixture = createComponent();

    const token = fixture.nativeElement.querySelector('[data-entry-id="e1"]') as HTMLElement;
    expect(token.classList.contains('beer-token--full')).toBe(true);
    expect(token.querySelector('.beer-token__style')?.textContent?.trim()).toBe('Munich Helles');

    const chips = [...token.querySelectorAll('.beer-token__chip')].map((chip) =>
      chip.textContent?.trim(),
    );
    expect(chips).toEqual(['Estilos clásicos', '4 · Pale Malty European Lager', '5.2% ABV']);
  });

  it('shows the judge display name next to the avatar', () => {
    const fixture = createComponent();

    const seat = fixture.nativeElement.querySelector('[data-judge-id="j1"]') as HTMLElement;
    expect(seat.querySelector('.judge-seat__name')?.textContent?.trim()).toBe('Ada Lovelace');
  });

  it('exposes stable drop list ids for cross-component connection', () => {
    const fixture = createComponent();

    const lists = fixture.nativeElement.querySelectorAll('.unassigned-list');
    expect(lists[0].id).toBe('judges-unassigned');
    expect(lists[1].id).toBe('beers-unassigned');
  });

  it('emits judgeActivated / beerActivated when items are activated', () => {
    const fixture = createComponent();
    const judgeActivated = jest.fn();
    const beerActivated = jest.fn();
    fixture.componentInstance.judgeActivated.subscribe(judgeActivated);
    fixture.componentInstance.beerActivated.subscribe(beerActivated);

    (fixture.nativeElement.querySelector('[data-judge-id="j1"]') as HTMLDivElement).dispatchEvent(
      new KeyboardEvent('keydown', { key: 'Enter' }),
    );
    (fixture.nativeElement.querySelector('[data-entry-id="e1"]') as HTMLDivElement).dispatchEvent(
      new KeyboardEvent('keydown', { key: 'Enter' }),
    );

    expect(judgeActivated).toHaveBeenCalledWith('j1');
    expect(beerActivated).toHaveBeenCalledWith('e1');
  });
});
