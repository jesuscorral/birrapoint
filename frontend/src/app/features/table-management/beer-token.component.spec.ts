import { TestBed } from '@angular/core/testing';

import { BeerTokenComponent } from './beer-token.component';
import type { BeerTokenData } from './beer-token.component';

describe('BeerTokenComponent', () => {
  function createComponent(beer: BeerTokenData) {
    const fixture = TestBed.createComponent(BeerTokenComponent);
    fixture.componentRef.setInput('beer', beer);
    fixture.detectChanges();
    return fixture;
  }

  it('renders the blind code and a data-entry-id attribute', () => {
    const fixture = createComponent({ id: 'e1', blindCode: 'AB12', notValidForBos: false });

    const token = fixture.nativeElement.querySelector('div') as HTMLDivElement;
    expect(token.textContent?.trim()).toBe('AB12');
    expect(token.getAttribute('data-entry-id')).toBe('e1');
  });

  it('marks a BOS-flagged entry with the flagged class', () => {
    const fixture = createComponent({ id: 'e1', blindCode: 'AB12', notValidForBos: true });

    const token = fixture.nativeElement.querySelector('div') as HTMLDivElement;
    expect(token.classList.contains('beer-token--bos-flagged')).toBe(true);
  });

  it('conveys the BOS-flagged state via a visible marker and aria-describedby, not color alone, while leaving the accessible name unchanged (WCAG 1.4.1)', () => {
    const fixture = createComponent({ id: 'e1', blindCode: 'AB12', notValidForBos: true });

    const token = fixture.nativeElement.querySelector('div') as HTMLDivElement;
    expect(token.getAttribute('aria-label')).toBe('Beer AB12 — view details');

    const describedById = token.getAttribute('aria-describedby');
    expect(describedById).toBeTruthy();

    const note = fixture.nativeElement.querySelector(`#${describedById}`) as HTMLElement;
    expect(note.textContent?.trim()).toBe('Not valid for Best of Show');

    const marker = token.querySelector('.bos-marker') as HTMLElement;
    expect(marker).not.toBeNull();
    expect(marker.getAttribute('aria-hidden')).toBe('true');
  });

  it('renders no BOS marker, note, or aria-describedby when not flagged', () => {
    const fixture = createComponent({ id: 'e1', blindCode: 'AB12', notValidForBos: false });

    const token = fixture.nativeElement.querySelector('div') as HTMLDivElement;
    expect(token.hasAttribute('aria-describedby')).toBe(false);
    expect(token.querySelector('.bos-marker')).toBeNull();
    expect(fixture.nativeElement.querySelector('.sr-only')).toBeNull();
  });

  it('emits activated on Enter keydown (keyboard-accessible click equivalent)', () => {
    const fixture = createComponent({ id: 'e1', blindCode: 'AB12', notValidForBos: false });
    const activated = jest.fn();
    fixture.componentInstance.activated.subscribe(activated);

    const token = fixture.nativeElement.querySelector('div') as HTMLDivElement;
    token.dispatchEvent(new KeyboardEvent('keydown', { key: 'Enter' }));

    expect(activated).toHaveBeenCalledTimes(1);
  });
});
