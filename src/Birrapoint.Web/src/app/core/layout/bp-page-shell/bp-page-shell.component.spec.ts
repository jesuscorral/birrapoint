import { Component } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { provideRouter, Router } from '@angular/router';
import { By } from '@angular/platform-browser';
import Keycloak from 'keycloak-js';

import { ActiveRoleService } from '../../auth/active-role.service';
import { BpTopbarComponent } from '../../../shared/components/bp-topbar/bp-topbar.component';
import { BpPageShellComponent } from './bp-page-shell.component';

@Component({
  standalone: true,
  imports: [BpPageShellComponent],
  template: `
    <bp-page-shell [homeLink]="homeLink" [title]="title">
      <p>Projected content</p>
    </bp-page-shell>
  `,
})
class HostComponent {
  homeLink = '/organizer/dashboard';
  title = 'Test title';
}

@Component({
  standalone: true,
  imports: [BpPageShellComponent],
  template: `
    <bp-page-shell>
      <p>Default content</p>
    </bp-page-shell>
  `,
})
class DefaultsHostComponent {}

@Component({
  standalone: true,
  imports: [BpPageShellComponent],
  template: `
    <bp-page-shell>
      <ng-container bpTopbarActions>
        <button type="button">Custom action</button>
      </ng-container>
      <p>Main content</p>
    </bp-page-shell>
  `,
})
class TopbarActionsHostComponent {}

function buttonWithText(root: Element, text: string): HTMLButtonElement | undefined {
  return ([...root.querySelectorAll('button')] as HTMLButtonElement[]).find(
    (button) => button.textContent?.trim() === text,
  );
}

describe('BpPageShellComponent', () => {
  let fakeKeycloak: { logout: jest.Mock; tokenParsed?: { realm_access?: { roles: string[] } } };

  beforeEach(() => {
    sessionStorage.clear();
    fakeKeycloak = { logout: jest.fn(), tokenParsed: { realm_access: { roles: ['ORGANIZER'] } } };
    TestBed.configureTestingModule({
      providers: [provideRouter([]), { provide: Keycloak, useValue: fakeKeycloak }],
    });
  });

  it('renders exactly one topbar header landmark and one main landmark', () => {
    const fixture = TestBed.createComponent(HostComponent);
    fixture.detectChanges();

    expect(fixture.nativeElement.querySelectorAll('header').length).toBe(1);
    expect(fixture.nativeElement.querySelectorAll('main').length).toBe(1);
  });

  it('projects content into the page container inside the main landmark', () => {
    const fixture = TestBed.createComponent(HostComponent);
    fixture.detectChanges();

    const main = fixture.nativeElement.querySelector('main') as HTMLElement;
    expect(main.textContent).toContain('Projected content');
    expect(main.querySelector('.page-container p')?.textContent).toBe('Projected content');
  });

  it('forwards homeLink and title to bp-topbar', () => {
    const fixture = TestBed.createComponent(HostComponent);
    fixture.detectChanges();

    const topbar = fixture.debugElement.query(By.directive(BpTopbarComponent));
    expect(topbar.componentInstance.homeLink()).toBe('/organizer/dashboard');
    expect(topbar.componentInstance.title()).toBe('Test title');
  });

  it('defaults homeLink to /organizer/dashboard and title to empty when not provided', () => {
    const fixture = TestBed.createComponent(DefaultsHostComponent);
    fixture.detectChanges();

    const topbar = fixture.debugElement.query(By.directive(BpTopbarComponent));
    expect(topbar.componentInstance.homeLink()).toBe('/organizer/dashboard');
    expect(topbar.componentInstance.title()).toBe('');
  });

  // A page like the organizer dashboard may need its own extra controls in the topbar, not just
  // the main content area — content marked [bpTopbarActions] is forwarded into bp-topbar's own
  // projection slot rather than landing in <main>, and appears before the shell's own fixed
  // Ajustes/Cerrar sesión actions.
  it('projects [bpTopbarActions] content into the topbar header, not the main landmark', () => {
    const fixture = TestBed.createComponent(TopbarActionsHostComponent);
    fixture.detectChanges();

    const header = fixture.nativeElement.querySelector('header') as HTMLElement;
    const main = fixture.nativeElement.querySelector('main') as HTMLElement;
    expect(header.querySelector('button')?.textContent).toContain('Custom action');
    expect(main.querySelector('button')).toBeNull();
    expect(main.textContent).toContain('Main content');
  });

  describe('fixed identity actions (Session 2026-09-20 — every screen gets these, not just organizer-dashboard)', () => {
    it('renders a fixed "Ajustes" link to /settings', () => {
      const fixture = TestBed.createComponent(DefaultsHostComponent);
      fixture.detectChanges();

      const header = fixture.nativeElement.querySelector('header') as Element;
      const link = header.querySelector('a[href="/settings"]');
      expect(link).not.toBeNull();
      expect(link?.textContent).toContain('Ajustes');
    });

    it('clears the active role and calls keycloak.logout when "Cerrar sesión" is clicked', () => {
      TestBed.inject(ActiveRoleService).setActiveRole('ORGANIZER');
      const fixture = TestBed.createComponent(DefaultsHostComponent);
      fixture.detectChanges();
      const header = fixture.nativeElement.querySelector('header') as Element;

      buttonWithText(header, 'Cerrar sesión')?.click();

      expect(TestBed.inject(ActiveRoleService).getActiveRole()).toBeNull();
      expect(fakeKeycloak.logout).toHaveBeenCalledWith({
        redirectUri: window.location.origin + '/',
      });
    });

    it('does not render "Cambiar rol" for a single-role account', () => {
      const fixture = TestBed.createComponent(DefaultsHostComponent);
      fixture.detectChanges();
      const header = fixture.nativeElement.querySelector('header') as Element;

      expect(buttonWithText(header, 'Cambiar rol')).toBeUndefined();
    });

    it('renders "Cambiar rol" for a dual-role account, clearing the active role and navigating to /select-role', () => {
      fakeKeycloak.tokenParsed = { realm_access: { roles: ['ORGANIZER', 'JUDGE'] } };
      TestBed.inject(ActiveRoleService).setActiveRole('JUDGE');
      const fixture = TestBed.createComponent(DefaultsHostComponent);
      const router = TestBed.inject(Router);
      const navigateSpy = jest.spyOn(router, 'navigateByUrl').mockResolvedValue(true);
      fixture.detectChanges();
      const header = fixture.nativeElement.querySelector('header') as Element;

      buttonWithText(header, 'Cambiar rol')?.click();

      expect(TestBed.inject(ActiveRoleService).getActiveRole()).toBeNull();
      expect(navigateSpy).toHaveBeenCalledWith('/select-role');
    });
  });
});
