import { provideRouter } from '@angular/router';
import { TestBed } from '@angular/core/testing';
import { of, throwError } from 'rxjs';

import { ApiError } from '../../core/api/api-error';
import { OrganizerDashboardComponent } from './organizer-dashboard.component';
import { CompetitionsApiService } from '../competition-wizard/competitions-api.service';
import type { CompetitionSummary } from '../competition-wizard/competitions-api.service';

function competitionFixture(overrides: Partial<CompetitionSummary> = {}): CompetitionSummary {
  return {
    id: 'c1',
    name: 'Golden Ale Cup',
    venue: 'Town Hall',
    startDate: '2026-08-01',
    endDate: '2026-08-02',
    state: 'Draft',
    ...overrides,
  };
}

describe('OrganizerDashboardComponent', () => {
  let fakeApi: { list: jest.Mock };

  beforeEach(() => {
    fakeApi = { list: jest.fn().mockReturnValue(of([competitionFixture()])) };
    TestBed.configureTestingModule({
      providers: [{ provide: CompetitionsApiService, useValue: fakeApi }, provideRouter([])],
    });
  });

  function createComponent() {
    const fixture = TestBed.createComponent(OrganizerDashboardComponent);
    fixture.detectChanges();
    return fixture;
  }

  it('loads and renders each competition with its name, venue, dates, and state', () => {
    const fixture = createComponent();

    expect(fakeApi.list).toHaveBeenCalled();
    const text = fixture.nativeElement.textContent as string;
    expect(text).toContain('Golden Ale Cup');
    expect(text).toContain('Town Hall');
    expect(text).toContain('2026-08-01');
    expect(text).toContain('2026-08-02');
    expect(text).toContain('Draft');
  });

  it('links a Draft competition to the setup wizard', () => {
    const fixture = createComponent();

    const link = fixture.nativeElement.querySelector('a[href="/organizer/competitions/c1"]');
    expect(link).not.toBeNull();
  });

  it.each(['Active', 'InEvaluation', 'Finalized'] as const)(
    'links a %s competition to the tables screen',
    (state) => {
      fakeApi.list.mockReturnValue(of([competitionFixture({ state })]));
      const fixture = createComponent();

      const link = fixture.nativeElement.querySelector(
        'a[href="/organizer/competitions/c1/tables"]',
      );
      expect(link).not.toBeNull();
    },
  );

  it('shows an empty state with a create action when the organizer has no competitions', () => {
    fakeApi.list.mockReturnValue(of([]));
    const fixture = createComponent();

    const text = fixture.nativeElement.textContent as string;
    expect(text).toContain('No competitions yet');
    const link = fixture.nativeElement.querySelector('a[href="/organizer/competitions/new"]');
    expect(link).not.toBeNull();
  });

  it('surfaces an error message when loading competitions fails', () => {
    fakeApi.list.mockReturnValue(
      throwError(
        () => new ApiError({ status: 500, title: 'An unexpected error occurred.', urn: null }),
      ),
    );
    const fixture = createComponent();

    const alert = fixture.nativeElement.querySelector('[role="alert"]');
    expect(alert?.textContent).toContain('An unexpected error occurred.');
  });

  it('always shows a "New competition" action, whether or not there are competitions', () => {
    const fixture = createComponent();

    const link = fixture.nativeElement.querySelector('a[href="/organizer/competitions/new"]');
    expect(link).not.toBeNull();
    expect(link.textContent).toContain('New competition');
  });
});
