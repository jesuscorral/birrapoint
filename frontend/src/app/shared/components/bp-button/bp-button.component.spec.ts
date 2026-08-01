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
      component.label = 'Click me';
      fixture.detectChanges();

      const button = fixture.nativeElement.querySelector('button');
      expect(button.textContent).toContain('Click me');
    });

    it('should render as block when block=true', () => {
      component.block = true;
      fixture.detectChanges();

      const button = fixture.nativeElement.querySelector('button');
      expect(button.classList.contains('w-full')).toBe(true);
    });

    it('should have correct type attribute', () => {
      component.type = 'submit';
      fixture.detectChanges();

      const button = fixture.nativeElement.querySelector('button');
      expect(button.type).toBe('submit');
    });
  });

  describe('Variants', () => {
    it('should apply primary variant classes', () => {
      component.variant = 'primary';
      fixture.detectChanges();

      const button = fixture.nativeElement.querySelector('button');
      expect(button.classList.toString()).toContain('bg-bp-cobre-500');
    });

    it('should apply secondary variant classes', () => {
      component.variant = 'secondary';
      fixture.detectChanges();

      const button = fixture.nativeElement.querySelector('button');
      expect(button.classList.toString()).toContain('bg-bp-surface');
    });

    it('should apply ghost variant classes', () => {
      component.variant = 'ghost';
      fixture.detectChanges();

      const button = fixture.nativeElement.querySelector('button');
      expect(button.classList.toString()).toContain('text-bp-text-muted');
    });
  });

  describe('Loading state', () => {
    it('should show spinner when loading=true', () => {
      component.loading = true;
      fixture.detectChanges();

      const spinner = fixture.nativeElement.querySelector('.spinner');
      expect(spinner).toBeTruthy();
    });

    it('should set aria-busy when loading', () => {
      component.loading = true;
      fixture.detectChanges();

      const button = fixture.nativeElement.querySelector('button');
      expect(button.getAttribute('aria-busy')).toBe('true');
    });

    it('should disable button when loading', () => {
      component.loading = true;
      fixture.detectChanges();

      const button = fixture.nativeElement.querySelector('button');
      expect(button.disabled).toBe(true);
    });

    it('should reduce opacity when loading', () => {
      component.loading = true;
      fixture.detectChanges();

      const button = fixture.nativeElement.querySelector('button');
      expect(button.classList.toString()).toContain('opacity-50');
    });
  });

  describe('Disabled state', () => {
    it('should disable button when disabled=true', () => {
      component.disabled = true;
      fixture.detectChanges();

      const button = fixture.nativeElement.querySelector('button');
      expect(button.disabled).toBe(true);
    });

    it('should disable button when loading=true', () => {
      component.loading = true;
      fixture.detectChanges();

      const button = fixture.nativeElement.querySelector('button');
      expect(button.disabled).toBe(true);
    });

    it('should have pointer-events-none when disabled', () => {
      component.disabled = true;
      fixture.detectChanges();

      const button = fixture.nativeElement.querySelector('button');
      expect(button.classList.toString()).toContain('pointer-events-none');
    });
  });

  describe('Click event', () => {
    it('should emit onClick event when clicked', () => {
      jest.spyOn(component.onClick, 'emit');
      component.disabled = false;
      component.loading = false;
      fixture.detectChanges();

      const button = fixture.nativeElement.querySelector('button');
      button.click();

      expect(component.onClick.emit).toHaveBeenCalled();
    });

    it('should not emit click when disabled', () => {
      jest.spyOn(component.onClick, 'emit');
      component.disabled = true;
      fixture.detectChanges();

      const button = fixture.nativeElement.querySelector('button');
      button.click();

      // Button is disabled, so click won't fire
      expect(component.onClick.emit).not.toHaveBeenCalled();
    });
  });

  describe('Sizes', () => {
    it('should apply sm size classes', () => {
      component.size = 'sm';
      fixture.detectChanges();

      const button = fixture.nativeElement.querySelector('button');
      expect(button.classList.toString()).toContain('min-h-9');
    });

    it('should apply lg size classes', () => {
      component.size = 'lg';
      fixture.detectChanges();

      const button = fixture.nativeElement.querySelector('button');
      expect(button.classList.toString()).toContain('min-h-13');
    });

    it('should apply md (default) size classes', () => {
      component.size = 'md';
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
      component.variant = 'primary';
      fixture.detectChanges();

      const button = fixture.nativeElement.querySelector('button');
      expect(button.classList.toString()).toContain('focus-visible');
    });
  });
});
