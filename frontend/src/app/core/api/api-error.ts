import { isBirraPointErrorUrn } from './problem-details.model';
import type {
  BirraPointErrorUrn,
  ProblemDetails,
  ValidationProblemDetails,
} from './problem-details.model';

// Normalized, UI-facing error shape every ApiClient call rejects with — callers never need to
// know whether the backend raised a DomainException, FluentValidation, or a framework default
// (auth challenge, unmapped 404).
export class ApiError extends Error {
  readonly status: number;
  readonly title: string;
  readonly urn: BirraPointErrorUrn | null;
  readonly detail?: string;
  readonly errors?: Record<string, string[]>;
  readonly extensions: Record<string, unknown>;

  constructor(params: {
    status: number;
    title: string;
    urn: BirraPointErrorUrn | null;
    detail?: string;
    errors?: Record<string, string[]>;
    extensions?: Record<string, unknown>;
  }) {
    super(params.title);
    this.name = 'ApiError';
    this.status = params.status;
    this.title = params.title;
    this.urn = params.urn;
    this.detail = params.detail;
    this.errors = params.errors;
    this.extensions = params.extensions ?? {};
  }
}

function isProblemDetailsShaped(body: unknown): body is ProblemDetails {
  return typeof body === 'object' && body !== null;
}

function isValidationProblemDetails(body: ProblemDetails): body is ValidationProblemDetails {
  return typeof body['errors'] === 'object' && body['errors'] !== null;
}

const GENERIC_TITLE = 'An unexpected error occurred.';
const STANDARD_PROBLEM_DETAILS_KEYS = new Set([
  'type',
  'title',
  'status',
  'detail',
  'instance',
  'errors',
]);

function extractExtensions(body: ProblemDetails): Record<string, unknown> {
  // Object.create(null), not {}: JSON.parse('{"__proto__": ...}') produces a genuine own
  // property named "__proto__" (CreateDataProperty semantics), and assigning that key into a
  // normal {} object triggers Object.prototype's __proto__ accessor setter instead of storing a
  // property — a null-prototype object has no such accessor, so the assignment below is always a
  // plain own-property write (T020 review).
  const extensions: Record<string, unknown> = Object.create(null) as Record<string, unknown>;
  for (const [key, value] of Object.entries(body)) {
    if (!STANDARD_PROBLEM_DETAILS_KEYS.has(key)) {
      extensions[key] = value;
    }
  }
  return extensions;
}

/**
 * Maps an HTTP status + response body to an {@link ApiError}. The body may be a well-formed
 * ProblemDetails (the common case — every 4xx/5xx from this API is `application/problem+json`,
 * including framework-default auth challenges via `services.AddProblemDetails()`), a non-JSON
 * body, or nothing at all (network failure, `status === 0`) — this never throws regardless.
 */
export function toApiError(status: number, body: unknown): ApiError {
  if (!isProblemDetailsShaped(body)) {
    return new ApiError({ status, title: GENERIC_TITLE, urn: null });
  }

  const urn = isBirraPointErrorUrn(body.type) ? body.type : null;
  const title =
    typeof body.title === 'string' && body.title.length > 0 ? body.title : GENERIC_TITLE;
  const detail = typeof body.detail === 'string' ? body.detail : undefined;
  const errors = isValidationProblemDetails(body) ? body.errors : undefined;

  return new ApiError({ status, title, urn, detail, errors, extensions: extractExtensions(body) });
}
