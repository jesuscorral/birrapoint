import { ComponentFixture, TestBed } from '@angular/core/testing';

import { BpDiscreteSliderComponent } from './bp-discrete-slider.component';

describe('BpDiscreteSliderComponent', () => {
  let fixture: ComponentFixture<BpDiscreteSliderComponent>;

  beforeEach(() => {
    fixture = TestBed.createComponent(BpDiscreteSliderComponent);
    fixture.componentRef.setInput('id', 'aroma-malt');
    fixture.componentRef.setInput('label', 'Malta');
  });

  it('renders a range input with min 0 and max 3', () => {
    fixture.detectChanges();

    const input = fixture.nativeElement.querySelector('input[type="range"]') as HTMLInputElement;
    expect(input.min).toBe('0');
    expect(input.max).toBe('3');
  });

  // Same fix as bp-input.component.ts: a static id="x" on the host would otherwise duplicate the
  // id the inner <input> needs to itself be uniquely addressable.
  it('nulls the id on its own host element so only the inner input keeps it', () => {
    fixture.detectChanges();

    expect(fixture.nativeElement.getAttribute('id')).toBeNull();
    expect(fixture.nativeElement.querySelector('#aroma-malt')).not.toBeNull();
  });

  it('shows the four intensity tick labels', () => {
    fixture.detectChanges();

    const text = fixture.nativeElement.textContent as string;
    expect(text).toContain('Nada');
    expect(text).toContain('Bajo');
    expect(text).toContain('Medio');
    expect(text).toContain('Alto');
  });

  it('defaults to 0 and reflects the value input', () => {
    fixture.componentRef.setInput('value', 2);
    fixture.detectChanges();

    const input = fixture.nativeElement.querySelector('input[type="range"]') as HTMLInputElement;
    expect(input.value).toBe('2');
  });

  it('announces the current intensity label via aria-valuetext, not the raw number', () => {
    fixture.componentRef.setInput('value', 3);
    fixture.detectChanges();

    const input = fixture.nativeElement.querySelector('input[type="range"]') as HTMLInputElement;
    expect(input.getAttribute('aria-valuetext')).toBe('Alto');
  });

  it('emits valueChange as a number on input', () => {
    fixture.detectChanges();
    const emitted: number[] = [];
    fixture.componentInstance.valueChange.subscribe((value) => emitted.push(value));

    const input = fixture.nativeElement.querySelector('input[type="range"]') as HTMLInputElement;
    input.value = '1';
    input.dispatchEvent(new Event('input'));

    expect(emitted).toEqual([1]);
  });

  // Organizer follow-up (Session 2026-09-21): every descriptor needs its own "Inapropiado" flag,
  // built into the slider itself rather than a separate repeated row per attribute.
  it('shows an Inapropiado checkbox reflecting the inappropriate input', () => {
    fixture.componentRef.setInput('inappropriate', true);
    fixture.detectChanges();

    const checkbox = fixture.nativeElement.querySelector(
      'input[type="checkbox"]',
    ) as HTMLInputElement;
    expect(checkbox.checked).toBe(true);
    expect(fixture.nativeElement.textContent).toContain('Inapropiado');
  });

  it('emits inappropriateChange as a boolean on toggle', () => {
    fixture.detectChanges();
    const emitted: boolean[] = [];
    fixture.componentInstance.inappropriateChange.subscribe((value) => emitted.push(value));

    const checkbox = fixture.nativeElement.querySelector(
      'input[type="checkbox"]',
    ) as HTMLInputElement;
    checkbox.checked = true;
    checkbox.dispatchEvent(new Event('change'));

    expect(emitted).toEqual([true]);
  });
});
