import { provideRouter } from '@angular/router';
import { TestBed } from '@angular/core/testing';
import Keycloak from 'keycloak-js';
import { Subject, of, throwError } from 'rxjs';

import { ApiError } from '../../core/api/api-error';
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

describe('JudgeTablesListComponent', () => {
  let fakeApi: { getMyTables: jest.Mock };

  beforeEach(() => {
    sessionStorage.clear();
    fakeApi = { getMyTables: jest.fn().mockReturnValue(of([tableFixture()])) };
    TestBed.configureTestingModule({
      providers: [
        { provide: TastingOrderApiService, useValue: fakeApi },
        { provide: Keycloak, useValue: { tokenParsed: { realm_access: { roles: ['JUDGE'] } } } },
        provideRouter([]),
      ],
    });
  });

  function createComponent() {
    const fixture = TestBed.createComponent(JudgeTablesListComponent);
    fixture.detectChanges();
    return fixture;
  }

  it('renders inside the shared page shell (bp-page-shell — Ajustes/Cerrar sesión fixed here too)', () => {
    const fixture = createComponent();

    expect(fixture.nativeElement.querySelector('header')).not.toBeNull();
    expect(fixture.nativeElement.querySelectorAll('main').length).toBe(1);
  });

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

    expect(fixture.nativeElement.textContent).toContain('Orden sin fijar');
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

    expect(fixture.nativeElement.textContent).toContain('Todavía no tienes mesas asignadas.');
  });

  it('shows a loading state instead of the empty-state message while the API call is in flight', () => {
    const tables$ = new Subject<JudgeTableSummary[]>();
    fakeApi.getMyTables.mockReturnValue(tables$);
    const fixture = createComponent();

    const text = fixture.nativeElement.textContent as string;
    expect(text).toContain('Cargando');
    expect(text).not.toContain('Todavía no tienes mesas asignadas.');

    tables$.next([]);
    fixture.detectChanges();

    const textAfter = fixture.nativeElement.textContent as string;
    expect(textAfter).not.toContain('Cargando');
    expect(textAfter).toContain('Todavía no tienes mesas asignadas.');
  });

  it('surfaces an error message when loading tables fails', () => {
    fakeApi.getMyTables.mockReturnValue(
      throwError(
        () => new ApiError({ status: 500, title: 'Ha ocurrido un error inesperado.', urn: null }),
      ),
    );
    const fixture = createComponent();

    expect(fixture.nativeElement.textContent).toContain('Ha ocurrido un error inesperado.');
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
      expect(banner?.textContent).toContain('El organizador te ha eliminado de Table 1.');
    });

    it('falls back to generic wording when no table name was passed', () => {
      history.replaceState({ ejected: true }, '');
      const fixture = createComponent();

      const banner = fixture.nativeElement.querySelector('[role="status"]');
      expect(banner?.textContent).toContain('El organizador te ha eliminado de una mesa.');
    });

    it('dismisses the banner on click', () => {
      history.replaceState({ ejected: true, tableName: 'Table 1' }, '');
      const fixture = createComponent();

      const banner = fixture.nativeElement.querySelector('[role="status"]') as Element;
      (banner.querySelector('button') as HTMLButtonElement).click();
      fixture.detectChanges();

      expect(fixture.nativeElement.querySelector('[role="status"]')).toBeNull();
    });
  });
});
