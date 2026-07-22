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
      abvLow: 4.5,
      abvHigh: 5.5,
      beerName: 'Golden Helles',
      notValidForBos: false,
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

    (fixture.nativeElement.querySelector('[data-judge-id="j1"]') as HTMLLIElement).dispatchEvent(
      new KeyboardEvent('keydown', { key: 'Enter' }),
    );
    (fixture.nativeElement.querySelector('[data-entry-id="e1"]') as HTMLLIElement).dispatchEvent(
      new KeyboardEvent('keydown', { key: 'Enter' }),
    );

    expect(judgeActivated).toHaveBeenCalledWith('j1');
    expect(beerActivated).toHaveBeenCalledWith('e1');
  });
});
