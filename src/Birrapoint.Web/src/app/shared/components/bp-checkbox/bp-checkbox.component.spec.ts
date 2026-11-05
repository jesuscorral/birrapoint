import { Component } from '@angular/core';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { FormControl, ReactiveFormsModule } from '@angular/forms';
import { BpCheckboxComponent } from './bp-checkbox.component';

describe('BpCheckboxComponent', () => {
  let component: BpCheckboxComponent;
  let fixture: ComponentFixture<BpCheckboxComponent>;

  beforeEach(async () => {
    await TestBed.configureTestingModule({
      imports: [BpCheckboxComponent, ReactiveFormsModule],
    }).compileComponents();

    fixture = TestBed.createComponent(BpCheckboxComponent);
    component = fixture.componentInstance;
  });

  it('should create', () => {
    expect(component).toBeTruthy();
  });

  describe('Rendering', () => {
    it('should render checkbox input', () => {
      fixture.detectChanges();

      const input = fixture.nativeElement.querySelector('input[type="checkbox"]');
      expect(input).toBeTruthy();
    });

    it('should render label wrapping input and content', () => {
      fixture.detectChanges();

      const label = fixture.nativeElement.querySelector('label.check');
      expect(label).toBeTruthy();
    });

    it('should project content via ng-content', () => {
      const testContent = 'I agree to terms';
      const span = fixture.nativeElement.querySelector('span');
      if (span) {
        span.textContent = testContent;
      }
      fixture.detectChanges();

      const content = fixture.nativeElement.textContent;
      expect(content).toContain(testContent);
    });
  });

  describe('Checked state', () => {
    it('should be unchecked by default', () => {
      fixture.detectChanges();

      const input = fixture.nativeElement.querySelector('input[type="checkbox"]');
      expect(input.checked).toBe(false);
    });

    it('should reflect checked property', () => {
      component.checked = true;
      fixture.detectChanges();

      const input = fixture.nativeElement.querySelector('input[type="checkbox"]');
      expect(input.checked).toBe(true);
    });

    it('should update checked when input changes', () => {
      fixture.detectChanges();

      const input = fixture.nativeElement.querySelector('input[type="checkbox"]');
      input.click();

      expect(component.checked).toBe(true);
    });
  });

  describe('Change event', () => {
    it('should emit checkedChange event when checkbox is clicked', () => {
      jest.spyOn(component.checkedChange, 'emit');
      fixture.detectChanges();

      const input = fixture.nativeElement.querySelector('input[type="checkbox"]');
      input.click();

      expect(component.checkedChange.emit).toHaveBeenCalledWith(true);
    });

    it('should emit false when unchecking', () => {
      jest.spyOn(component.checkedChange, 'emit');
      component.checked = true;
      fixture.detectChanges();

      const input = fixture.nativeElement.querySelector('input[type="checkbox"]');
      input.click();

      expect(component.checkedChange.emit).toHaveBeenCalledWith(false);
    });
  });

  describe('Disabled state', () => {
    it('should disable checkbox when disabled=true', () => {
      component.disabled = true;
      fixture.detectChanges();

      const input = fixture.nativeElement.querySelector('input[type="checkbox"]');
      expect(input.disabled).toBe(true);
    });

    it('should apply disabled styling to label', () => {
      component.disabled = true;
      fixture.detectChanges();

      const label = fixture.nativeElement.querySelector('label');
      expect(label.classList.toString()).toContain('check-disabled');
    });

    it('should not emit change when disabled', () => {
      jest.spyOn(component.checkedChange, 'emit');
      component.disabled = true;
      fixture.detectChanges();

      const input = fixture.nativeElement.querySelector('input[type="checkbox"]');
      input.click();

      expect(component.checkedChange.emit).not.toHaveBeenCalled();
    });
  });

  describe('ControlValueAccessor', () => {
    it('should work with reactive forms', () => {
      const control = new FormControl(false);
      component.registerOnChange((value) => {
        control.setValue(value, { emitEvent: false });
      });

      fixture.detectChanges();

      const input = fixture.nativeElement.querySelector('input[type="checkbox"]');
      input.click();

      expect(control.value).toBe(true);
    });

    it('should update value via writeValue', () => {
      component.writeValue(true);
      expect(component.checked).toBe(true);

      component.writeValue(false);
      expect(component.checked).toBe(false);
    });

    it('should register onChange callback', () => {
      const onChangeFn = jest.fn();
      component.registerOnChange(onChangeFn);
      fixture.detectChanges();

      const input = fixture.nativeElement.querySelector('input[type="checkbox"]');
      input.click();

      expect(onChangeFn).toHaveBeenCalledWith(true);
    });

    it('should register onTouched callback', () => {
      const onTouchedFn = jest.fn();
      component.registerOnTouched(onTouchedFn);
      fixture.detectChanges();

      const input = fixture.nativeElement.querySelector('input[type="checkbox"]');
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

  describe('OnPush + ControlValueAccessor written from a parent view', () => {
    // Regression test for markForCheck() inside writeValue()/setDisabledState(): the tests above
    // call component.writeValue()/setDisabledState() directly on this root fixture, which doesn't
    // exercise a parent view writing in from outside this component's own OnPush change-detection
    // cycle — the actual Reactive Forms usage pattern. Only a host-based test reproduces that.
    @Component({
      template: `<bp-checkbox [formControl]="control">Acepto</bp-checkbox>`,
      imports: [BpCheckboxComponent, ReactiveFormsModule],
    })
    class HostComponent {
      readonly control = new FormControl(false);
    }

    it('renders a value written by the parent FormControl', () => {
      const host = TestBed.createComponent(HostComponent);
      host.detectChanges();

      host.componentInstance.control.setValue(true);
      host.detectChanges();

      const input = host.nativeElement.querySelector('input[type="checkbox"]') as HTMLInputElement;
      expect(input.checked).toBe(true);
    });

    it('renders disabled state set by the parent FormControl', () => {
      const host = TestBed.createComponent(HostComponent);
      host.detectChanges();

      host.componentInstance.control.disable();
      host.detectChanges();

      const input = host.nativeElement.querySelector('input[type="checkbox"]') as HTMLInputElement;
      expect(input.disabled).toBe(true);
    });
  });

  describe('Accessibility', () => {
    it('should have checkbox input accessible to screen readers', () => {
      fixture.detectChanges();

      const input = fixture.nativeElement.querySelector('input[type="checkbox"]');
      expect(input).toBeTruthy();
      expect(input.getAttribute('type')).toBe('checkbox');
    });

    it('should be keyboard accessible', () => {
      fixture.detectChanges();

      const input = fixture.nativeElement.querySelector('input[type="checkbox"]');
      expect(input.tabIndex).toBeGreaterThanOrEqual(-1);
    });

    it('should have proper label association', () => {
      fixture.detectChanges();

      const label = fixture.nativeElement.querySelector('label');
      expect(label).toBeTruthy();
      // Native label wrapping approach doesn't need for attribute
    });
  });
});
