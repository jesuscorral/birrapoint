import { provideRouter } from '@angular/router';
import type { ComponentFixture } from '@angular/core/testing';
import { TestBed } from '@angular/core/testing';
import { of, throwError } from 'rxjs';

import { ApiError } from '../../core/api/api-error';
import { OrganizerDashboardComponent } from './organizer-dashboard.component';
import { CompetitionsApiService } from '../../core/api/competitions-api.service';
import type { CompetitionSummary } from '../../core/api/competitions-api.service';

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

function findButtonByText(root: Element, text: string): HTMLButtonElement {
  const buttons = Array.from(root.querySelectorAll('button')) as HTMLButtonElement[];
  const button = buttons.find((candidate) => candidate.textContent?.trim() === text);
  if (!button) {
    throw new Error(`Button with text "${text}" not found`);
  }
  return button;
}

function clickAdvance(fixture: ComponentFixture<OrganizerDashboardComponent>, label: string): void {
  findButtonByText(fixture.nativeElement as Element, label).click();
  fixture.detectChanges();
}

function clickConfirm(fixture: ComponentFixture<OrganizerDashboardComponent>): void {
  findButtonByText(fixture.nativeElement as Element, 'Confirm').click();
  fixture.detectChanges();
}

describe('OrganizerDashboardComponent', () => {
  let fakeApi: { list: jest.Mock; changeState: jest.Mock };

  beforeEach(() => {
    fakeApi = {
      list: jest.fn().mockReturnValue(of([competitionFixture()])),
      changeState: jest.fn().mockReturnValue(of({ state: 'Active' })),
    };
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

  it('links an Active competition to the tables screen', () => {
    fakeApi.list.mockReturnValue(of([competitionFixture({ state: 'Active' })]));
    const fixture = createComponent();

    const link = fixture.nativeElement.querySelector('a[href="/organizer/competitions/c1/tables"]');
    expect(link).not.toBeNull();
  });

  it.each(['InEvaluation', 'Finalized'] as const)(
    'links a %s competition to the live monitoring dashboard',
    (state) => {
      fakeApi.list.mockReturnValue(of([competitionFixture({ state })]));
      const fixture = createComponent();

      const link = fixture.nativeElement.querySelector(
        'a[href="/organizer/competitions/c1/monitor"]',
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

  describe('advance-state action (FR-051)', () => {
    it.each([
      ['Draft', 'Activate'],
      ['Active', 'Start evaluation'],
      ['InEvaluation', 'Finalize'],
    ] as const)('shows a "%s" state as "%s"', (state, label) => {
      fakeApi.list.mockReturnValue(of([competitionFixture({ state })]));
      const fixture = createComponent();

      expect(() => findButtonByText(fixture.nativeElement as Element, label)).not.toThrow();
    });

    it('renders no advance-state control for a Finalized competition', () => {
      fakeApi.list.mockReturnValue(of([competitionFixture({ state: 'Finalized' })]));
      const fixture = createComponent();

      const buttons = Array.from(
        (fixture.nativeElement as Element).querySelectorAll('button'),
      ) as HTMLButtonElement[];
      const advanceLabels = ['Activate', 'Start evaluation', 'Finalize'];
      expect(
        buttons.some((button) => advanceLabels.includes(button.textContent?.trim() ?? '')),
      ).toBe(false);
    });

    it('renders the navigation link and the advance control as independent siblings, not nested', () => {
      const fixture = createComponent();

      const link = fixture.nativeElement.querySelector('a.competition-list-item');
      expect(link.querySelector('button')).toBeNull();

      const advanceButton = findButtonByText(fixture.nativeElement as Element, 'Activate');
      expect(advanceButton.closest('a')).toBeNull();
    });

    it('requires an explicit confirm step before calling the API', () => {
      const fixture = createComponent();

      clickAdvance(fixture, 'Activate');

      expect(fakeApi.changeState).not.toHaveBeenCalled();
      const dialog = fixture.nativeElement.querySelector('[role="alertdialog"]');
      expect(dialog).not.toBeNull();

      clickConfirm(fixture);

      expect(fakeApi.changeState).toHaveBeenCalledWith('c1', 'Active');
    });

    it('can be cancelled without calling the API', () => {
      const fixture = createComponent();

      clickAdvance(fixture, 'Activate');
      findButtonByText(fixture.nativeElement as Element, 'Cancel').click();
      fixture.detectChanges();

      expect(fakeApi.changeState).not.toHaveBeenCalled();
      expect(fixture.nativeElement.querySelector('[role="alertdialog"]')).toBeNull();
    });

    it('refetches the competitions list after a successful advance', () => {
      const fixture = createComponent();
      expect(fakeApi.list).toHaveBeenCalledTimes(1);

      clickAdvance(fixture, 'Activate');
      clickConfirm(fixture);

      expect(fakeApi.list).toHaveBeenCalledTimes(2);
    });

    it('announces the successful advance via a status region', () => {
      const fixture = createComponent();

      clickAdvance(fixture, 'Activate');
      clickConfirm(fixture);

      const status = fixture.nativeElement.querySelector('[role="status"]');
      expect(status?.textContent).toContain('Active');
    });

    it('names how many tables are blocking on 409 tables-still-open', () => {
      fakeApi.list.mockReturnValue(of([competitionFixture({ state: 'InEvaluation' })]));
      fakeApi.changeState.mockReturnValue(
        throwError(
          () =>
            new ApiError({
              status: 409,
              title: 'Tables still open',
              urn: 'urn:birrapoint:tables-still-open',
              extensions: { openTableIds: ['t1', 't2'] },
            }),
        ),
      );
      const fixture = createComponent();

      clickAdvance(fixture, 'Finalize');
      clickConfirm(fixture);

      const alert = fixture.nativeElement.querySelector('[role="alert"]');
      expect(alert?.textContent).toContain('2 table(s) still open');
      expect(fixture.nativeElement.querySelector('[role="alertdialog"]')).toBeNull();
    });

    it('surfaces a plain message and refetches the list on 409 invalid-state-transition', () => {
      fakeApi.changeState.mockReturnValue(
        throwError(
          () =>
            new ApiError({
              status: 409,
              title: 'Invalid state transition',
              urn: 'urn:birrapoint:invalid-state-transition',
            }),
        ),
      );
      const fixture = createComponent();
      expect(fakeApi.list).toHaveBeenCalledTimes(1);

      clickAdvance(fixture, 'Activate');
      clickConfirm(fixture);

      const alert = fixture.nativeElement.querySelector('[role="alert"]');
      expect(alert?.textContent).toContain('already changed');
      expect(fakeApi.list).toHaveBeenCalledTimes(2);
    });
  });
});
