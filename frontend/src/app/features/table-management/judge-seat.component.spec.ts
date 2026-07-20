import { TestBed } from '@angular/core/testing';

import { JudgeSeatComponent } from './judge-seat.component';
import type { TableJudge } from './table-management-api.service';

describe('JudgeSeatComponent', () => {
  function createComponent(judge: TableJudge) {
    const fixture = TestBed.createComponent(JudgeSeatComponent);
    fixture.componentRef.setInput('judge', judge);
    fixture.detectChanges();
    return fixture;
  }

  it('renders the judge initials and a data-judge-id attribute', () => {
    const fixture = createComponent({
      id: 'j1',
      email: 'ada@example.com',
      displayName: 'Ada Lovelace',
    });

    const seat = fixture.nativeElement.querySelector('li') as HTMLLIElement;
    expect(seat.textContent?.trim()).toBe('AL');
    expect(seat.getAttribute('data-judge-id')).toBe('j1');
    expect(seat.getAttribute('role')).toBe('button');
  });

  it('handles a single-word display name', () => {
    const fixture = createComponent({ id: 'j2', email: 'ada@example.com', displayName: 'Ada' });

    const seat = fixture.nativeElement.querySelector('li') as HTMLLIElement;
    expect(seat.textContent?.trim()).toBe('A');
  });

  it('emits activated on Enter keydown (keyboard-accessible click equivalent)', () => {
    const fixture = createComponent({
      id: 'j1',
      email: 'ada@example.com',
      displayName: 'Ada Lovelace',
    });
    const activated = jest.fn();
    fixture.componentInstance.activated.subscribe(activated);

    const seat = fixture.nativeElement.querySelector('li') as HTMLLIElement;
    seat.dispatchEvent(new KeyboardEvent('keydown', { key: 'Enter' }));

    expect(activated).toHaveBeenCalledTimes(1);
  });
});
