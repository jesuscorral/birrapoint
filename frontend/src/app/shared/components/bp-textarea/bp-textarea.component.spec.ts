import { ComponentFixture, TestBed } from '@angular/core/testing';
import { BpTextareaComponent } from './bp-textarea.component';

describe('BpTextareaComponent', () => {
  let component: BpTextareaComponent;
  let fixture: ComponentFixture<BpTextareaComponent>;

  beforeEach(async () => {
    await TestBed.configureTestingModule({
      imports: [BpTextareaComponent],
    }).compileComponents();

    fixture = TestBed.createComponent(BpTextareaComponent);
    component = fixture.componentInstance;
  });

  it('should create', () => {
    expect(component).toBeTruthy();
  });

  it('should render a textarea element', () => {
    fixture.detectChanges();
    expect(fixture.nativeElement.querySelector('textarea')).toBeTruthy();
  });

  it('should render label with required asterisk', () => {
    fixture.componentRef.setInput('label', 'Descripción');
    fixture.componentRef.setInput('required', true);
    fixture.detectChanges();

    const label = fixture.nativeElement.querySelector('label');
    expect(label.textContent).toContain('Descripción');
    expect(fixture.nativeElement.querySelector('.req')).toBeTruthy();
  });

  it('should apply the configured number of rows', () => {
    fixture.componentRef.setInput('rows', 6);
    fixture.detectChanges();

    const textarea = fixture.nativeElement.querySelector('textarea');
    expect(textarea.rows).toBe(6);
  });

  it('should set aria-invalid and show the error message when hasError is true', () => {
    fixture.componentRef.setInput('hasError', true);
    fixture.componentRef.setInput('errorMessage', 'Campo requerido');
    fixture.detectChanges();

    const textarea = fixture.nativeElement.querySelector('textarea');
    expect(textarea.getAttribute('aria-invalid')).toBe('true');
    expect(fixture.nativeElement.querySelector('.field__error').textContent).toContain(
      'Campo requerido',
    );
  });

  it('should hide the hint when there is an error', () => {
    fixture.componentRef.setInput('hint', 'Cuéntanos algo');
    fixture.componentRef.setInput('hasError', true);
    fixture.componentRef.setInput('errorMessage', 'Error');
    fixture.detectChanges();

    expect(fixture.nativeElement.querySelector('.field__hint')).toBeFalsy();
  });

  it('should disable the textarea when disabled=true', () => {
    component.disabled = true;
    fixture.detectChanges();

    const textarea = fixture.nativeElement.querySelector('textarea');
    expect(textarea.disabled).toBe(true);
  });

  describe('ControlValueAccessor', () => {
    it('should update value and emit on input', () => {
      jest.spyOn(component.valueChange, 'emit');
      fixture.detectChanges();

      const textarea = fixture.nativeElement.querySelector('textarea');
      textarea.value = 'Hola mundo';
      textarea.dispatchEvent(new Event('input'));

      expect(component.value).toBe('Hola mundo');
      expect(component.valueChange.emit).toHaveBeenCalledWith('Hola mundo');
    });

    it('should write value via writeValue', () => {
      component.writeValue('valor inicial');
      expect(component.value).toBe('valor inicial');

      component.writeValue(null);
      expect(component.value).toBe('');
    });

    it('should register onChange and onTouched callbacks', () => {
      const onChangeFn = jest.fn();
      const onTouchedFn = jest.fn();
      component.registerOnChange(onChangeFn);
      component.registerOnTouched(onTouchedFn);
      fixture.detectChanges();

      const textarea = fixture.nativeElement.querySelector('textarea');
      textarea.value = 'test';
      textarea.dispatchEvent(new Event('input'));
      textarea.dispatchEvent(new Event('blur'));

      expect(onChangeFn).toHaveBeenCalledWith('test');
      expect(onTouchedFn).toHaveBeenCalled();
    });

    it('should set disabled state via setDisabledState', () => {
      component.setDisabledState(true);
      expect(component.disabled).toBe(true);
    });
  });
});
