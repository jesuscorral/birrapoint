import { ComponentFixture, TestBed } from '@angular/core/testing';
import { ReactiveFormsModule } from '@angular/forms';
import { BpInputComponent } from './bp-input.component';

describe('BpInputComponent', () => {
  let component: BpInputComponent;
  let fixture: ComponentFixture<BpInputComponent>;

  beforeEach(async () => {
    await TestBed.configureTestingModule({
      imports: [BpInputComponent, ReactiveFormsModule],
    }).compileComponents();

    fixture = TestBed.createComponent(BpInputComponent);
    component = fixture.componentInstance;
  });

  it('should create', () => {
    expect(component).toBeTruthy();
  });

  describe('Rendering', () => {
    it('should render input element', () => {
      fixture.detectChanges();

      const input = fixture.nativeElement.querySelector('input');
      expect(input).toBeTruthy();
    });

    it('should render label when provided', () => {
      fixture.componentRef.setInput('label', 'Email');
      fixture.detectChanges();

      const label = fixture.nativeElement.querySelector('label');
      expect(label.textContent).toContain('Email');
    });

    it('should show asterisk for required fields', () => {
      fixture.componentRef.setInput('label', 'Email');
      fixture.componentRef.setInput('required', true);
      fixture.detectChanges();

      const req = fixture.nativeElement.querySelector('.req');
      expect(req).toBeTruthy();
    });

    it('should render hint text when provided', () => {
      fixture.componentRef.setInput('hint', 'Your email for login');
      fixture.componentRef.setInput('hasError', false);
      fixture.detectChanges();

      const hint = fixture.nativeElement.querySelector('.field__hint');
      expect(hint.textContent).toContain('Your email for login');
    });

    it('should hide hint when there is an error', () => {
      fixture.componentRef.setInput('hint', 'Your email');
      fixture.componentRef.setInput('hasError', true);
      fixture.componentRef.setInput('errorMessage', 'Invalid email');
      fixture.detectChanges();

      const hint = fixture.nativeElement.querySelector('.field__hint');
      expect(hint).toBeFalsy();
    });
  });

  describe('Input types', () => {
    it('should render text input by default', () => {
      fixture.detectChanges();

      const input = fixture.nativeElement.querySelector('input');
      expect(input.type).toBe('text');
    });

    it('should render email input', () => {
      fixture.componentRef.setInput('type', 'email');
      fixture.detectChanges();

      const input = fixture.nativeElement.querySelector('input');
      expect(input.type).toBe('email');
    });

    it('should render password input', () => {
      fixture.componentRef.setInput('type', 'password');
      fixture.detectChanges();

      const input = fixture.nativeElement.querySelector('input');
      expect(input.type).toBe('password');
    });

    it('should render tel input', () => {
      fixture.componentRef.setInput('type', 'tel');
      fixture.detectChanges();

      const input = fixture.nativeElement.querySelector('input');
      expect(input.type).toBe('tel');
    });
  });

  describe('Password toggle', () => {
    it('should show toggle button for password type', () => {
      fixture.componentRef.setInput('type', 'password');
      fixture.detectChanges();

      const button = fixture.nativeElement.querySelector('.field__action');
      expect(button).toBeTruthy();
    });

    it('should not show toggle button for other types', () => {
      fixture.componentRef.setInput('type', 'email');
      fixture.detectChanges();

      const button = fixture.nativeElement.querySelector('.field__action');
      expect(button).toBeFalsy();
    });

    it('should toggle password visibility', () => {
      fixture.componentRef.setInput('type', 'password');
      fixture.detectChanges();

      const input = fixture.nativeElement.querySelector('input');
      const button = fixture.nativeElement.querySelector('.field__action');

      expect(input.type).toBe('password');

      button.click();
      fixture.detectChanges();
      expect(input.type).toBe('text');

      button.click();
      fixture.detectChanges();
      expect(input.type).toBe('password');
    });
  });

  describe('Validation', () => {
    it('should set aria-invalid when hasError=true', () => {
      fixture.componentRef.setInput('hasError', true);
      fixture.detectChanges();

      const input = fixture.nativeElement.querySelector('input');
      expect(input.getAttribute('aria-invalid')).toBe('true');
    });

    it('should apply error styling when hasError=true', () => {
      fixture.componentRef.setInput('hasError', true);
      fixture.detectChanges();

      const input = fixture.nativeElement.querySelector('input');
      expect(input.classList.toString()).toContain('border-bp-danger-600');
    });

    it('should show error message when provided', () => {
      fixture.componentRef.setInput('hasError', true);
      fixture.componentRef.setInput('errorMessage', 'This field is required');
      fixture.detectChanges();

      const error = fixture.nativeElement.querySelector('.field__error');
      expect(error.textContent).toContain('This field is required');
    });

    it('should set aria-describedby for hint', () => {
      fixture.componentRef.setInput('id', 'email-field');
      fixture.componentRef.setInput('hint', 'Your email');
      fixture.componentRef.setInput('hasError', false);
      fixture.detectChanges();

      const input = fixture.nativeElement.querySelector('input');
      expect(input.getAttribute('aria-describedby')).toContain('email-field-hint');
    });

    it('should set aria-describedby for error', () => {
      fixture.componentRef.setInput('id', 'email-field');
      fixture.componentRef.setInput('hasError', true);
      fixture.componentRef.setInput('errorMessage', 'Invalid');
      fixture.detectChanges();

      const input = fixture.nativeElement.querySelector('input');
      expect(input.getAttribute('aria-describedby')).toContain('email-field-error');
    });
  });

  describe('Disabled state', () => {
    it('should disable input when disabled=true', () => {
      component.disabled = true;
      fixture.detectChanges();

      const input = fixture.nativeElement.querySelector('input');
      expect(input.disabled).toBe(true);
    });

    it('should apply disabled styling', () => {
      component.disabled = true;
      fixture.detectChanges();

      const input = fixture.nativeElement.querySelector('input');
      expect(input.classList.toString()).toContain('disabled');
    });
  });

  describe('ControlValueAccessor', () => {
    it('should update value when input changes', () => {
      component.value = '';
      fixture.detectChanges();

      const input = fixture.nativeElement.querySelector('input');
      input.value = 'test@example.com';
      input.dispatchEvent(new Event('input'));

      expect(component.value).toBe('test@example.com');
    });

    it('should emit valueChange event', () => {
      jest.spyOn(component.valueChange, 'emit');
      fixture.detectChanges();

      const input = fixture.nativeElement.querySelector('input');
      input.value = 'test@example.com';
      input.dispatchEvent(new Event('input'));

      expect(component.valueChange.emit).toHaveBeenCalledWith('test@example.com');
    });

    it('should work with reactive forms', () => {
      fixture.componentRef.setInput('id', 'email');

      fixture.detectChanges();
      component.writeValue('new value');
      fixture.detectChanges();

      const input = fixture.nativeElement.querySelector('input');
      expect(input.value).toBe('new value');
    });

    it('should register onChange callback', () => {
      const onChangeFn = jest.fn();
      component.registerOnChange(onChangeFn);
      fixture.detectChanges();

      const input = fixture.nativeElement.querySelector('input');
      input.value = 'test';
      input.dispatchEvent(new Event('input'));

      expect(onChangeFn).toHaveBeenCalledWith('test');
    });

    it('should register onTouched callback', () => {
      const onTouchedFn = jest.fn();
      component.registerOnTouched(onTouchedFn);
      fixture.detectChanges();

      const input = fixture.nativeElement.querySelector('input');
      input.dispatchEvent(new Event('blur'));

      expect(onTouchedFn).toHaveBeenCalled();
    });

    it('should set disabled state via setDisabledState', () => {
      component.setDisabledState(true);
      expect(component.disabled).toBe(true);

      component.setDisabledState(false);
      expect(component.disabled).toBe(false);
    });
  });

  describe('Placeholder', () => {
    it('should set placeholder text', () => {
      fixture.componentRef.setInput('placeholder', 'Enter email');
      fixture.detectChanges();

      const input = fixture.nativeElement.querySelector('input');
      expect(input.placeholder).toBe('Enter email');
    });
  });

  describe('Accessibility', () => {
    it('should have proper label association', () => {
      fixture.componentRef.setInput('id', 'custom-id');
      fixture.componentRef.setInput('label', 'Custom Field');
      fixture.detectChanges();

      const label = fixture.nativeElement.querySelector('label');
      expect(label.getAttribute('for')).toBe('custom-id');
    });

    it('should be focusable', () => {
      fixture.detectChanges();

      const input = fixture.nativeElement.querySelector('input');
      expect(input.tabIndex).toBeGreaterThanOrEqual(-1);
    });
  });
});
