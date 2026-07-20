import { TestBed } from '@angular/core/testing';

import { TableDetailModalComponent } from './table-detail-modal.component';
import type {
  BeerDetailContent,
  JudgeDetailContent,
  TableOption,
} from './table-detail-modal.component';

const tables: TableOption[] = [
  { id: 't1', name: 'Mesa 1' },
  { id: 't2', name: 'Mesa 2' },
];

describe('TableDetailModalComponent', () => {
  function createComponent(
    content: BeerDetailContent | JudgeDetailContent,
    assignedTableIds: string[],
  ) {
    const fixture = TestBed.createComponent(TableDetailModalComponent);
    fixture.componentRef.setInput('content', content);
    fixture.componentRef.setInput('assignedTableIds', assignedTableIds);
    fixture.componentRef.setInput('tables', tables);
    fixture.detectChanges();
    return fixture;
  }

  it('shows beer detail content: blind code, style, ABV range, assigned table', () => {
    const beer: BeerDetailContent = {
      kind: 'beer',
      id: 'e1',
      blindCode: 'AB12',
      styleName: 'Munich Helles',
      abvLow: 5.5,
      abvHigh: 7.5,
    };
    const fixture = createComponent(beer, ['t1']);

    const text = fixture.nativeElement.textContent as string;
    expect(text).toContain('AB12');
    expect(text).toContain('Munich Helles');
    expect(text).toContain('5.5–7.5%');
    expect(text).toContain('Mesa 1');
  });

  it('omits the ABV row gracefully when both abvLow and abvHigh are null', () => {
    const beer: BeerDetailContent = {
      kind: 'beer',
      id: 'e1',
      blindCode: 'AB12',
      styleName: 'Local style',
      abvLow: null,
      abvHigh: null,
    };
    const fixture = createComponent(beer, []);

    const text = fixture.nativeElement.textContent as string;
    expect(text).not.toContain('ABV');
    expect(text).toContain('Unassigned');
  });

  it('shows judge detail content: name, email, assigned table', () => {
    const judge: JudgeDetailContent = {
      kind: 'judge',
      id: 'j1',
      displayName: 'Ada Lovelace',
      email: 'ada@example.com',
    };
    const fixture = createComponent(judge, ['t2']);

    const text = fixture.nativeElement.textContent as string;
    expect(text).toContain('Ada Lovelace');
    expect(text).toContain('ada@example.com');
    expect(text).toContain('Mesa 2');
    // no allergen/award/certification fields per the scope decision — nothing to assert missing
    // by name since none of those fields exist on the input type at all.
  });

  it('does not hardcode a single assigned table — joins every table the entity belongs to', () => {
    const judge: JudgeDetailContent = {
      kind: 'judge',
      id: 'j1',
      displayName: 'Ada Lovelace',
      email: 'ada@example.com',
    };
    const fixture = createComponent(judge, ['t1', 't2']);

    const text = fixture.nativeElement.textContent as string;
    expect(text).toContain('Mesa 1, Mesa 2');
  });

  it('emits closed when the Close button is clicked', () => {
    const judge: JudgeDetailContent = {
      kind: 'judge',
      id: 'j1',
      displayName: 'Ada',
      email: 'a@example.com',
    };
    const fixture = createComponent(judge, []);
    const closed = jest.fn();
    fixture.componentInstance.closed.subscribe(closed);

    const buttons = [...fixture.nativeElement.querySelectorAll('button')] as HTMLButtonElement[];
    buttons.find((b) => b.textContent?.trim() === 'Close')!.click();

    expect(closed).toHaveBeenCalledTimes(1);
  });

  it('emits closed on Escape (keyboard-accessible close)', () => {
    const judge: JudgeDetailContent = {
      kind: 'judge',
      id: 'j1',
      displayName: 'Ada',
      email: 'a@example.com',
    };
    const fixture = createComponent(judge, []);
    const closed = jest.fn();
    fixture.componentInstance.closed.subscribe(closed);

    const panel = fixture.nativeElement.querySelector('.modal-panel') as HTMLElement;
    panel.dispatchEvent(new KeyboardEvent('keydown', { key: 'Escape', bubbles: true }));

    expect(closed).toHaveBeenCalledTimes(1);
  });

  it('emits move with the selected table id (keyboard-accessible drag-drop equivalent)', () => {
    const judge: JudgeDetailContent = {
      kind: 'judge',
      id: 'j1',
      displayName: 'Ada',
      email: 'a@example.com',
    };
    const fixture = createComponent(judge, ['t1']);
    const move = jest.fn();
    fixture.componentInstance.move.subscribe(move);

    const select = fixture.nativeElement.querySelector('select') as HTMLSelectElement;
    select.value = 't2';
    select.dispatchEvent(new Event('change'));
    fixture.detectChanges();

    const buttons = [...fixture.nativeElement.querySelectorAll('button')] as HTMLButtonElement[];
    buttons.find((b) => b.textContent?.trim() === 'Move')!.click();

    expect(move).toHaveBeenCalledWith('t2');
  });

  it('emits move with null when moving back to Unassigned', () => {
    const judge: JudgeDetailContent = {
      kind: 'judge',
      id: 'j1',
      displayName: 'Ada',
      email: 'a@example.com',
    };
    const fixture = createComponent(judge, ['t1']);
    const move = jest.fn();
    fixture.componentInstance.move.subscribe(move);

    const select = fixture.nativeElement.querySelector('select') as HTMLSelectElement;
    select.value = '';
    select.dispatchEvent(new Event('change'));
    fixture.detectChanges();

    const buttons = [...fixture.nativeElement.querySelectorAll('button')] as HTMLButtonElement[];
    buttons.find((b) => b.textContent?.trim() === 'Move')!.click();

    expect(move).toHaveBeenCalledWith(null);
  });
});
