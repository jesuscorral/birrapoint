import { provideRouter, Router } from '@angular/router';
import { TestBed } from '@angular/core/testing';
import Keycloak from 'keycloak-js';
import { of, throwError } from 'rxjs';

import { ApiError } from '../../core/api/api-error';
import { ActiveRoleService } from '../../core/auth/active-role.service';
import { JudgeTablesListComponent } from './judge-tables-list.component';
import { TastingOrderApiService } from './tasting-order-api.service';
import type { JudgeTableSummary } from './tasting-order-api.service';

function tableFixture(overrides: Partial<JudgeTableSummary> = {}): JudgeTableSummary {
  return {
    tableId: 't1',
    name: 'Table 1',
    competitionState: 'Active',
    tableState: 'Open',
    orderFixed: false,
    orderFixedBy: null,
    ...overrides,
  };
}

function buttonWithText(root: Element, text: string): HTMLButtonElement | undefined {
  return ([...root.querySelectorAll('button')] as HTMLButtonElement[]).find(
    (button) => button.textContent?.trim() === text,
  );
}

describe('JudgeTablesListComponent', () => {
  let fakeApi: { getMyTables: jest.Mock };
  let fakeKeycloak: { tokenParsed?: { realm_access?: { roles: string[] } } };

  beforeEach(() => {
    sessionStorage.clear();
    fakeApi = { getMyTables: jest.fn().mockReturnValue(of([tableFixture()])) };
    fakeKeycloak = { tokenParsed: { realm_access: { roles: ['JUDGE'] } } };
    TestBed.configureTestingModule({
      providers: [
        { provide: TastingOrderApiService, useValue: fakeApi },
        { provide: Keycloak, useValue: fakeKeycloak },
        provideRouter([]),
      ],
    });
  });

  function createComponent() {
    const fixture = TestBed.createComponent(JudgeTablesListComponent);
    fixture.detectChanges();
    return fixture;
  }

  it('loads and renders the assigned tables', () => {
    const fixture = createComponent();

    expect(fakeApi.getMyTables).toHaveBeenCalled();
    const text = fixture.nativeElement.textContent as string;
    expect(text).toContain('Table 1');
    expect(text).toContain('Active');
    expect(text).toContain('Open');
  });

  it('shows an order-not-fixed badge when the table order is not fixed', () => {
    const fixture = createComponent();

    expect(fixture.nativeElement.textContent).toContain('Order not fixed');
  });

  it('shows the fixer name when the table order is already fixed', () => {
    fakeApi.getMyTables.mockReturnValue(
      of([tableFixture({ orderFixed: true, orderFixedBy: 'Ada Lovelace' })]),
    );
    const fixture = createComponent();

    expect(fixture.nativeElement.textContent).toContain('Ada Lovelace');
  });

  it('links each table to its per-table order view', () => {
    const fixture = createComponent();

    const link = fixture.nativeElement.querySelector('a[href="/judge/tables/t1"]');
    expect(link).not.toBeNull();
  });

  it('shows an empty-state message when the judge has no assigned tables', () => {
    fakeApi.getMyTables.mockReturnValue(of([]));
    const fixture = createComponent();

    expect(fixture.nativeElement.textContent).toContain('No tables assigned yet');
  });

  it('surfaces an error message when loading tables fails', () => {
    fakeApi.getMyTables.mockReturnValue(
      throwError(
        () => new ApiError({ status: 500, title: 'An unexpected error occurred.', urn: null }),
      ),
    );
    const fixture = createComponent();

    expect(fixture.nativeElement.textContent).toContain('An unexpected error occurred.');
  });

  describe('ejection banner (T087/US12)', () => {
    afterEach(() => {
      history.replaceState(null, '');
    });

    it('shows no banner by default', () => {
      const fixture = createComponent();

      expect(fixture.nativeElement.querySelector('[role="status"]')).toBeNull();
    });

    it('shows a banner naming the table when redirected here after an ejection', () => {
      history.replaceState({ ejected: true, tableName: 'Table 1' }, '');
      const fixture = createComponent();

      const banner = fixture.nativeElement.querySelector('[role="status"]');
      expect(banner?.textContent).toContain('You were removed from Table 1 by the organizer.');
    });

    it('falls back to generic wording when no table name was passed', () => {
      history.replaceState({ ejected: true }, '');
      const fixture = createComponent();

      const banner = fixture.nativeElement.querySelector('[role="status"]');
      expect(banner?.textContent).toContain('You were removed from a table by the organizer.');
    });

    it('dismisses the banner on click', () => {
      history.replaceState({ ejected: true, tableName: 'Table 1' }, '');
      const fixture = createComponent();

      ([...fixture.nativeElement.querySelectorAll('button')] as HTMLButtonElement[])[0].click();
      fixture.detectChanges();

      expect(fixture.nativeElement.querySelector('[role="status"]')).toBeNull();
    });
  });

  describe('"Cambiar rol" (dual-role accounts only)', () => {
    it('does not render for a JUDGE-only account', () => {
      const fixture = createComponent();

      expect(buttonWithText(fixture.nativeElement, 'Cambiar rol')).toBeUndefined();
    });

    it('clears the active role and navigates to /select-role for a dual-role account', () => {
      fakeKeycloak.tokenParsed = { realm_access: { roles: ['ORGANIZER', 'JUDGE'] } };
      TestBed.inject(ActiveRoleService).setActiveRole('JUDGE');
      const fixture = createComponent();
      const router = TestBed.inject(Router);
      const navigateSpy = jest.spyOn(router, 'navigateByUrl').mockResolvedValue(true);

      buttonWithText(fixture.nativeElement, 'Cambiar rol')!.click();

      expect(TestBed.inject(ActiveRoleService).getActiveRole()).toBeNull();
      expect(navigateSpy).toHaveBeenCalledWith('/select-role');
    });
  });
});
