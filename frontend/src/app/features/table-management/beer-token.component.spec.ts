import { TestBed } from '@angular/core/testing';

import { BeerTokenComponent } from './beer-token.component';
import type { BeerTokenData, BeerTokenVariant } from './beer-token.component';

describe('BeerTokenComponent', () => {
  function beerFixture(overrides: Partial<BeerTokenData> = {}): BeerTokenData {
    return {
      id: 'e1',
      blindCode: 'AB12',
      styleCode: '21A',
      notValidForBos: false,
      styleName: 'American IPA',
      abvPercent: 6.8,
      competitionCategoryName: 'Estilos clásicos',
      bjcpCategoryNumber: '21',
      bjcpCategoryName: 'IPA',
      categoryColor: '#d7e6f4',
      ...overrides,
    };
  }

  function createComponent(beer: BeerTokenData, variant?: BeerTokenVariant) {
    const fixture = TestBed.createComponent(BeerTokenComponent);
    fixture.componentRef.setInput('beer', beer);
    if (variant) {
      fixture.componentRef.setInput('variant', variant);
    }
    fixture.detectChanges();
    return fixture;
  }

  function tokenOf(fixture: ReturnType<typeof createComponent>): HTMLDivElement {
    return fixture.nativeElement.querySelector('.beer-token') as HTMLDivElement;
  }

  it('renders the blind code and a data-entry-id attribute (full variant)', () => {
    const fixture = createComponent(beerFixture(), 'full');

    const token = tokenOf(fixture);
    expect(token.querySelector('.beer-token__code')?.textContent?.trim()).toBe('AB12');
    expect(token.getAttribute('data-entry-id')).toBe('e1');
  });

  // T124 — this is the whole point of the `full` variant: while assigning a beer to a table the
  // organizer needs style, competition category and real ABV without opening the detail modal.
  it('renders style, competition category, BJCP category and ABV in the full variant', () => {
    const fixture = createComponent(beerFixture(), 'full');

    const token = tokenOf(fixture);
    expect(token.classList.contains('beer-token--full')).toBe(true);
    expect(token.querySelector('.beer-token__style')?.textContent?.trim()).toBe('American IPA');

    const chips = Array.from(token.querySelectorAll('.beer-token__chip')).map((chip) =>
      chip.textContent?.trim(),
    );
    expect(chips).toEqual(['Estilos clásicos', '21 · IPA', '6.8% ABV']);
  });

  it('omits the competition-category chip when the entry has no category', () => {
    const fixture = createComponent(beerFixture({ competitionCategoryName: null }), 'full');

    const chips = Array.from(tokenOf(fixture).querySelectorAll('.beer-token__chip')).map((chip) =>
      chip.textContent?.trim(),
    );
    expect(chips).toEqual(['21 · IPA', '6.8% ABV']);
  });

  it('omits the BJCP chip when the style code has no catalog row', () => {
    const fixture = createComponent(
      beerFixture({ bjcpCategoryNumber: null, bjcpCategoryName: null }),
      'full',
    );

    const chips = Array.from(tokenOf(fixture).querySelectorAll('.beer-token__chip')).map((chip) =>
      chip.textContent?.trim(),
    );
    expect(chips).toEqual(['Estilos clásicos', '6.8% ABV']);
  });

  // T125c: the mini token is a compact seated pill — BJCP style code plus the bare graduation
  // only; no icon, no style name, no chips. The full variant (asserted above/below) is untouched
  // by this compaction.
  it('renders the style code and bare graduation in the mini variant, with no icon, style name or chips', () => {
    const fixture = createComponent(beerFixture(), 'mini');

    const token = tokenOf(fixture);
    expect(token.classList.contains('beer-token--mini')).toBe(true);
    expect(token.querySelector('.beer-token__code')?.textContent?.trim()).toBe('21A');
    expect(token.querySelector('svg.beer-token__icon')).toBeNull();
    expect(token.querySelector('.beer-token__style')).toBeNull();
    expect(token.querySelector('.beer-token__chip')).toBeNull();
  });

  // The graduation is shown WITHOUT the "ABV" suffix the full variant's chip carries — the pill
  // has no room for a unit the percent sign already implies.
  it('shows the graduation as a bare percent in the mini variant, never suffixed with ABV', () => {
    const fixture = createComponent(beerFixture(), 'mini');

    const abv = tokenOf(fixture).querySelector('.beer-token__abv-mini');
    expect(abv?.textContent?.trim()).toBe('6.8%');
    expect(abv?.textContent).not.toContain('ABV');
  });

  it('shows the blind code (not the style code) as the visible label in the full variant', () => {
    const fixture = createComponent(beerFixture(), 'full');

    const token = tokenOf(fixture);
    expect(token.querySelector('.beer-token__code')?.textContent?.trim()).toBe('AB12');
    expect(token.querySelector('svg.beer-token__icon')).not.toBeNull();
  });

  it('sets the background to the resolved categoryColor', () => {
    const fixture = createComponent(beerFixture({ categoryColor: '#f4e2d7' }));

    // jsdom's CSSOM normalizes hex colors to rgb() on read; #f4e2d7 = rgb(244, 226, 215).
    expect(tokenOf(fixture).style.background).toContain('rgb(244, 226, 215)');
  });

  it('defaults to the mini variant (the seated form on a MesaCard)', () => {
    const fixture = createComponent(beerFixture());

    expect(tokenOf(fixture).classList.contains('beer-token--mini')).toBe(true);
  });

  it('marks a BOS-flagged entry with the flagged class', () => {
    const fixture = createComponent(beerFixture({ notValidForBos: true }));

    expect(tokenOf(fixture).classList.contains('beer-token--bos-flagged')).toBe(true);
  });

  // WCAG 1.3.1: the visual chips are aria-hidden (they would otherwise be announced as loose
  // fragments), so the same information has to reach a screen reader as a description.
  it('describes style, category and ABV to a screen reader without changing the accessible name', () => {
    const fixture = createComponent(beerFixture(), 'full');

    const token = tokenOf(fixture);
    expect(token.getAttribute('aria-label')).toBe('Beer AB12 — view details');

    const describedById = token.getAttribute('aria-describedby');
    expect(describedById).toBe('beer-note-e1');

    const note = fixture.nativeElement.querySelector(`#${describedById}`) as HTMLElement;
    expect(note.textContent?.trim()).toBe(
      'American IPA, categoría Estilos clásicos, BJCP 21 · IPA, 6.8% ABV',
    );
  });

  it('conveys the BOS-flagged state via a visible marker and aria-describedby, not color alone, while leaving the accessible name unchanged (WCAG 1.4.1)', () => {
    const fixture = createComponent(beerFixture({ notValidForBos: true }));

    const token = tokenOf(fixture);
    expect(token.getAttribute('aria-label')).toBe('Beer AB12 — view details');

    // Both the details description and the BOS note, in that order.
    expect(token.getAttribute('aria-describedby')).toBe('beer-note-e1 bos-note-e1');

    const bosNote = fixture.nativeElement.querySelector('#bos-note-e1') as HTMLElement;
    expect(bosNote.textContent?.trim()).toBe('Not valid for Best of Show');

    const marker = token.querySelector('.bos-marker') as HTMLElement;
    expect(marker).not.toBeNull();
    expect(marker.getAttribute('aria-hidden')).toBe('true');
  });

  it('renders no BOS marker or BOS note when not flagged', () => {
    const fixture = createComponent(beerFixture());

    const token = tokenOf(fixture);
    expect(token.getAttribute('aria-describedby')).toBe('beer-note-e1');
    expect(token.querySelector('.bos-marker')).toBeNull();
    expect(fixture.nativeElement.querySelector('#bos-note-e1')).toBeNull();
  });

  it('emits activated on Enter keydown (keyboard-accessible click equivalent)', () => {
    const fixture = createComponent(beerFixture());
    const activated = jest.fn();
    fixture.componentInstance.activated.subscribe(activated);

    tokenOf(fixture).dispatchEvent(new KeyboardEvent('keydown', { key: 'Enter' }));

    expect(activated).toHaveBeenCalledTimes(1);
  });
});
