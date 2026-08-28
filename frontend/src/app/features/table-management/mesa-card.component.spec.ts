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
        abvPercent: 5.2,
        abvLow: 4.5,
        abvHigh: 5.5,
        notValidForBos: false,
        competitionCategoryName: 'Estilos clásicos',
        bjcpCategoryNumber: '4',
        bjcpCategoryName: 'Pale Malty European Lager',
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

  it('shows the judge count and beer count for quick balance-at-a-glance', () => {
    const fixture = createComponent(tableFixture());

    // tableFixture() has 2 judges and 1 sample. Asserted against the specific stat elements, not
    // generic textContent.toContain — the fixture's ABV (5.2%) and progress (1/3) already contain
    // both "2" and "1" as digits, so a bare toContain assertion would pass even if these stat
    // <dd>s were deleted entirely.
    const judgesDd = fixture.nativeElement.querySelector('dd[data-stat="judges"]') as HTMLElement;
    const beersDd = fixture.nativeElement.querySelector('dd[data-stat="beers"]') as HTMLElement;
    expect(judgesDd.textContent?.trim()).toBe('2');
    expect(beersDd.textContent?.trim()).toBe('1');
  });

  it('recomputes the judge/beer counts live from table().judges/samples, not a cached value', () => {
    const table = {
      ...tableFixture(),
      judges: [],
      samples: [],
    };
    const fixture = createComponent(table);

    const judgesDd = fixture.nativeElement.querySelector('dd[data-stat="judges"]') as HTMLElement;
    const beersDd = fixture.nativeElement.querySelector('dd[data-stat="beers"]') as HTMLElement;
    expect(judgesDd.textContent?.trim()).toBe('0');
    expect(beersDd.textContent?.trim()).toBe('0');
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

    const seat = fixture.nativeElement.querySelector('[data-judge-id="j1"]') as HTMLDivElement;
    seat.dispatchEvent(new KeyboardEvent('keydown', { key: 'Enter' }));

    expect(activated).toHaveBeenCalledWith('j1');
  });

  it('emits beerActivated when a seated beer token is activated', () => {
    const fixture = createComponent(tableFixture());
    const activated = jest.fn();
    fixture.componentInstance.beerActivated.subscribe(activated);

    const token = fixture.nativeElement.querySelector('[data-entry-id="e1"]') as HTMLDivElement;
    token.dispatchEvent(new KeyboardEvent('keydown', { key: 'Enter' }));

    expect(activated).toHaveBeenCalledWith('e1');
  });

  // T124: the two numbers the organizer balances a table on get visual primacy over the
  // supporting counts, and the style names get their own line instead of being crammed into the
  // "Estilos" value as "1 (Munich Helles)".
  it('gives beer count and mean ABV primary emphasis and lists style names separately', () => {
    const fixture = createComponent(tableFixture());

    const primary = [
      ...fixture.nativeElement.querySelectorAll('.mesa-stats__item--primary dt'),
    ] as HTMLElement[];
    expect(primary.map((dt) => dt.textContent?.trim())).toEqual(['Cervezas', 'Media ABV']);

    const styleCountDd = [...fixture.nativeElement.querySelectorAll('.mesa-stats__item')]
      .find((item) => (item as HTMLElement).querySelector('dt')?.textContent?.trim() === 'Estilos')
      ?.querySelector('dd') as HTMLElement;
    expect(styleCountDd.textContent?.trim()).toBe('1');

    const styles = fixture.nativeElement.querySelector('.mesa-styles') as HTMLElement;
    expect(styles.textContent?.trim()).toBe('Munich Helles');
  });

  it('joins several style names on the styles line', () => {
    const table = {
      ...tableFixture(),
      stats: { meanAbv: 5.2, styleCount: 2, styles: ['Munich Helles', 'American IPA'] },
    };
    const fixture = createComponent(table);

    expect(
      (fixture.nativeElement.querySelector('.mesa-styles') as HTMLElement).textContent?.trim(),
    ).toBe('Munich Helles · American IPA');
  });

  // The drop zones are the point of the card, so they have to read as targets while still empty.
  it('shows drop hints in the empty judge and beer zones', () => {
    const fixture = createComponent({ ...tableFixture(), judges: [], samples: [] });

    const hints = [...fixture.nativeElement.querySelectorAll('.mesa-empty')] as HTMLElement[];
    expect(hints.map((hint) => hint.textContent?.trim())).toEqual([
      'Arrastra jueces aquí',
      'Arrastra cervezas aquí',
    ]);
    expect(hints.every((hint) => hint.getAttribute('aria-hidden') === 'true')).toBe(true);
  });

  it('seats beers in the compact mini token variant', () => {
    const fixture = createComponent(tableFixture());

    const token = fixture.nativeElement.querySelector('[data-entry-id="e1"]') as HTMLElement;
    expect(token.classList.contains('beer-token--mini')).toBe(true);
  });

  it('assigns the judge and beer drop lists deterministic ids for cross-component connection', () => {
    const fixture = createComponent(tableFixture());

    expect(fixture.nativeElement.querySelector('.mesa-seats')?.id).toBe('judges-t1');
    expect(fixture.nativeElement.querySelector('.mesa-tokens')?.id).toBe('beers-t1');
  });
});
