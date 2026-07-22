import { HttpClient, HttpErrorResponse, HttpHeaders, HttpResponse } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import type { Observable } from 'rxjs';
import { catchError, from, switchMap, throwError } from 'rxjs';

import { environment } from '../../../environments/environment';
import { toApiError } from './api-error';
import { blobToText } from './blob-text';

export interface ApiRequestOptions {
  headers?: HttpHeaders | Record<string, string | string[]>;
}

function rethrowAsApiError(error: unknown): Observable<never> {
  if (error instanceof HttpErrorResponse) {
    return throwError(() => toApiError(error.status, error.error));
  }
  return throwError(() => error);
}

// responseType: 'blob' applies to error bodies too, so a 4xx/5xx from getBlob() never arrives as
// parsed JSON like the other verbs — its body has to be read back out as text first.
function rethrowBlobError(error: unknown): Observable<never> {
  if (error instanceof HttpErrorResponse && error.error instanceof Blob) {
    return from(blobToText(error.error)).pipe(
      switchMap((text) => {
        let body: unknown = null;
        try {
          body = text ? JSON.parse(text) : null;
        } catch {
          body = null;
        }
        return throwError(() => toApiError(error.status, body));
      }),
    );
  }
  return rethrowAsApiError(error);
}

/**
 * Thin typed HttpClient wrapper: builds every request against `${apiBaseUrl}/api/v1`, and
 * normalizes any failure into an {@link ApiError} so callers never handle a raw HttpErrorResponse.
 * No per-endpoint methods — those land with the feature slice that first needs them.
 */
@Injectable({ providedIn: 'root' })
export class ApiClient {
  private readonly http = inject(HttpClient);
  private readonly baseUrl = `${environment.apiBaseUrl}/api/v1`;

  get<T>(path: string, options?: ApiRequestOptions): Observable<T> {
    return this.http.get<T>(this.url(path), options).pipe(catchError(rethrowAsApiError));
  }

  post<T>(path: string, body: unknown, options?: ApiRequestOptions): Observable<T> {
    return this.http.post<T>(this.url(path), body, options).pipe(catchError(rethrowAsApiError));
  }

  put<T>(path: string, body: unknown, options?: ApiRequestOptions): Observable<T> {
    return this.http.put<T>(this.url(path), body, options).pipe(catchError(rethrowAsApiError));
  }

  delete<T>(path: string, options?: ApiRequestOptions): Observable<T> {
    return this.http.delete<T>(this.url(path), options).pipe(catchError(rethrowAsApiError));
  }

  // Full HttpResponse (not just the body) so callers can branch on status — e.g. the results
  // archive endpoint (contracts/rest-api.md) returns 200 (ZIP bytes) or 202 (JSON status) through
  // the same blob-typed request, and only the status code tells the two apart.
  getBlob(path: string): Observable<HttpResponse<Blob>> {
    return this.http
      .get(this.url(path), { responseType: 'blob', observe: 'response' })
      .pipe(catchError(rethrowBlobError));
  }

  private url(path: string): string {
    return `${this.baseUrl}${path}`;
  }
}
