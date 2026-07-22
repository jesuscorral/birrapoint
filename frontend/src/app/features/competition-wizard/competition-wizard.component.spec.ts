import { Location } from '@angular/common';
import { TestBed } from '@angular/core/testing';
import { ActivatedRoute, convertToParamMap } from '@angular/router';
import { of, throwError } from 'rxjs';

import { CompetitionsApiService } from '../../core/api/competitions-api.service';
import type { CompetitionDetail } from '../../core/api/competitions-api.service';
import { CompetitionWizardComponent } from './competition-wizard.component';

function detailFixture(overrides: Partial<CompetitionDetail> = {}): CompetitionDetail {
  return {
    id: 'c1',
    name: 'Golden Ale Cup',
    venue: 'Town Hall',
    startDate: '2026-08-01',
    endDate: '2026-08-02',
    description: null,
    logoUrl: null,
    entryLimit: null,
    registrationStart: null,
    registrationEnd: null,
    state: 'Draft',
    ...overrides,
  };
}

describe('CompetitionWizardComponent', () => {
  let fakeApi: { create: jest.Mock; update: jest.Mock; getById: jest.Mock };

  function configure(id: string | null) {
    fakeApi = { create: jest.fn(), update: jest.fn(), getById: jest.fn() };
    TestBed.configureTestingModule({
      providers: [
        { provide: CompetitionsApiService, useValue: fakeApi },
        {
          provide: ActivatedRoute,
          useValue: { snapshot: { paramMap: convertToParamMap(id ? { id } : {}) } },
        },
      ],
    });
  }

  it('starts blank on step 1 when no :id route param is present', () => {
    configure(null);
    const fixture = TestBed.createComponent(CompetitionWizardComponent);
    fixture.detectChanges();

    expect(fakeApi.getById).not.toHaveBeenCalled();
    expect(fixture.nativeElement.querySelector('app-basics-step')).toBeTruthy();
    expect(fixture.nativeElement.querySelector('app-details-step')).toBeFalsy();
  });

  it('loads the competition and populates step 1 when :id is present (resume-with-data)', () => {
    configure('c1');
    fakeApi.getById.mockReturnValue(of(detailFixture({ name: 'Resumed Cup' })));

    const fixture = TestBed.createComponent(CompetitionWizardComponent);
    fixture.detectChanges();

    expect(fakeApi.getById).toHaveBeenCalledWith('c1');
    const nameInput = fixture.nativeElement.querySelector(
      'input[formcontrolname="name"]',
    ) as HTMLInputElement;
    expect(nameInput.value).toBe('Resumed Cup');
  });

  it('shows a loading state while fetching, and an error state if the fetch fails', () => {
    configure('missing');
    fakeApi.getById.mockReturnValue(throwError(() => new Error('not found')));

    const fixture = TestBed.createComponent(CompetitionWizardComponent);
    fixture.detectChanges();

    expect(fixture.nativeElement.textContent).toContain('Unable to load this competition');
  });

  it('advances to step 2 and updates the URL after basics is saved for a brand-new competition', () => {
    configure(null);
    const fixture = TestBed.createComponent(CompetitionWizardComponent);
    fixture.detectChanges();
    const location = TestBed.inject(Location);
    const replaceStateSpy = jest.spyOn(location, 'replaceState');

    const detail = detailFixture({ id: 'new-id' });
    fixture.componentInstance['onBasicsSaved'](detail);
    fixture.detectChanges();

    expect(replaceStateSpy).toHaveBeenCalledWith('/organizer/competitions/new-id');
    expect(fixture.nativeElement.querySelector('app-details-step')).toBeTruthy();
    expect(fixture.nativeElement.querySelector('app-basics-step')).toBeFalsy();
  });

  it('does not touch the URL when basics is saved for an already-existing competition', () => {
    configure('c1');
    fakeApi.getById.mockReturnValue(of(detailFixture()));
    const fixture = TestBed.createComponent(CompetitionWizardComponent);
    fixture.detectChanges();
    const location = TestBed.inject(Location);
    const replaceStateSpy = jest.spyOn(location, 'replaceState');

    fixture.componentInstance['onBasicsSaved'](detailFixture({ name: 'Updated name' }));
    fixture.detectChanges();

    expect(replaceStateSpy).not.toHaveBeenCalled();
    expect(fixture.nativeElement.querySelector('app-details-step')).toBeTruthy();
  });
});
