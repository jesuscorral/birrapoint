import { HttpClient, HttpErrorResponse, HttpHeaders } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import type { Observable } from 'rxjs';
import { catchError, throwError } from 'rxjs';

import { environment } from '../../../environments/environment';
import { toApiError } from './api-error';

export interface ApiRequestOptions {
  headers?: HttpHeaders | Record<string, string | string[]>;
}

function rethrowAsApiError(error: unknown): Observable<never> {
  if (error instanceof HttpErrorResponse) {
    return throwError(() => toApiError(error.status, error.error));
  }
  return throwError(() => error);
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

  private url(path: string): string {
    return `${this.baseUrl}${path}`;
  }
}
