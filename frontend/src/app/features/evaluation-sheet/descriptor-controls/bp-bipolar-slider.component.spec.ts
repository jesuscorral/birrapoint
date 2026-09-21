import { ComponentFixture, TestBed } from '@angular/core/testing';

import { BpBipolarSliderComponent } from './bp-bipolar-slider.component';

describe('BpBipolarSliderComponent', () => {
  let fixture: ComponentFixture<BpBipolarSliderComponent>;

  beforeEach(() => {
    fixture = TestBed.createComponent(BpBipolarSliderComponent);
    fixture.componentRef.setInput('id', 'flavor-balance');
    fixture.componentRef.setInput('label', 'Equilibrio');
    fixture.componentRef.setInput('startLabel', 'Lupulado');
    fixture.componentRef.setInput('endLabel', 'Maltoso');
  });

  it('renders a range input with min 0 and max 100', () => {
    fixture.detectChanges();

    const input = fixture.nativeElement.querySelector('input[type="range"]') as HTMLInputElement;
    expect(input.min).toBe('0');
    expect(input.max).toBe('100');
  });

  // Same fix as bp-input.component.ts: a static id="x" on the host would otherwise duplicate the
  // id the inner <input> needs to itself be uniquely addressable.
  it('nulls the id on its own host element so only the inner input keeps it', () => {
    fixture.detectChanges();

    expect(fixture.nativeElement.getAttribute('id')).toBeNull();
    expect(fixture.nativeElement.querySelector('#flavor-balance')).not.toBeNull();
  });

  it('shows both pole labels', () => {
    fixture.detectChanges();

    const text = fixture.nativeElement.textContent as string;
    expect(text).toContain('Lupulado');
    expect(text).toContain('Maltoso');
  });

  it('defaults to 50 and reflects the value input', () => {
    fixture.detectChanges();
    const defaultInput = fixture.nativeElement.querySelector(
      'input[type="range"]',
    ) as HTMLInputElement;
    expect(defaultInput.value).toBe('50');

    fixture.componentRef.setInput('value', 80);
    fixture.detectChanges();
    expect(defaultInput.value).toBe('80');
  });

  // senior-review M3: the discrete slider already announced its intensity label via
  // aria-valuetext; the bipolar one didn't, so a screen reader announced a bare "50" on an axis
  // like Lupulado↔Maltoso with no indication of direction.
  it('announces the value with both pole labels via aria-valuetext', () => {
    fixture.componentRef.setInput('value', 30);
    fixture.detectChanges();

    const input = fixture.nativeElement.querySelector('input[type="range"]') as HTMLInputElement;
    const valueText = input.getAttribute('aria-valuetext');
    expect(valueText).toContain('30');
    expect(valueText).toContain('Lupulado');
    expect(valueText).toContain('Maltoso');
  });

  it('emits valueChange as a number on input', () => {
    fixture.detectChanges();
    const emitted: number[] = [];
    fixture.componentInstance.valueChange.subscribe((value) => emitted.push(value));

    const input = fixture.nativeElement.querySelector('input[type="range"]') as HTMLInputElement;
    input.value = '30';
    input.dispatchEvent(new Event('input'));

    expect(emitted).toEqual([30]);
  });
});
