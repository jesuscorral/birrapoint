import { TestBed } from '@angular/core/testing';
import { of, throwError } from 'rxjs';

import { ApiError } from '../../../core/api/api-error';
import { CatalogApiService } from '../../../core/api/catalog-api.service';
import type { StyleDetail } from '../../../core/api/catalog-api.service';
import { StyleReferencePanelComponent } from './style-reference-panel.component';

function styleDetailFixture(overrides: Partial<StyleDetail> = {}): StyleDetail {
  return {
    code: '21A',
    name: 'American IPA',
    categoryNumber: '21',
    categoryName: 'IPA',
    vitalStatistics: {
      ogLow: 1.056,
      ogHigh: 1.07,
      fgLow: 1.008,
      fgHigh: 1.014,
      ibuLow: 40,
      ibuHigh: 70,
      srmLow: 6,
      srmHigh: 14,
      abvLow: 5.5,
      abvHigh: 7.5,
    },
    description: {
      overallImpression: 'A decidedly hoppy and bitter, moderately strong American pale ale.',
      aroma: 'Hop aroma is medium to strong intensity.',
      appearance: 'Color ranges from medium gold to light reddish amber.',
      flavor: 'Similar to aroma; a hop-forward beer.',
      mouthfeel: 'Medium-light to medium body.',
      comments: 'Hop character should be evident.',
      history: 'An American development of the historical English style.',
      characteristicIngredients: 'Pale ale malt, American or New World hops.',
      styleComparison: 'Bigger, hoppier, and stronger than an American Pale Ale.',
      entryInstructions: null,
      commercialExamples: ['Russian River Blind Pig IPA'],
      tags: ['strong', 'hoppy'],
    },
    ...overrides,
  };
}

describe('StyleReferencePanelComponent', () => {
  let fakeCatalog: { getStyleDetail: jest.Mock };

  beforeEach(() => {
    fakeCatalog = { getStyleDetail: jest.fn().mockReturnValue(of(styleDetailFixture())) };
    TestBed.configureTestingModule({
      providers: [{ provide: CatalogApiService, useValue: fakeCatalog }],
    });
  });

  function createComponent(styleCode = '21A', styleName = 'American IPA') {
    const fixture = TestBed.createComponent(StyleReferencePanelComponent);
    fixture.componentRef.setInput('styleCode', styleCode);
    fixture.componentRef.setInput('styleName', styleName);
    fixture.detectChanges();
    return fixture;
  }

  it('starts collapsed with a real, keyboard-accessible toggle button', () => {
    const fixture = createComponent();

    const button = fixture.nativeElement.querySelector('button') as HTMLButtonElement;
    expect(button).not.toBeNull();
    expect(button.getAttribute('aria-expanded')).toBe('false');
    expect(fakeCatalog.getStyleDetail).not.toHaveBeenCalled();
    expect(fixture.nativeElement.textContent).not.toContain('Hop aroma is medium to strong');
  });

  it('fetches and renders the style detail on first expand', () => {
    const fixture = createComponent();

    (fixture.nativeElement.querySelector('button') as HTMLButtonElement).click();
    fixture.detectChanges();

    expect(fakeCatalog.getStyleDetail).toHaveBeenCalledWith('21A');
    const button = fixture.nativeElement.querySelector('button') as HTMLButtonElement;
    expect(button.getAttribute('aria-expanded')).toBe('true');
    const text = fixture.nativeElement.textContent as string;
    expect(text).toContain('Hop aroma is medium to strong intensity.');
    expect(text).toContain('Russian River Blind Pig IPA');
    expect(text).toContain('1.056');
  });

  it('does not refetch on a later collapse/expand cycle', () => {
    const fixture = createComponent();
    const button = fixture.nativeElement.querySelector('button') as HTMLButtonElement;

    button.click();
    fixture.detectChanges();
    button.click(); // collapse
    fixture.detectChanges();
    expect(button.getAttribute('aria-expanded')).toBe('false');

    button.click(); // expand again
    fixture.detectChanges();

    expect(fakeCatalog.getStyleDetail).toHaveBeenCalledTimes(1);
    expect(button.getAttribute('aria-expanded')).toBe('true');
  });

  it('shows an error message if the style detail fails to load, and lets a retry succeed', () => {
    fakeCatalog.getStyleDetail.mockReturnValueOnce(
      throwError(() => new ApiError({ status: 404, title: 'Not found', urn: null })),
    );
    const fixture = createComponent();
    const button = fixture.nativeElement.querySelector('button') as HTMLButtonElement;

    button.click();
    fixture.detectChanges();

    expect(fixture.nativeElement.textContent).toContain('Not found');

    fakeCatalog.getStyleDetail.mockReturnValue(of(styleDetailFixture()));
    button.click(); // collapse
    fixture.detectChanges();
    button.click(); // expand — retries since the previous attempt never cached a detail
    fixture.detectChanges();

    expect(fixture.nativeElement.textContent).toContain('Hop aroma is medium to strong intensity.');
  });
});
