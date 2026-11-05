import { Component } from '@angular/core';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { BpAlertComponent } from './bp-alert.component';

describe('BpAlertComponent', () => {
  let component: BpAlertComponent;
  let fixture: ComponentFixture<BpAlertComponent>;

  beforeEach(async () => {
    await TestBed.configureTestingModule({
      imports: [BpAlertComponent],
    }).compileComponents();

    fixture = TestBed.createComponent(BpAlertComponent);
    component = fixture.componentInstance;
  });

  it('should create', () => {
    expect(component).toBeTruthy();
  });

  describe('Alert types', () => {
    it('should render error alert', () => {
      fixture.componentRef.setInput('type', 'error');
      fixture.detectChanges();

      const alert = fixture.nativeElement.querySelector('.alert');
      expect(alert.classList.toString()).toContain('alert-error');
    });

    it('should render info alert', () => {
      fixture.componentRef.setInput('type', 'info');
      fixture.detectChanges();

      const alert = fixture.nativeElement.querySelector('.alert');
      expect(alert.classList.toString()).toContain('alert-info');
    });

    it('should render success alert', () => {
      fixture.componentRef.setInput('type', 'success');
      fixture.detectChanges();

      const alert = fixture.nativeElement.querySelector('.alert');
      expect(alert.classList.toString()).toContain('alert-success');
    });
  });

  describe('Icon rendering', () => {
    it('should render error icon for error type', () => {
      fixture.componentRef.setInput('type', 'error');
      fixture.detectChanges();

      const svg = fixture.nativeElement.querySelector('svg');
      expect(svg).toBeTruthy();
      expect(svg.innerHTML).toContain('circle');
    });

    it('should render success icon for success type', () => {
      fixture.componentRef.setInput('type', 'success');
      fixture.detectChanges();

      const svg = fixture.nativeElement.querySelector('svg');
      expect(svg).toBeTruthy();
      expect(svg.innerHTML).toContain('path');
    });

    it('should render info icon for info type', () => {
      fixture.componentRef.setInput('type', 'info');
      fixture.detectChanges();

      const svg = fixture.nativeElement.querySelector('svg');
      expect(svg).toBeTruthy();
    });
  });

  describe('Title and content', () => {
    it('should render title when provided', () => {
      fixture.componentRef.setInput('title', 'Error occurred');
      fixture.detectChanges();

      const title = fixture.nativeElement.querySelector('.alert-title');
      expect(title.textContent).toContain('Error occurred');
    });

    it('should project content via ng-content', () => {
      @Component({
        template: `<bp-alert title="Error">{{ content }}</bp-alert>`,
        imports: [BpAlertComponent],
      })
      class HostComponent {
        content = 'Something went wrong';
      }

      const hostFixture = TestBed.createComponent(HostComponent);
      hostFixture.detectChanges();

      const alert = hostFixture.nativeElement.querySelector('.alert');
      expect(alert.textContent).toContain('Something went wrong');
    });
  });

  describe('ARIA attributes', () => {
    it('should have role="alert" by default', () => {
      fixture.componentRef.setInput('role', 'alert');
      fixture.detectChanges();

      const alert = fixture.nativeElement.querySelector('.alert');
      expect(alert.getAttribute('role')).toBe('alert');
    });

    it('should have role="status" when specified', () => {
      fixture.componentRef.setInput('role', 'status');
      fixture.detectChanges();

      const alert = fixture.nativeElement.querySelector('.alert');
      expect(alert.getAttribute('role')).toBe('status');
    });

    it('should have aria-hidden on icon', () => {
      fixture.detectChanges();

      const svg = fixture.nativeElement.querySelector('svg');
      expect(svg.getAttribute('aria-hidden')).toBe('true');
    });
  });

  describe('Styling', () => {
    it('should apply error styling (background, border, text)', () => {
      fixture.componentRef.setInput('type', 'error');
      fixture.detectChanges();

      const alert = fixture.nativeElement.querySelector('.alert');
      const classList = alert.classList.toString();
      expect(classList).toContain('alert-error');
    });

    it('should have icon element', () => {
      fixture.detectChanges();

      const icon = fixture.nativeElement.querySelector('.alert__icon');
      expect(icon).toBeTruthy();
    });
  });

  describe('Default values', () => {
    it('should default to info type', () => {
      fixture.detectChanges();

      const alert = fixture.nativeElement.querySelector('.alert');
      expect(alert.classList.toString()).toContain('alert-info');
    });

    it('should default to alert role', () => {
      fixture.detectChanges();

      const alert = fixture.nativeElement.querySelector('.alert');
      expect(alert.getAttribute('role')).toBe('alert');
    });

    it('should have empty title by default', () => {
      fixture.detectChanges();

      const title = fixture.nativeElement.querySelector('.alert-title');
      expect(title).toBeFalsy();
    });
  });
});
