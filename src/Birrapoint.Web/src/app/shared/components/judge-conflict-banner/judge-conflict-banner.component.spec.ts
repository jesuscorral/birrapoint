import { TestBed } from '@angular/core/testing';

import type { JudgeConflictBannerItem } from './judge-conflict-banner.component';
import { JudgeConflictBannerComponent } from './judge-conflict-banner.component';

function item(overrides: Partial<JudgeConflictBannerItem> = {}): JudgeConflictBannerItem {
  return { tableId: 't1', judgeDisplayName: 'Ana Pérez', tableName: 'Mesa IPA', ...overrides };
}

describe('JudgeConflictBannerComponent', () => {
  function create(conflicts: JudgeConflictBannerItem[]) {
    const fixture = TestBed.createComponent(JudgeConflictBannerComponent);
    fixture.componentRef.setInput('conflicts', conflicts);
    const dismissed: string[] = [];
    fixture.componentInstance.dismissed.subscribe((id) => dismissed.push(id));
    fixture.detectChanges();
    return { fixture, dismissed, el: fixture.nativeElement as HTMLElement };
  }

  it('shows the judge and the table with an instruction to fix the table', () => {
    const { el } = create([item()]);

    const text = el.textContent ?? '';
    expect(text).toContain('Ana Pérez');
    expect(text).toContain('Mesa IPA');
    expect(text).toContain('Corrige la mesa');
  });

  it('falls back to "una mesa" when the table name is unknown', () => {
    const { el } = create([item({ tableName: null })]);

    expect(el.textContent).toContain('una mesa');
  });

  it('renders each banner with role="alert"', () => {
    const { el } = create([item(), item({ tableId: 't2' })]);

    expect(el.querySelectorAll('[role="alert"]').length).toBe(2);
  });

  it('shows one banner per table, the latest event replacing the earlier one', () => {
    const { el } = create([item(), item({ judgeDisplayName: 'Luis Gómez' })]);

    expect(el.querySelectorAll('[role="alert"]').length).toBe(1);
    expect(el.textContent).toContain('Luis Gómez');
    expect(el.textContent).not.toContain('Ana Pérez');
  });

  it('dismisses with the button', () => {
    const { el, dismissed } = create([item()]);

    el.querySelector<HTMLButtonElement>('button')!.click();

    expect(dismissed).toEqual(['t1']);
  });

  it('dismisses with the Escape key', () => {
    const { el, dismissed } = create([item()]);

    el.querySelector('button')!.dispatchEvent(
      new KeyboardEvent('keydown', { key: 'Escape', bubbles: true }),
    );

    expect(dismissed).toEqual(['t1']);
  });
});
