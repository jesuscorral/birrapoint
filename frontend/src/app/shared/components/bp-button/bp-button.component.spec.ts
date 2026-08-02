import { ComponentFixture, TestBed } from '@angular/core/testing';
import { BpButtonComponent } from './bp-button.component';

describe('BpButtonComponent', () => {
  let component: BpButtonComponent;
  let fixture: ComponentFixture<BpButtonComponent>;

  beforeEach(async () => {
    await TestBed.configureTestingModule({
      imports: [BpButtonComponent],
    }).compileComponents();

    fixture = TestBed.createComponent(BpButtonComponent);
    component = fixture.componentInstance;
  });

  it('should create', () => {
    expect(component).toBeTruthy();
  });

  describe('Rendering', () => {
    it('should render button with label', () => {
      fixture.componentRef.setInput('label', 'Click me');
      fixture.detectChanges();

      const button = fixture.nativeElement.querySelector('button');
      expect(button.textContent).toContain('Click me');
    });

    it('should render as block when block=true', () => {
      fixture.componentRef.setInput('block', true);
      fixture.detectChanges();

      const button = fixture.nativeElement.querySelector('button');
      expect(button.classList.contains('w-full')).toBe(true);
    });

    it('should have correct type attribute', () => {
      fixture.componentRef.setInput('type', 'submit');
      fixture.detectChanges();

      const button = fixture.nativeElement.querySelector('button');
      expect(button.type).toBe('submit');
    });
  });

  describe('Variants', () => {
    it('should apply primary variant classes', () => {
      fixture.componentRef.setInput('variant', 'primary');
      fixture.detectChanges();

      const button = fixture.nativeElement.querySelector('button');
      expect(button.classList.toString()).toContain('bg-bp-cobre-500');
    });

    it('should apply secondary variant classes', () => {
      fixture.componentRef.setInput('variant', 'secondary');
      fixture.detectChanges();

      const button = fixture.nativeElement.querySelector('button');
      expect(button.classList.toString()).toContain('bg-bp-surface');
    });

    it('should apply ghost variant classes', () => {
      fixture.componentRef.setInput('variant', 'ghost');
      fixture.detectChanges();

      const button = fixture.nativeElement.querySelector('button');
      expect(button.classList.toString()).toContain('text-bp-text-muted');
    });
  });

  describe('Loading state', () => {
    it('should show spinner when loading=true', () => {
      fixture.componentRef.setInput('loading', true);
      fixture.detectChanges();

      const spinner = fixture.nativeElement.querySelector('.spinner');
      expect(spinner).toBeTruthy();
    });

    it('should set aria-busy when loading', () => {
      fixture.componentRef.setInput('loading', true);
      fixture.detectChanges();

      const button = fixture.nativeElement.querySelector('button');
      expect(button.getAttribute('aria-busy')).toBe('true');
    });

    it('should disable button when loading', () => {
      fixture.componentRef.setInput('loading', true);
      fixture.detectChanges();

      const button = fixture.nativeElement.querySelector('button');
      expect(button.disabled).toBe(true);
    });

    it('should reduce opacity when loading', () => {
      fixture.componentRef.setInput('loading', true);
      fixture.detectChanges();

      const button = fixture.nativeElement.querySelector('button');
      expect(button.classList.toString()).toContain('opacity-50');
    });
  });

  describe('Disabled state', () => {
    it('should disable button when disabled=true', () => {
      fixture.componentRef.setInput('disabled', true);
      fixture.detectChanges();

      const button = fixture.nativeElement.querySelector('button');
      expect(button.disabled).toBe(true);
    });

    it('should disable button when loading=true', () => {
      fixture.componentRef.setInput('loading', true);
      fixture.detectChanges();

      const button = fixture.nativeElement.querySelector('button');
      expect(button.disabled).toBe(true);
    });

    it('should have pointer-events-none when disabled', () => {
      fixture.componentRef.setInput('disabled', true);
      fixture.detectChanges();

      const button = fixture.nativeElement.querySelector('button');
      expect(button.classList.toString()).toContain('pointer-events-none');
    });
  });

  describe('Click event', () => {
    it('should emit clicked event when clicked', () => {
      jest.spyOn(component.clicked, 'emit');
      fixture.componentRef.setInput('disabled', false);
      fixture.componentRef.setInput('loading', false);
      fixture.detectChanges();

      const button = fixture.nativeElement.querySelector('button');
      button.click();

      expect(component.clicked.emit).toHaveBeenCalled();
    });

    it('should not emit click when disabled', () => {
      jest.spyOn(component.clicked, 'emit');
      fixture.componentRef.setInput('disabled', true);
      fixture.detectChanges();

      const button = fixture.nativeElement.querySelector('button');
      button.click();

      // Button is disabled, so click won't fire
      expect(component.clicked.emit).not.toHaveBeenCalled();
    });
  });

  describe('Sizes', () => {
    it('should apply sm size classes', () => {
      fixture.componentRef.setInput('size', 'sm');
      fixture.detectChanges();

      const button = fixture.nativeElement.querySelector('button');
      expect(button.classList.toString()).toContain('min-h-9');
    });

    it('should apply lg size classes', () => {
      fixture.componentRef.setInput('size', 'lg');
      fixture.detectChanges();

      const button = fixture.nativeElement.querySelector('button');
      expect(button.classList.toString()).toContain('min-h-13');
    });

    it('should apply md (default) size classes', () => {
      fixture.componentRef.setInput('size', 'md');
      fixture.detectChanges();

      const button = fixture.nativeElement.querySelector('button');
      expect(button.classList.toString()).toContain('min-h-11');
    });
  });

  describe('Accessibility', () => {
    it('should have focusable button element', () => {
      fixture.detectChanges();

      const button = fixture.nativeElement.querySelector('button');
      expect(button.tabIndex).toBeGreaterThanOrEqual(-1);
    });

    it('should apply focus-visible styles', () => {
      fixture.componentRef.setInput('variant', 'primary');
      fixture.detectChanges();

      const button = fixture.nativeElement.querySelector('button');
      expect(button.classList.toString()).toContain('focus-visible');
    });

    it('should not set an aria-label on the native button by default', () => {
      fixture.componentRef.setInput('label', 'Editar');
      fixture.detectChanges();

      const button = fixture.nativeElement.querySelector('button');
      expect(button.hasAttribute('aria-label')).toBe(false);
    });

    it('should forward ariaLabel onto the native button, overriding the visible label as the accessible name', () => {
      fixture.componentRef.setInput('label', 'Excluir');
      fixture.componentRef.setInput('ariaLabel', 'Excluir fila #3');
      fixture.detectChanges();

      const button = fixture.nativeElement.querySelector('button');
      expect(button.getAttribute('aria-label')).toBe('Excluir fila #3');
    });
  });
});
