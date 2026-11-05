import { Component } from '@angular/core';
import { TestBed } from '@angular/core/testing';

import { BpStepActionsComponent } from './bp-step-actions.component';

@Component({
  standalone: true,
  imports: [BpStepActionsComponent],
  template: `
    <bp-step-actions
      [showBack]="showBack"
      [backLabel]="backLabel"
      [showNext]="showNext"
      [nextLabel]="nextLabel"
      [nextDisabled]="nextDisabled"
      (back)="backCount = backCount + 1"
      (next)="nextCount = nextCount + 1"
    >
      <button type="button" class="extra-action">Consolidar</button>
    </bp-step-actions>
  `,
})
class HostComponent {
  showBack = true;
  backLabel = 'Atrás';
  showNext = true;
  nextLabel = 'Siguiente';
  nextDisabled = false;
  backCount = 0;
  nextCount = 0;
}

describe('BpStepActionsComponent', () => {
  function createHost() {
    const fixture = TestBed.createComponent(HostComponent);
    fixture.detectChanges();
    return fixture;
  }

  function buttonWithText(root: Element, text: string): HTMLButtonElement {
    const match = [...root.querySelectorAll('button')].find(
      (button) => button.textContent?.trim() === text,
    );
    if (!match) {
      throw new Error(`No button with text "${text}" found`);
    }
    return match as HTMLButtonElement;
  }

  it('renders Atrás on the left, projected actions in the centre and Siguiente on the right', () => {
    const fixture = createHost();

    const start = fixture.nativeElement.querySelector('.step-actions__zone--start') as HTMLElement;
    const center = fixture.nativeElement.querySelector(
      '.step-actions__zone--center',
    ) as HTMLElement;
    const end = fixture.nativeElement.querySelector('.step-actions__zone--end') as HTMLElement;

    expect(start.textContent?.trim()).toBe('Atrás');
    expect(center.querySelector('.extra-action')?.textContent?.trim()).toBe('Consolidar');
    expect(end.textContent?.trim()).toBe('Siguiente');
  });

  it('emits back and next when their buttons are clicked', () => {
    const fixture = createHost();

    buttonWithText(fixture.nativeElement, 'Atrás').click();
    buttonWithText(fixture.nativeElement, 'Siguiente').click();

    expect(fixture.componentInstance.backCount).toBe(1);
    expect(fixture.componentInstance.nextCount).toBe(1);
  });

  it('hides each side independently (step 1 has no back, the last step no next)', () => {
    const fixture = createHost();
    fixture.componentInstance.showBack = false;
    fixture.detectChanges();

    const start = fixture.nativeElement.querySelector('.step-actions__zone--start') as HTMLElement;
    expect(start.textContent?.trim()).toBe('');
    // The zone itself stays in the grid so the centre and end zones do not shift position
    // between steps — the whole point of a shared bar.
    expect(start).not.toBeNull();

    fixture.componentInstance.showNext = false;
    fixture.detectChanges();
    const end = fixture.nativeElement.querySelector('.step-actions__zone--end') as HTMLElement;
    expect(end.textContent?.trim()).toBe('');
  });

  it('takes custom labels, so a terminal step can say Finalizar', () => {
    const fixture = createHost();
    fixture.componentInstance.nextLabel = 'Finalizar';
    fixture.detectChanges();

    expect(buttonWithText(fixture.nativeElement, 'Finalizar')).not.toBeNull();
  });

  it('disables the forward action without hiding it', () => {
    const fixture = createHost();
    fixture.componentInstance.nextDisabled = true;
    fixture.detectChanges();

    expect(buttonWithText(fixture.nativeElement, 'Siguiente').disabled).toBe(true);
    expect(fixture.componentInstance.nextCount).toBe(0);
  });
});
