import { TestBed } from '@angular/core/testing';

import { StylePickerComponent } from './style-picker.component';
import type { StyleSummary } from './entry-import-api.service';

describe('StylePickerComponent', () => {
  const styles: StyleSummary[] = [
    {
      code: '4A',
      name: 'Munich Helles',
      categoryNumber: '4',
      categoryName: 'Pale Malty European Lager',
    },
    { code: '21A', name: 'American IPA', categoryNumber: '21', categoryName: 'IPA' },
    { code: '21B', name: 'Specialty IPA', categoryNumber: '21', categoryName: 'IPA' },
  ];

  function createComponent() {
    const fixture = TestBed.createComponent(StylePickerComponent);
    fixture.componentRef.setInput('styles', styles);
    fixture.detectChanges();
    return fixture;
  }

  it('lists every style as an option when the filter is empty', () => {
    const fixture = createComponent();
    const options = fixture.nativeElement.querySelectorAll('select option[value]:not([value=""])');
    expect(options.length).toBe(3);
  });

  it('narrows options to those matching the filter text by code or name', () => {
    const fixture = createComponent();
    const filterInput = fixture.nativeElement.querySelector(
      'input[type="text"]',
    ) as HTMLInputElement;

    filterInput.value = 'ipa';
    filterInput.dispatchEvent(new Event('input'));
    fixture.detectChanges();

    const options: NodeListOf<HTMLOptionElement> = fixture.nativeElement.querySelectorAll(
      'select option[value]:not([value=""])',
    );
    expect(options.length).toBe(2);
    expect([...options].map((o) => o.value)).toEqual(['21A', '21B']);
  });

  it('disables the assign button until a style is selected', () => {
    const fixture = createComponent();
    const button = fixture.nativeElement.querySelector('button') as HTMLButtonElement;
    expect(button.disabled).toBe(true);

    const select = fixture.nativeElement.querySelector('select') as HTMLSelectElement;
    select.value = '4A';
    select.dispatchEvent(new Event('change'));
    fixture.detectChanges();

    expect(button.disabled).toBe(false);
  });

  it('emits assign with the selected style code on click', () => {
    const fixture = createComponent();
    const emitted: string[] = [];
    fixture.componentInstance.assign.subscribe((code) => emitted.push(code));

    const select = fixture.nativeElement.querySelector('select') as HTMLSelectElement;
    select.value = '21B';
    select.dispatchEvent(new Event('change'));
    fixture.detectChanges();

    const button = fixture.nativeElement.querySelector('button') as HTMLButtonElement;
    button.click();

    expect(emitted).toEqual(['21B']);
  });
});
