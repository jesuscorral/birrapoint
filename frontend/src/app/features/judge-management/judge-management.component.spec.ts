import { TestBed } from '@angular/core/testing';
import { ActivatedRoute, convertToParamMap } from '@angular/router';
import { of, throwError } from 'rxjs';

import { ApiError } from '../../core/api/api-error';
import { JudgeManagementApiService } from './judge-management-api.service';
import type { JudgeProfile, RegisterJudgesResult } from './judge-management-api.service';
import { JudgeManagementComponent } from './judge-management.component';

function judgesFixture(): JudgeProfile[] {
  return [
    {
      id: 'j1',
      email: 'ada@example.com',
      displayName: 'Ada Lovelace',
      invitationStatus: 'Sent',
      attempts: 1,
      lastError: null,
      sentAt: '2026-07-20T10:00:00Z',
    },
    {
      id: 'j2',
      email: 'grace@example.com',
      displayName: 'Grace Hopper',
      invitationStatus: 'Failed',
      attempts: 3,
      lastError: 'SMTP timeout',
      sentAt: null,
    },
  ];
}

function buttonWithText(root: Element, text: string): HTMLButtonElement {
  const buttons = [...root.querySelectorAll('button')] as HTMLButtonElement[];
  const match = buttons.find((button) => button.textContent?.trim() === text);
  if (!match) {
    throw new Error(`No button with text "${text}" found`);
  }
  return match;
}

describe('JudgeManagementComponent', () => {
  let fakeApi: {
    registerJudges: jest.Mock;
    getJudges: jest.Mock;
    updateJudgeEmail: jest.Mock;
    resendInvitation: jest.Mock;
  };

  beforeEach(() => {
    fakeApi = {
      registerJudges: jest.fn(),
      getJudges: jest.fn().mockReturnValue(of([])),
      updateJudgeEmail: jest.fn(),
      resendInvitation: jest.fn(),
    };
    TestBed.configureTestingModule({
      providers: [
        { provide: JudgeManagementApiService, useValue: fakeApi },
        {
          provide: ActivatedRoute,
          useValue: { snapshot: { paramMap: convertToParamMap({ id: 'c1' }) } },
        },
      ],
    });
  });

  function createComponent() {
    const fixture = TestBed.createComponent(JudgeManagementComponent);
    fixture.detectChanges();
    return fixture;
  }

  it('loads the delivery status list on init', () => {
    fakeApi.getJudges.mockReturnValue(of(judgesFixture()));
    const fixture = createComponent();

    expect(fakeApi.getJudges).toHaveBeenCalledWith('c1');
    const text = fixture.nativeElement.textContent as string;
    expect(text).toContain('ada@example.com');
    expect(text).toContain('grace@example.com');
    expect(text).toContain('Sent');
    expect(text).toContain('Failed');
    expect(text).toContain('SMTP timeout');
  });

  it('marks each row with a data-judge-email attribute', () => {
    fakeApi.getJudges.mockReturnValue(of(judgesFixture()));
    const fixture = createComponent();

    const row = fixture.nativeElement.querySelector('tr[data-judge-email="ada@example.com"]');
    expect(row).not.toBeNull();
  });

  it('registers judges from the pasted email list and shows created/skipped, then refreshes the list', () => {
    const fixture = createComponent();
    const textarea = fixture.nativeElement.querySelector('textarea') as HTMLTextAreaElement;
    textarea.value = 'ada@example.com\ngrace@example.com';
    textarea.dispatchEvent(new Event('input'));
    fixture.detectChanges();

    const registerResult: RegisterJudgesResult = {
      created: [{ id: 'j1', email: 'ada@example.com' }],
      skipped: [{ email: 'grace@example.com', reason: 'already-registered' }],
    };
    fakeApi.registerJudges.mockReturnValue(of(registerResult));
    fakeApi.getJudges.mockReturnValue(of(judgesFixture()));

    buttonWithText(fixture.nativeElement, 'Register judges').click();
    fixture.detectChanges();

    expect(fakeApi.registerJudges).toHaveBeenCalledWith('c1', [
      'ada@example.com',
      'grace@example.com',
    ]);
    const text = fixture.nativeElement.textContent as string;
    expect(text).toContain('ada@example.com');
    expect(text).toContain('already registered');
    // list refreshed after registration, not just showing the immediate POST response
    expect(fakeApi.getJudges).toHaveBeenCalledTimes(2);
  });

  it('splits pasted emails on commas and blank lines, ignoring empty entries', () => {
    const fixture = createComponent();
    const textarea = fixture.nativeElement.querySelector('textarea') as HTMLTextAreaElement;
    textarea.value = 'ada@example.com, grace@example.com\n\n  katherine@example.com  ';
    textarea.dispatchEvent(new Event('input'));
    fixture.detectChanges();

    fakeApi.registerJudges.mockReturnValue(of({ created: [], skipped: [] }));
    buttonWithText(fixture.nativeElement, 'Register judges').click();

    expect(fakeApi.registerJudges).toHaveBeenCalledWith('c1', [
      'ada@example.com',
      'grace@example.com',
      'katherine@example.com',
    ]);
  });

  it('surfaces the server error message when registration fails', () => {
    const fixture = createComponent();
    const textarea = fixture.nativeElement.querySelector('textarea') as HTMLTextAreaElement;
    textarea.value = 'ada@example.com';
    textarea.dispatchEvent(new Event('input'));
    fixture.detectChanges();

    fakeApi.registerJudges.mockReturnValue(
      throwError(
        () =>
          new ApiError({
            status: 400,
            title: 'Invalid request',
            urn: 'urn:birrapoint:validation',
            detail: 'ada@example.com is not a valid email address.',
          }),
      ),
    );
    buttonWithText(fixture.nativeElement, 'Register judges').click();
    fixture.detectChanges();

    expect(fixture.nativeElement.textContent).toContain(
      'ada@example.com is not a valid email address.',
    );
  });

  it('resends an invitation and refreshes the list', () => {
    fakeApi.getJudges.mockReturnValue(of(judgesFixture()));
    const fixture = createComponent();

    fakeApi.resendInvitation.mockReturnValue(of({ status: 'Pending' }));
    const row = fixture.nativeElement.querySelector(
      'tr[data-judge-email="grace@example.com"]',
    ) as Element;
    buttonWithText(row, 'Resend invitation').click();
    fixture.detectChanges();

    expect(fakeApi.resendInvitation).toHaveBeenCalledWith('c1', 'j2');
    expect(fakeApi.getJudges).toHaveBeenCalledTimes(2);
  });

  it('edits a judge email and refreshes the list on success', () => {
    fakeApi.getJudges.mockReturnValue(of(judgesFixture()));
    const fixture = createComponent();

    const row = fixture.nativeElement.querySelector(
      'tr[data-judge-email="ada@example.com"]',
    ) as Element;
    buttonWithText(row, 'Edit email').click();
    fixture.detectChanges();

    const input = row.querySelector('input[type="email"]') as HTMLInputElement;
    input.value = 'ada2@example.com';
    input.dispatchEvent(new Event('input'));
    fixture.detectChanges();

    fakeApi.updateJudgeEmail.mockReturnValue(
      of({ ...judgesFixture()[0], email: 'ada2@example.com' }),
    );
    fakeApi.getJudges.mockReturnValue(
      of([{ ...judgesFixture()[0], email: 'ada2@example.com' }, judgesFixture()[1]]),
    );
    buttonWithText(row, 'Save').click();
    fixture.detectChanges();

    expect(fakeApi.updateJudgeEmail).toHaveBeenCalledWith('c1', 'j1', 'ada2@example.com');
    expect(fakeApi.getJudges).toHaveBeenCalledTimes(2);
  });

  it('surfaces the judge-already-active 409 clearly when editing after first login', () => {
    fakeApi.getJudges.mockReturnValue(of(judgesFixture()));
    const fixture = createComponent();

    const row = fixture.nativeElement.querySelector(
      'tr[data-judge-email="ada@example.com"]',
    ) as Element;
    buttonWithText(row, 'Edit email').click();
    fixture.detectChanges();

    const input = row.querySelector('input[type="email"]') as HTMLInputElement;
    input.value = 'ada2@example.com';
    input.dispatchEvent(new Event('input'));
    fixture.detectChanges();

    fakeApi.updateJudgeEmail.mockReturnValue(
      throwError(
        () =>
          new ApiError({
            status: 409,
            title: 'Judge already active',
            urn: 'urn:birrapoint:judge-already-active',
            detail: 'This judge has already logged in and can no longer be corrected here.',
          }),
      ),
    );
    buttonWithText(row, 'Save').click();
    fixture.detectChanges();

    expect(fixture.nativeElement.textContent).toContain(
      'This judge has already logged in and can no longer be corrected here.',
    );
  });
});
