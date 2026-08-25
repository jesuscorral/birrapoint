import { TestBed } from '@angular/core/testing';

import { JudgeSeatComponent } from './judge-seat.component';
import type { TableJudge } from './table-management-api.service';

describe('JudgeSeatComponent', () => {
  function createComponent(judge: TableJudge, showName?: boolean) {
    const fixture = TestBed.createComponent(JudgeSeatComponent);
    fixture.componentRef.setInput('judge', judge);
    if (showName !== undefined) {
      fixture.componentRef.setInput('showName', showName);
    }
    fixture.detectChanges();
    return fixture;
  }

  const ada: TableJudge = { id: 'j1', email: 'ada@example.com', displayName: 'Ada Lovelace' };

  it('renders the judge initials and a data-judge-id attribute', () => {
    const fixture = createComponent(ada);

    const seat = fixture.nativeElement.querySelector('.judge-seat') as HTMLDivElement;
    expect(seat.querySelector('.judge-seat__avatar')?.textContent?.trim()).toBe('AL');
    expect(seat.getAttribute('data-judge-id')).toBe('j1');
    expect(seat.getAttribute('role')).toBe('button');
  });

  // T124 — initials alone could not tell two judges apart while seating them at tables.
  it('shows the full display name under the avatar by default', () => {
    const fixture = createComponent(ada);

    const seat = fixture.nativeElement.querySelector('.judge-seat') as HTMLDivElement;
    expect(seat.classList.contains('judge-seat--named')).toBe(true);
    const name = seat.querySelector('.judge-seat__name') as HTMLElement;
    expect(name.textContent?.trim()).toBe('Ada Lovelace');
    // The accessible name already carries it; announcing it twice would duplicate every seat.
    expect(name.getAttribute('aria-hidden')).toBe('true');
    expect(seat.getAttribute('aria-label')).toBe('Judge Ada Lovelace — view details');
  });

  it('falls back to the bare avatar when showName is off', () => {
    const fixture = createComponent(ada, false);

    const seat = fixture.nativeElement.querySelector('.judge-seat') as HTMLDivElement;
    expect(seat.classList.contains('judge-seat--named')).toBe(false);
    expect(seat.querySelector('.judge-seat__name')).toBeNull();
    expect(seat.textContent?.trim()).toBe('AL');
  });

  // T125b: inside a rail card the stacked form is what made the cards tall — dense lays avatar
  // and name in a row, and the name still shows.
  it('keeps the name visible in the dense form used inside rail cards', () => {
    const fixture = createComponent(ada);
    fixture.componentRef.setInput('dense', true);
    fixture.detectChanges();

    const seat = fixture.nativeElement.querySelector('.judge-seat') as HTMLDivElement;
    expect(seat.classList.contains('judge-seat--dense')).toBe(true);
    expect(seat.querySelector('.judge-seat__name')?.textContent?.trim()).toBe('Ada Lovelace');
  });

  it('handles a single-word display name', () => {
    const fixture = createComponent({ id: 'j2', email: 'ada@example.com', displayName: 'Ada' });

    const seat = fixture.nativeElement.querySelector('.judge-seat') as HTMLDivElement;
    expect(seat.querySelector('.judge-seat__avatar')?.textContent?.trim()).toBe('A');
  });

  it('emits activated on Enter keydown (keyboard-accessible click equivalent)', () => {
    const fixture = createComponent(ada);
    const activated = jest.fn();
    fixture.componentInstance.activated.subscribe(activated);

    const seat = fixture.nativeElement.querySelector('.judge-seat') as HTMLDivElement;
    seat.dispatchEvent(new KeyboardEvent('keydown', { key: 'Enter' }));

    expect(activated).toHaveBeenCalledTimes(1);
  });
});
