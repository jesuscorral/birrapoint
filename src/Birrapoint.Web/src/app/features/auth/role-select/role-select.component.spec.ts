import { TestBed } from '@angular/core/testing';
import type { ComponentFixture } from '@angular/core/testing';
import { provideRouter, Router } from '@angular/router';

import { ActiveRoleService } from '../../../core/auth/active-role.service';
import { RoleSelectComponent } from './role-select.component';

function buttonWithText(root: Element, text: string): HTMLButtonElement {
  const buttons = [...root.querySelectorAll('button')] as HTMLButtonElement[];
  const match = buttons.find((button) => button.textContent?.trim() === text);
  if (!match) {
    throw new Error(`No button with text "${text}" found`);
  }
  return match;
}

describe('RoleSelectComponent', () => {
  let navigateSpy: jest.SpyInstance;
  let activeRole: ActiveRoleService;

  function createComponent(): ComponentFixture<RoleSelectComponent> {
    sessionStorage.clear();
    TestBed.configureTestingModule({
      providers: [provideRouter([])],
    });
    const router = TestBed.inject(Router);
    navigateSpy = jest.spyOn(router, 'navigateByUrl').mockResolvedValue(true);
    activeRole = TestBed.inject(ActiveRoleService);
    const fixture = TestBed.createComponent(RoleSelectComponent);
    fixture.detectChanges();
    return fixture;
  }

  it('renders both role options', () => {
    const fixture = createComponent();

    expect(fixture.nativeElement.textContent).toContain('Organizador');
    expect(fixture.nativeElement.textContent).toContain('Juez');
  });

  it('choosing Organizador stores ORGANIZER as the active role and navigates to /organizer/dashboard', () => {
    const fixture = createComponent();

    buttonWithText(fixture.nativeElement, 'Organizador').click();

    expect(activeRole.getActiveRole()).toBe('ORGANIZER');
    expect(navigateSpy).toHaveBeenCalledWith('/organizer/dashboard');
  });

  it('choosing Juez stores JUDGE as the active role and navigates to /judge/tables', () => {
    const fixture = createComponent();

    buttonWithText(fixture.nativeElement, 'Juez').click();

    expect(activeRole.getActiveRole()).toBe('JUDGE');
    expect(navigateSpy).toHaveBeenCalledWith('/judge/tables');
  });
});
