import { TestBed } from '@angular/core/testing';
import { ActivatedRoute } from '@angular/router';
import { of } from 'rxjs';

import { AuthPlaceholderComponent } from './auth-placeholder.component';

describe('AuthPlaceholderComponent', () => {
  it('renders the label supplied via route data', () => {
    TestBed.configureTestingModule({
      imports: [AuthPlaceholderComponent],
      providers: [{ provide: ActivatedRoute, useValue: { data: of({ label: 'Organizer' }) } }],
    });

    const fixture = TestBed.createComponent(AuthPlaceholderComponent);
    fixture.detectChanges();

    expect(fixture.nativeElement.textContent).toContain('Organizer');
  });
});
