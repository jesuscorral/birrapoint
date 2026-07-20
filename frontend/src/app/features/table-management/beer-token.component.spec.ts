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

    const token = fixture.nativeElement.querySelector('li') as HTMLLIElement;
    expect(token.textContent?.trim()).toBe('AB12');
    expect(token.getAttribute('data-entry-id')).toBe('e1');
  });

  it('marks a BOS-flagged entry with the flagged class', () => {
    const fixture = createComponent({ id: 'e1', blindCode: 'AB12', notValidForBos: true });

    const token = fixture.nativeElement.querySelector('li') as HTMLLIElement;
    expect(token.classList.contains('beer-token--bos-flagged')).toBe(true);
  });

  it('emits activated on Enter keydown (keyboard-accessible click equivalent)', () => {
    const fixture = createComponent({ id: 'e1', blindCode: 'AB12', notValidForBos: false });
    const activated = jest.fn();
    fixture.componentInstance.activated.subscribe(activated);

    const token = fixture.nativeElement.querySelector('li') as HTMLLIElement;
    token.dispatchEvent(new KeyboardEvent('keydown', { key: 'Enter' }));

    expect(activated).toHaveBeenCalledTimes(1);
  });
});
