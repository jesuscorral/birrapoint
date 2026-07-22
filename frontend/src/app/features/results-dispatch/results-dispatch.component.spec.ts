import { ActivatedRoute, convertToParamMap } from '@angular/router';
import { signal } from '@angular/core';
import type { WritableSignal } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { HubConnectionState } from '@microsoft/signalr';
import { of, Subject, throwError } from 'rxjs';

import { ApiError } from '../../core/api/api-error';
import { DispatchApiService } from '../../core/api/dispatch-api.service';
import type { DispatchStatusRow, ResultsArchiveResult } from '../../core/api/dispatch-api.service';
import { CompetitionHubService } from '../../core/realtime/competition-hub.service';
import type { DispatchProgressEvent } from '../../core/realtime/competition-hub.events';
import { ResultsDispatchComponent } from './results-dispatch.component';

function rowsFixture(overrides: Partial<DispatchStatusRow>[] = []): DispatchStatusRow[] {
  const base: DispatchStatusRow[] = [
    {
      participantId: 'p1',
      email: 'ada@example.com',
      status: 'Completed',
      attempts: 1,
      lastError: null,
    },
    {
      participantId: 'p2',
      email: 'grace@example.com',
      status: 'Failed',
      attempts: 3,
      lastError: 'SMTP timeout',
    },
  ];
  return overrides.length > 0 ? (overrides as DispatchStatusRow[]) : base;
}

describe('ResultsDispatchComponent', () => {
  let fakeDispatchApi: {
    getDispatchStatus: jest.Mock;
    retryDispatch: jest.Mock;
    downloadResultsArchive: jest.Mock;
  };
  let fakeHub: {
    start: jest.Mock;
    joinCompetitionAsOrganizer: jest.Mock;
    leaveCompetition: jest.Mock;
    on: jest.Mock;
    state: WritableSignal<HubConnectionState>;
  };
  let dispatchProgressSubject: Subject<DispatchProgressEvent>;
  let createObjectURLMock: jest.Mock;
  let revokeObjectURLMock: jest.Mock;
  let clickSpy: jest.SpyInstance;

  beforeEach(() => {
    dispatchProgressSubject = new Subject<DispatchProgressEvent>();

    fakeDispatchApi = {
      getDispatchStatus: jest.fn().mockReturnValue(of(rowsFixture())),
      retryDispatch: jest.fn().mockReturnValue(of(undefined)),
      downloadResultsArchive: jest
        .fn()
        .mockReturnValue(of<ResultsArchiveResult>({ ready: true, blob: new Blob(['zip']) })),
    };
    fakeHub = {
      start: jest.fn().mockResolvedValue(undefined),
      joinCompetitionAsOrganizer: jest.fn().mockResolvedValue(undefined),
      leaveCompetition: jest.fn().mockResolvedValue(undefined),
      on: jest.fn(() => dispatchProgressSubject.asObservable()),
      state: signal(HubConnectionState.Connected),
    };

    createObjectURLMock = jest.fn().mockReturnValue('blob:fake-url');
    revokeObjectURLMock = jest.fn();
    Object.defineProperty(URL, 'createObjectURL', {
      value: createObjectURLMock,
      writable: true,
    });
    Object.defineProperty(URL, 'revokeObjectURL', {
      value: revokeObjectURLMock,
      writable: true,
    });
    clickSpy = jest.spyOn(HTMLAnchorElement.prototype, 'click').mockImplementation(() => undefined);

    TestBed.configureTestingModule({
      providers: [
        { provide: DispatchApiService, useValue: fakeDispatchApi },
        { provide: CompetitionHubService, useValue: fakeHub },
        {
          provide: ActivatedRoute,
          useValue: { snapshot: { paramMap: convertToParamMap({ id: 'c1' }) } },
        },
      ],
    });
  });

  afterEach(() => {
    clickSpy.mockRestore();
  });

  function createComponent() {
    const fixture = TestBed.createComponent(ResultsDispatchComponent);
    fixture.detectChanges();
    return fixture;
  }

  async function flush() {
    await Promise.resolve();
    await Promise.resolve();
  }

  function buttonWithText(root: Element, text: string): HTMLButtonElement | null {
    const buttons = [...root.querySelectorAll('button')] as HTMLButtonElement[];
    return buttons.find((button) => button.textContent?.trim() === text) ?? null;
  }

  it('loads and renders the per-participant dispatch status table', async () => {
    const fixture = createComponent();
    await flush();
    fixture.detectChanges();

    expect(fakeDispatchApi.getDispatchStatus).toHaveBeenCalledWith('c1');
    const text = fixture.nativeElement.textContent as string;
    expect(text).toContain('ada@example.com');
    expect(text).toContain('Completed');
    expect(text).toContain('grace@example.com');
    expect(text).toContain('Failed');
    expect(text).toContain('SMTP timeout');
  });

  it('surfaces a load error message when the dispatch status request fails', async () => {
    fakeDispatchApi.getDispatchStatus.mockReturnValue(
      throwError(
        () => new ApiError({ status: 500, title: 'An unexpected error occurred.', urn: null }),
      ),
    );
    const fixture = createComponent();
    await flush();
    fixture.detectChanges();

    const alert = fixture.nativeElement.querySelector('[role="alert"]');
    expect(alert?.textContent).toContain('An unexpected error occurred.');
  });

  it('joins the competition SignalR group on init and leaves it on destroy', async () => {
    const fixture = createComponent();
    await flush();

    expect(fakeHub.start).toHaveBeenCalled();
    expect(fakeHub.joinCompetitionAsOrganizer).toHaveBeenCalledWith('c1');

    fixture.destroy();
    await flush();

    expect(fakeHub.leaveCompetition).toHaveBeenCalledWith('c1');
  });

  it('shows a live pipeline-stage indicator from a DispatchProgress event', async () => {
    const fixture = createComponent();
    await flush();
    fixture.detectChanges();

    dispatchProgressSubject.next({ jobType: 'GeneratePdfs', status: 'Running' });
    fixture.detectChanges();

    expect(fixture.nativeElement.textContent).toContain('Generating PDFs');
    expect(fixture.nativeElement.textContent).toContain('Running');
  });

  it('only shows a Retry button for Failed rows', async () => {
    const fixture = createComponent();
    await flush();
    fixture.detectChanges();

    const completedRow = fixture.nativeElement.querySelector('[data-participant-id="p1"]');
    const failedRow = fixture.nativeElement.querySelector('[data-participant-id="p2"]');

    expect(buttonWithText(completedRow, 'Retry')).toBeNull();
    expect(buttonWithText(failedRow, 'Retry')).not.toBeNull();
  });

  it('retries a single failed participant and refreshes the list', async () => {
    const fixture = createComponent();
    await flush();
    fixture.detectChanges();
    fakeDispatchApi.getDispatchStatus.mockClear();

    const failedRow = fixture.nativeElement.querySelector('[data-participant-id="p2"]');
    buttonWithText(failedRow, 'Retry')!.click();
    fixture.detectChanges();
    await flush();
    fixture.detectChanges();

    expect(fakeDispatchApi.retryDispatch).toHaveBeenCalledWith('c1', ['p2']);
    expect(fakeDispatchApi.getDispatchStatus).toHaveBeenCalledWith('c1');
  });

  it('surfaces a retry error without crashing', async () => {
    fakeDispatchApi.retryDispatch.mockReturnValue(
      throwError(
        () => new ApiError({ status: 500, title: 'An unexpected error occurred.', urn: null }),
      ),
    );
    const fixture = createComponent();
    await flush();
    fixture.detectChanges();

    const failedRow = fixture.nativeElement.querySelector('[data-participant-id="p2"]');
    buttonWithText(failedRow, 'Retry')!.click();
    fixture.detectChanges();
    await flush();
    fixture.detectChanges();

    const alert = fixture.nativeElement.querySelector('[role="alert"]');
    expect(alert?.textContent).toContain('An unexpected error occurred.');
  });

  it('offers a "Retry all failed" bulk action when more than one row failed', async () => {
    fakeDispatchApi.getDispatchStatus.mockReturnValue(
      of(
        rowsFixture([
          { participantId: 'p1', email: 'a@x.com', status: 'Failed', attempts: 3, lastError: 'x' },
          { participantId: 'p2', email: 'b@x.com', status: 'Failed', attempts: 3, lastError: 'y' },
        ]),
      ),
    );
    const fixture = createComponent();
    await flush();
    fixture.detectChanges();

    const bulkButton = buttonWithText(fixture.nativeElement, 'Retry all failed');
    expect(bulkButton).not.toBeNull();
    bulkButton!.click();
    fixture.detectChanges();
    await flush();

    expect(fakeDispatchApi.retryDispatch).toHaveBeenCalledWith('c1', ['p1', 'p2']);
  });

  it('does not offer the bulk retry action when zero or one rows failed', async () => {
    const fixture = createComponent();
    await flush();
    fixture.detectChanges();

    expect(buttonWithText(fixture.nativeElement, 'Retry all failed')).toBeNull();
  });

  it('disables the download action until the archive is ready', async () => {
    fakeDispatchApi.getDispatchStatus.mockReturnValue(
      of([
        { participantId: 'p1', email: 'a@x.com', status: 'Pending', attempts: 0, lastError: null },
      ]),
    );
    const fixture = createComponent();
    await flush();
    fixture.detectChanges();

    const downloadButton = buttonWithText(fixture.nativeElement, 'Download results ZIP');
    expect(downloadButton!.disabled).toBe(true);
  });

  it('enables downloading once a live BundleZip Completed event arrives', async () => {
    fakeDispatchApi.getDispatchStatus.mockReturnValue(
      of([
        { participantId: 'p1', email: 'a@x.com', status: 'Pending', attempts: 0, lastError: null },
      ]),
    );
    const fixture = createComponent();
    await flush();
    fixture.detectChanges();

    dispatchProgressSubject.next({ jobType: 'BundleZip', status: 'Completed' });
    fixture.detectChanges();

    const downloadButton = buttonWithText(fixture.nativeElement, 'Download results ZIP');
    expect(downloadButton!.disabled).toBe(false);
  });

  it('treats every row reaching a terminal status as a fallback readiness signal', async () => {
    const fixture = createComponent();
    await flush();
    fixture.detectChanges();

    const downloadButton = buttonWithText(fixture.nativeElement, 'Download results ZIP');
    expect(downloadButton!.disabled).toBe(false);
  });

  it('triggers a blob download when the archive is ready', async () => {
    const fixture = createComponent();
    await flush();
    fixture.detectChanges();

    buttonWithText(fixture.nativeElement, 'Download results ZIP')!.click();
    await flush();
    fixture.detectChanges();

    expect(fakeDispatchApi.downloadResultsArchive).toHaveBeenCalledWith('c1');
    expect(createObjectURLMock).toHaveBeenCalled();
    expect(clickSpy).toHaveBeenCalled();
    expect(revokeObjectURLMock).toHaveBeenCalledWith('blob:fake-url');
  });

  it('shows a status message instead of downloading when the archive is not actually ready yet', async () => {
    fakeDispatchApi.downloadResultsArchive.mockReturnValue(
      of<ResultsArchiveResult>({ ready: false, status: 'Running' }),
    );
    const fixture = createComponent();
    await flush();
    fixture.detectChanges();

    buttonWithText(fixture.nativeElement, 'Download results ZIP')!.click();
    await flush();
    fixture.detectChanges();

    expect(clickSpy).not.toHaveBeenCalled();
    expect(fixture.nativeElement.textContent).toContain('Running');
  });

  it('refreshes the dispatch status list on a manual refresh action', async () => {
    const fixture = createComponent();
    await flush();
    fixture.detectChanges();
    fakeDispatchApi.getDispatchStatus.mockClear();

    buttonWithText(fixture.nativeElement, 'Refresh status')!.click();
    await flush();
    fixture.detectChanges();

    expect(fakeDispatchApi.getDispatchStatus).toHaveBeenCalledWith('c1');
  });
});
