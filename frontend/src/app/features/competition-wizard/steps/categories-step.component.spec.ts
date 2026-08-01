import { TestBed } from '@angular/core/testing';
import { provideRouter, Router } from '@angular/router';
import { of } from 'rxjs';

import { CatalogApiService } from '../../../core/api/catalog-api.service';
import type { StyleSummary } from '../../../core/api/catalog-api.service';
import { CompetitionsApiService } from '../../../core/api/competitions-api.service';
import type { CompetitionCategoriesResponse } from '../../../core/api/competitions-api.service';
import { CategoriesStepComponent } from './categories-step.component';

function styleFixture(overrides: Partial<StyleSummary> = {}): StyleSummary {
  return {
    code: '18A',
    name: 'Blonde Ale',
    categoryNumber: '18',
    categoryName: 'Pale American Ale',
    ...overrides,
  };
}

function categoriesResponseFixture(
  overrides: Partial<CompetitionCategoriesResponse> = {},
): CompetitionCategoriesResponse {
  return { categories: [], ...overrides };
}

function groupStyleFixtures(): StyleSummary[] {
  return [
    styleFixture({ code: '18A', name: 'Blonde Ale' }),
    styleFixture({ code: '18B', name: 'American Pale Ale' }),
    styleFixture({ code: '18C', name: 'Imperial Pale Ale' }),
  ];
}

describe('CategoriesStepComponent', () => {
  let fakeCatalogApi: { getStyles: jest.Mock };
  let fakeCompetitionsApi: { getCategories: jest.Mock; setCategories: jest.Mock };

  beforeEach(() => {
    fakeCatalogApi = { getStyles: jest.fn() };
    fakeCompetitionsApi = { getCategories: jest.fn(), setCategories: jest.fn() };
    fakeCatalogApi.getStyles.mockReturnValue(of([]));
    fakeCompetitionsApi.getCategories.mockReturnValue(of(categoriesResponseFixture()));

    TestBed.configureTestingModule({
      providers: [
        { provide: CatalogApiService, useValue: fakeCatalogApi },
        { provide: CompetitionsApiService, useValue: fakeCompetitionsApi },
        provideRouter([]),
      ],
    });
  });

  function createComponent() {
    const fixture = TestBed.createComponent(CategoriesStepComponent);
    fixture.componentRef.setInput('competitionId', 'c1');
    fixture.detectChanges();
    return fixture;
  }

  it('loads the BJCP catalog and existing categories on init', () => {
    fakeCatalogApi.getStyles.mockReturnValue(of([styleFixture()]));
    fakeCompetitionsApi.getCategories.mockReturnValue(
      of(
        categoriesResponseFixture({
          categories: [
            { id: 'cat-1', name: 'Estilos clásicos', displayOrder: 0, styleCodes: ['18A'] },
          ],
        }),
      ),
    );

    const fixture = createComponent();

    expect(fakeCatalogApi.getStyles).toHaveBeenCalled();
    expect(fakeCompetitionsApi.getCategories).toHaveBeenCalledWith('c1');
    expect(fixture.nativeElement.textContent).toContain('Estilos clásicos');
    expect(fixture.nativeElement.textContent).toContain('Pale American Ale');
    expect(fixture.nativeElement.textContent).toContain('Blonde Ale');
  });

  it('reflects a previously-assigned style as selected in its <select> on the very first render', () => {
    // Regression test: a native <select> bound via [value] on the host element can silently fail
    // to select the matching @for-generated <option> when that option doesn't exist in the DOM
    // yet at the moment Angular first applies the binding — and since the computed value never
    // changes afterward, Angular's dirty-check skips re-applying it, permanently leaving the
    // select stuck on its default option even though the underlying signal state is correct. Only
    // asserting on the component's signals/helpers (as the tests above do) doesn't catch this —
    // it must be asserted on the actual rendered <select>'s own value/selectedIndex.
    fakeCatalogApi.getStyles.mockReturnValue(of([styleFixture()]));
    fakeCompetitionsApi.getCategories.mockReturnValue(
      of(
        categoriesResponseFixture({
          categories: [
            { id: 'cat-1', name: 'Estilos clásicos', displayOrder: 0, styleCodes: ['18A'] },
          ],
        }),
      ),
    );

    const fixture = createComponent();

    const select = fixture.nativeElement.querySelector('.style-row__select') as HTMLSelectElement;
    expect(select.value).toBe('0');
    expect(select.selectedOptions[0].text).toBe('Estilos clásicos');
  });

  it('seeds a default "General" category when the competition has none yet', () => {
    const fixture = createComponent();

    expect(fixture.componentInstance.categories()).toEqual([
      { id: null, name: 'General', displayOrder: 0, styleCodes: [] },
    ]);
  });

  it('adds and removes a category row', () => {
    const fixture = createComponent();

    fixture.componentInstance.addCategory();
    fixture.detectChanges();
    expect(fixture.componentInstance.categories().length).toBe(2);
    expect(fixture.componentInstance.categories()[1]).toEqual({
      id: null,
      name: '',
      displayOrder: 1,
      styleCodes: [],
    });

    fixture.componentInstance.removeCategory(1);
    fixture.detectChanges();
    expect(fixture.componentInstance.categories()).toEqual([
      { id: null, name: 'General', displayOrder: 0, styleCodes: [] },
    ]);
  });

  it('never removes the last remaining category', () => {
    const fixture = createComponent();

    fixture.componentInstance.removeCategory(0);
    fixture.detectChanges();

    expect(fixture.componentInstance.categories().length).toBe(1);
  });

  it('toggles a category row into edit mode and back', () => {
    const fixture = createComponent();

    expect(fixture.nativeElement.querySelector('#category-name-0')).toBeFalsy();

    fixture.componentInstance.startEditing(0);
    fixture.detectChanges();
    expect(fixture.nativeElement.querySelector('#category-name-0')).toBeTruthy();

    fixture.componentInstance.stopEditing();
    fixture.detectChanges();
    expect(fixture.nativeElement.querySelector('#category-name-0')).toBeFalsy();
  });

  it('assigning a style to a category removes it from any previously-assigned category', () => {
    fakeCatalogApi.getStyles.mockReturnValue(of([styleFixture()]));
    fakeCompetitionsApi.getCategories.mockReturnValue(
      of(
        categoriesResponseFixture({
          categories: [
            { id: 'cat-1', name: 'A', displayOrder: 0, styleCodes: ['18A'] },
            { id: 'cat-2', name: 'B', displayOrder: 1, styleCodes: [] },
          ],
        }),
      ),
    );
    const fixture = createComponent();

    fixture.componentInstance.onAssignStyle('18A', '1');
    fixture.detectChanges();

    const rows = fixture.componentInstance.categories();
    expect(rows[0].styleCodes).toEqual([]);
    expect(rows[1].styleCodes).toEqual(['18A']);
  });

  it('unassigns a style back to "Sin asignar"', () => {
    fakeCatalogApi.getStyles.mockReturnValue(of([styleFixture()]));
    fakeCompetitionsApi.getCategories.mockReturnValue(
      of(
        categoriesResponseFixture({
          categories: [{ id: 'cat-1', name: 'A', displayOrder: 0, styleCodes: ['18A'] }],
        }),
      ),
    );
    const fixture = createComponent();

    fixture.componentInstance.onAssignStyle('18A', '');
    fixture.detectChanges();

    expect(fixture.componentInstance.categories()[0].styleCodes).toEqual([]);
  });

  it('renders one bulk-assign select per BJCP group, not one per style', () => {
    fakeCatalogApi.getStyles.mockReturnValue(of(groupStyleFixtures()));
    const fixture = createComponent();

    const bulkSelects = fixture.nativeElement.querySelectorAll('.style-group__bulk-select');
    expect(bulkSelects.length).toBe(1);
  });

  it('bulk-assigns every style in a BJCP group to the chosen category', () => {
    fakeCatalogApi.getStyles.mockReturnValue(of(groupStyleFixtures()));
    fakeCompetitionsApi.getCategories.mockReturnValue(
      of(
        categoriesResponseFixture({
          categories: [
            { id: 'cat-1', name: 'A', displayOrder: 0, styleCodes: [] },
            { id: 'cat-2', name: 'B', displayOrder: 1, styleCodes: [] },
          ],
        }),
      ),
    );
    const fixture = createComponent();

    fixture.componentInstance.onBulkAssignGroup(fixture.componentInstance.groupedCatalog()[0], '1');
    fixture.detectChanges();

    const rows = fixture.componentInstance.categories();
    expect(rows[0].styleCodes).toEqual([]);
    expect(rows[1].styleCodes).toEqual(['18A', '18B', '18C']);
    expect(fixture.componentInstance.styleSelectValue('18A')).toBe('1');
    expect(fixture.componentInstance.styleSelectValue('18B')).toBe('1');
    expect(fixture.componentInstance.styleSelectValue('18C')).toBe('1');
  });

  it('overwrites individual per-style assignments when bulk-assigning the whole group', () => {
    fakeCatalogApi.getStyles.mockReturnValue(of(groupStyleFixtures()));
    fakeCompetitionsApi.getCategories.mockReturnValue(
      of(
        categoriesResponseFixture({
          categories: [
            { id: 'cat-1', name: 'A', displayOrder: 0, styleCodes: [] },
            { id: 'cat-2', name: 'B', displayOrder: 1, styleCodes: ['18B'] },
          ],
        }),
      ),
    );
    const fixture = createComponent();

    fixture.componentInstance.onBulkAssignGroup(fixture.componentInstance.groupedCatalog()[0], '0');
    fixture.detectChanges();

    const rows = fixture.componentInstance.categories();
    expect(rows[0].styleCodes).toEqual(['18A', '18B', '18C']);
    expect(rows[1].styleCodes).toEqual([]);
  });

  it('clears the assignment for every style in the group when "Sin asignar" is chosen in bulk', () => {
    fakeCatalogApi.getStyles.mockReturnValue(of(groupStyleFixtures()));
    fakeCompetitionsApi.getCategories.mockReturnValue(
      of(
        categoriesResponseFixture({
          categories: [
            { id: 'cat-1', name: 'A', displayOrder: 0, styleCodes: ['18A'] },
            { id: 'cat-2', name: 'B', displayOrder: 1, styleCodes: ['18B', '18C'] },
          ],
        }),
      ),
    );
    const fixture = createComponent();

    fixture.componentInstance.onBulkAssignGroup(
      fixture.componentInstance.groupedCatalog()[0],
      'unassign',
    );
    fixture.detectChanges();

    const rows = fixture.componentInstance.categories();
    expect(rows[0].styleCodes).toEqual([]);
    expect(rows[1].styleCodes).toEqual([]);
  });

  it('resets the bulk-assign select back to the placeholder after firing', () => {
    fakeCatalogApi.getStyles.mockReturnValue(of(groupStyleFixtures()));
    fakeCompetitionsApi.getCategories.mockReturnValue(
      of(
        categoriesResponseFixture({
          categories: [{ id: 'cat-1', name: 'A', displayOrder: 0, styleCodes: [] }],
        }),
      ),
    );
    const fixture = createComponent();

    const bulkSelect = fixture.nativeElement.querySelector(
      '.style-group__bulk-select',
    ) as HTMLSelectElement;
    bulkSelect.value = '0';
    bulkSelect.dispatchEvent(new Event('change'));
    fixture.detectChanges();

    expect(bulkSelect.value).toBe('');
    expect(fixture.componentInstance.categories()[0].styleCodes).toEqual(['18A', '18B', '18C']);
  });

  it('disables "Continuar" until at least one category has at least one style', () => {
    fakeCatalogApi.getStyles.mockReturnValue(of([styleFixture()]));
    fakeCompetitionsApi.getCategories.mockReturnValue(
      of(
        categoriesResponseFixture({
          categories: [{ id: 'cat-1', name: 'A', displayOrder: 0, styleCodes: [] }],
        }),
      ),
    );
    const fixture = createComponent();

    const finishButton = fixture.nativeElement.querySelector(
      'button[type="submit"]',
    ) as HTMLButtonElement;
    expect(finishButton.disabled).toBe(true);

    fixture.componentInstance.onAssignStyle('18A', '0');
    fixture.detectChanges();

    expect(finishButton.disabled).toBe(false);
  });

  it('calls setCategories with the right payload and emits saved on success (step 3 is no longer terminal)', () => {
    fakeCatalogApi.getStyles.mockReturnValue(of([styleFixture()]));
    fakeCompetitionsApi.getCategories.mockReturnValue(
      of(
        categoriesResponseFixture({
          categories: [
            { id: 'cat-1', name: 'Estilos clásicos', displayOrder: 0, styleCodes: ['18A'] },
          ],
        }),
      ),
    );
    fakeCompetitionsApi.setCategories.mockReturnValue(
      of(
        categoriesResponseFixture({
          categories: [
            { id: 'cat-1', name: 'Estilos clásicos', displayOrder: 0, styleCodes: ['18A'] },
          ],
        }),
      ),
    );
    const fixture = createComponent();
    const router = TestBed.inject(Router);
    const navigateSpy = jest.spyOn(router, 'navigateByUrl');
    const emitted: void[] = [];
    fixture.componentInstance.saved.subscribe(() => emitted.push(undefined));

    fixture.componentInstance.onFinish();

    expect(fakeCompetitionsApi.setCategories).toHaveBeenCalledWith('c1', [
      { name: 'Estilos clásicos', displayOrder: 0, styleCodes: ['18A'] },
    ]);
    expect(emitted.length).toBe(1);
    expect(navigateSpy).not.toHaveBeenCalled();
  });

  it('opens a save-or-discard dialog instead of navigating immediately on "Volver al listado"', () => {
    const fixture = createComponent();

    const backButton = fixture.nativeElement.querySelector(
      '.back-to-list-link',
    ) as HTMLButtonElement;
    backButton.click();
    fixture.detectChanges();

    expect(fixture.nativeElement.querySelector('[role="alertdialog"]')).toBeTruthy();
  });

  it('discards without saving and navigates to the organizer dashboard', () => {
    const fixture = createComponent();
    const router = TestBed.inject(Router);
    const navigateSpy = jest.spyOn(router, 'navigateByUrl');

    fixture.componentInstance['onRequestBack']();
    fixture.componentInstance['onDiscardAndLeave']();
    fixture.detectChanges();

    expect(fakeCompetitionsApi.setCategories).not.toHaveBeenCalled();
    expect(navigateSpy).toHaveBeenCalledWith('/organizer/dashboard');
    expect(fixture.nativeElement.querySelector('[role="alertdialog"]')).toBeFalsy();
  });

  it('closes the dialog without navigating on "Cancelar"', () => {
    const fixture = createComponent();
    const router = TestBed.inject(Router);
    const navigateSpy = jest.spyOn(router, 'navigateByUrl');

    fixture.componentInstance['onRequestBack']();
    fixture.componentInstance['onCancelBackConfirm']();
    fixture.detectChanges();

    expect(navigateSpy).not.toHaveBeenCalled();
    expect(fixture.nativeElement.querySelector('[role="alertdialog"]')).toBeFalsy();
  });

  it('disables "Guardar borrador" in the confirm dialog while canFinish() is false', () => {
    const fixture = createComponent();

    fixture.componentInstance['onRequestBack']();
    fixture.detectChanges();

    const saveButton = fixture.nativeElement.querySelectorAll(
      '.modal-actions button',
    )[0] as HTMLButtonElement;
    expect(saveButton.disabled).toBe(true);
  });

  it('emits back when the "← Volver" button is clicked', () => {
    const fixture = createComponent();
    const emitted: void[] = [];
    fixture.componentInstance.back.subscribe(() => emitted.push(undefined));

    const buttons = Array.from(
      fixture.nativeElement.querySelectorAll('button'),
    ) as HTMLButtonElement[];
    const backButton = buttons.find(
      (button) =>
        button.textContent?.includes('Volver') && !button.className.includes('back-to-list-link'),
    );
    backButton?.click();

    expect(emitted.length).toBe(1);
  });
});
