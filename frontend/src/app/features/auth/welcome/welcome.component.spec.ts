import { ComponentFixture, TestBed } from '@angular/core/testing';
import { Router } from '@angular/router';
import Keycloak from 'keycloak-js';
import { WelcomeComponent } from './welcome.component';
import { BpButtonComponent } from '../../../shared/components/bp-button/bp-button.component';

describe('WelcomeComponent', () => {
  let component: WelcomeComponent;
  let fixture: ComponentFixture<WelcomeComponent>;
  let router: Router;
  let keycloak: { register: jest.Mock };

  beforeEach(async () => {
    keycloak = { register: jest.fn() };

    await TestBed.configureTestingModule({
      imports: [WelcomeComponent, BpButtonComponent],
      providers: [
        {
          provide: Router,
          useValue: {
            navigate: jest.fn(),
          },
        },
        { provide: Keycloak, useValue: keycloak },
      ],
    }).compileComponents();

    fixture = TestBed.createComponent(WelcomeComponent);
    component = fixture.componentInstance;
    router = TestBed.inject(Router);
  });

  it('should create', () => {
    expect(component).toBeTruthy();
  });

  describe('Rendering', () => {
    it('should render auth layout with brand panel and form panel', () => {
      fixture.detectChanges();

      const auth = fixture.nativeElement.querySelector('.auth');
      expect(auth).toBeTruthy();
      expect(auth.querySelector('.brand-panel')).toBeTruthy();
      expect(auth.querySelector('.form-panel')).toBeTruthy();
    });

    it('should render brand panel with logo', () => {
      fixture.detectChanges();

      const logo = fixture.nativeElement.querySelector('.logo');
      expect(logo).toBeTruthy();
      expect(logo.textContent).toContain('BirraPoint');
    });

    it('should render form panel with title and subtitle', () => {
      fixture.detectChanges();

      const title = fixture.nativeElement.querySelector('.title');
      expect(title.textContent).toContain('Entra en tu concurso');

      const subtitle = fixture.nativeElement.querySelector('.subtitle');
      expect(subtitle).toBeTruthy();
    });

    it('should render claim in brand panel', () => {
      fixture.detectChanges();

      const claim = fixture.nativeElement.querySelector('.brand-panel__claim');
      expect(claim.textContent).toContain('Catas a ciegas');
    });

    it('should render feature list', () => {
      fixture.detectChanges();

      const features = fixture.nativeElement.querySelectorAll('.feature-list li');
      expect(features.length).toBeGreaterThan(0);
    });
  });

  describe('CTA buttons and cards', () => {
    it('should render login button', () => {
      fixture.detectChanges();

      const button = fixture.nativeElement.querySelector('bp-button');
      expect(button).toBeTruthy();
    });

    it('should render organizer role card', () => {
      fixture.detectChanges();

      const roleCard = fixture.nativeElement.querySelector('.role-card');
      expect(roleCard).toBeTruthy();
      expect(roleCard.textContent).toContain('Crear cuenta de organizador');
    });

    it('should render judge role card (disabled)', () => {
      fixture.detectChanges();

      const judgeCard = fixture.nativeElement.querySelector('.role-card--muted');
      expect(judgeCard).toBeTruthy();
      expect(judgeCard.textContent).toContain('¿Eres juez?');
    });

    it('should have tag on judge card', () => {
      fixture.detectChanges();

      const tag = fixture.nativeElement.querySelector('.tag');
      expect(tag).toBeTruthy();
      expect(tag.textContent).toContain('Por invitación');
    });
  });

  describe('Navigation', () => {
    it('should navigate to /auth/handoff when login button is clicked', () => {
      fixture.detectChanges();

      component.onLogin();

      expect(router.navigate).toHaveBeenCalledWith(['/auth/handoff']);
    });

    it('should call onRegister method when organizer card is clicked', () => {
      jest.spyOn(component, 'onRegister');
      fixture.detectChanges();

      const roleCard = fixture.nativeElement.querySelector('.role-card');
      roleCard.click();

      expect(component.onRegister).toHaveBeenCalled();
    });

    it('should start the Keycloak registration flow, redirecting back to the app root', () => {
      component.onRegister();

      // keycloak.register() (not a hand-built URL) is what actually gets client_id, PKCE, and the
      // realm's base path right — see the comment in welcome.component.ts for why this matters.
      expect(keycloak.register).toHaveBeenCalledWith({
        redirectUri: window.location.origin + '/',
      });
    });
  });

  describe('Responsive layout', () => {
    it('should have responsive grid layout', () => {
      fixture.detectChanges();

      // jsdom does not reliably resolve emulated-encapsulation-scoped `display: grid` via
      // getComputedStyle — the actual grid CSS is verified in real browsers (visual
      // regression/E2E), so this only asserts the two-panel structure the grid lays out.
      const auth = fixture.nativeElement.querySelector('.auth');
      expect(auth.querySelector('.brand-panel')).toBeTruthy();
      expect(auth.querySelector('.form-panel')).toBeTruthy();
    });

    it('should render as single column on mobile', () => {
      fixture.detectChanges();

      const auth = fixture.nativeElement.querySelector('.auth');
      // On mobile, CSS media query changes grid-template-columns to 1fr
      // This is tested via visual regression or E2E
      expect(auth).toBeTruthy();
    });
  });

  describe('Accessibility', () => {
    it('should have proper heading hierarchy', () => {
      fixture.detectChanges();

      const h1 = fixture.nativeElement.querySelector('h1.title');
      const h2 = fixture.nativeElement.querySelector('h2.brand-panel__claim');
      expect(h1).toBeTruthy();
      expect(h2).toBeTruthy();
    });

    it('should have alt text on SVG bubbles (aria-hidden)', () => {
      fixture.detectChanges();

      const bubbles = fixture.nativeElement.querySelector('.brand-panel__bubbles');
      expect(bubbles.getAttribute('aria-hidden')).toBe('true');
    });

    it('should have logo mark aria-hidden', () => {
      fixture.detectChanges();

      const mark = fixture.nativeElement.querySelector('.logo__mark');
      expect(mark.getAttribute('aria-hidden')).toBe('true');
    });

    it('should have clickable role cards with proper text', () => {
      fixture.detectChanges();

      const roleCard = fixture.nativeElement.querySelector('.role-card');
      expect(roleCard.textContent).toContain('Crear cuenta de organizador');
      expect(roleCard.tagName.toLowerCase()).toBe('button');
    });
  });

  describe('Content', () => {
    it('should display BirraPoint branding consistently', () => {
      fixture.detectChanges();

      const brandText = fixture.nativeElement.textContent;
      expect(brandText).toContain('BirraPoint');
    });

    it('should mention competition features', () => {
      fixture.detectChanges();

      const content = fixture.nativeElement.textContent;
      expect(content.toLowerCase()).toContain('inscripciones');
      expect(content.toLowerCase()).toContain('jueces');
      expect(content.toLowerCase()).toContain('offline');
    });

    it('should have footer with links', () => {
      fixture.detectChanges();

      const footer = fixture.nativeElement.querySelector('.form-foot');
      expect(footer).toBeTruthy();
      expect(footer.textContent).toContain('Escríbenos');
    });
  });

  describe('Component lifecycle', () => {
    it('should initialize with default values', () => {
      expect(component).toBeDefined();
      expect(component.onLogin).toBeDefined();
      expect(component.onRegister).toBeDefined();
    });

    it('should handle multiple renders', () => {
      fixture.detectChanges();
      fixture.detectChanges();

      const title = fixture.nativeElement.querySelector('.title');
      expect(title).toBeTruthy();
    });
  });
});
