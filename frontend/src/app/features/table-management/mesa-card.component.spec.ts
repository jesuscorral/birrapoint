import { TestBed } from '@angular/core/testing';

import { MesaCardComponent } from './mesa-card.component';
import type { TableSummary } from './table-management-api.service';

function tableFixture(): TableSummary {
  return {
    id: 't1',
    name: 'Mesa 1',
    state: 'Open',
    judges: [
      { id: 'j1', email: 'ada@example.com', displayName: 'Ada Lovelace' },
      { id: 'j2', email: 'grace@example.com', displayName: 'Grace Hopper' },
    ],
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
    progress: { submitted: 1, total: 3 },
    stats: { meanAbv: 5.2, styleCount: 1, styles: ['Munich Helles'] },
  };
}

describe('MesaCardComponent', () => {
  function createComponent(table: TableSummary) {
    const fixture = TestBed.createComponent(MesaCardComponent);
    fixture.componentRef.setInput('table', table);
    fixture.componentRef.setInput('connectedJudgeListIds', ['judges-unassigned', 'judges-t1']);
    fixture.componentRef.setInput('connectedBeerListIds', ['beers-unassigned', 'beers-t1']);
    fixture.detectChanges();
    return fixture;
  }

  it('renders the table name and always-visible stats summary', () => {
    const fixture = createComponent(tableFixture());

    const text = fixture.nativeElement.textContent as string;
    expect(text).toContain('Mesa 1');
    expect(text).toContain('5.2%');
    expect(text).toContain('Munich Helles');
    expect(text).toContain('1/3');
  });

  it('shows a placeholder when mean ABV is unavailable', () => {
    const table = { ...tableFixture(), stats: { meanAbv: null, styleCount: 0, styles: [] } };
    const fixture = createComponent(table);

    const text = fixture.nativeElement.textContent as string;
    expect(text).toContain('—');
  });

  it('renders one judge seat per assigned judge with a data-table-id host attribute', () => {
    const fixture = createComponent(tableFixture());

    const article = fixture.nativeElement.querySelector('article') as HTMLElement;
    expect(article.getAttribute('data-table-id')).toBe('t1');
    expect(fixture.nativeElement.querySelectorAll('[data-judge-id]').length).toBe(2);
    expect(fixture.nativeElement.querySelectorAll('[data-entry-id]').length).toBe(1);
  });

  it('emits judgeActivated when a seated judge is activated', () => {
    const fixture = createComponent(tableFixture());
    const activated = jest.fn();
    fixture.componentInstance.judgeActivated.subscribe(activated);

    const seat = fixture.nativeElement.querySelector('[data-judge-id="j1"]') as HTMLLIElement;
    seat.dispatchEvent(new KeyboardEvent('keydown', { key: 'Enter' }));

    expect(activated).toHaveBeenCalledWith('j1');
  });

  it('emits beerActivated when a seated beer token is activated', () => {
    const fixture = createComponent(tableFixture());
    const activated = jest.fn();
    fixture.componentInstance.beerActivated.subscribe(activated);

    const token = fixture.nativeElement.querySelector('[data-entry-id="e1"]') as HTMLLIElement;
    token.dispatchEvent(new KeyboardEvent('keydown', { key: 'Enter' }));

    expect(activated).toHaveBeenCalledWith('e1');
  });

  it('assigns the judge and beer drop lists deterministic ids for cross-component connection', () => {
    const fixture = createComponent(tableFixture());

    expect(fixture.nativeElement.querySelector('.mesa-seats')?.id).toBe('judges-t1');
    expect(fixture.nativeElement.querySelector('.mesa-tokens')?.id).toBe('beers-t1');
  });
});
