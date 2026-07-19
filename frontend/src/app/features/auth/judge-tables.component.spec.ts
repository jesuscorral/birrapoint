import { TestBed } from '@angular/core/testing';

import { JudgeTablesComponent } from './judge-tables.component';

describe('JudgeTablesComponent', () => {
  it('renders a placeholder landing for the judge tables workspace', () => {
    TestBed.configureTestingModule({ imports: [JudgeTablesComponent] });

    const fixture = TestBed.createComponent(JudgeTablesComponent);
    fixture.detectChanges();

    expect(fixture.nativeElement.textContent).toContain('Judge');
  });
});
