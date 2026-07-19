import { TestBed } from '@angular/core/testing';

import { OrganizerDashboardComponent } from './organizer-dashboard.component';

describe('OrganizerDashboardComponent', () => {
  it('renders a placeholder landing for the organizer workspace', () => {
    TestBed.configureTestingModule({ imports: [OrganizerDashboardComponent] });

    const fixture = TestBed.createComponent(OrganizerDashboardComponent);
    fixture.detectChanges();

    expect(fixture.nativeElement.textContent).toContain('Organizer');
  });
});
