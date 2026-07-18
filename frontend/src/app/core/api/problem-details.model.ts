// RFC 7807 shape the backend's ProblemDetails middleware writes (Common/Errors/, T012). `type` is
// one of the 14 closed-catalog urn:birrapoint:* values below when raised by a DomainException or
// FluentValidation, or an ASP.NET Core default (non-birrapoint) `type` for framework-generated
// errors (auth challenge/forbid, unmapped 404s) — `services.AddProblemDetails()` formats those
// too, not just the exception handlers.
export interface ProblemDetails {
  type?: string;
  title?: string;
  status?: number;
  detail?: string;
  instance?: string;
  [extension: string]: unknown;
}

export interface ValidationProblemDetails extends ProblemDetails {
  errors: Record<string, string[]>;
}

// contracts/rest-api.md §Error catalog — closed list (14 entries). Adding one requires a contract
// amendment (Principle VI); mirrors the backend's DomainErrorType enum. The const array is the
// single source of truth — BirraPointErrorUrn is derived from it, not maintained separately, so
// isBirraPointErrorUrn() can check membership at runtime without duplicating the list.
export const BIRRAPOINT_ERROR_URNS = [
  'urn:birrapoint:validation',
  'urn:birrapoint:invalid-import-file',
  'urn:birrapoint:invalid-state-transition',
  'urn:birrapoint:conflict-of-interest',
  'urn:birrapoint:unresolved-import-rows',
  'urn:birrapoint:order-already-fixed',
  'urn:birrapoint:order-not-fixed',
  'urn:birrapoint:out-of-sequence',
  'urn:birrapoint:evaluation-locked',
  'urn:birrapoint:table-closed',
  'urn:birrapoint:evaluations-incomplete',
  'urn:birrapoint:discrepancy-open',
  'urn:birrapoint:tables-still-open',
  'urn:birrapoint:judge-already-active',
] as const;

export type BirraPointErrorUrn = (typeof BIRRAPOINT_ERROR_URNS)[number];

export function isBirraPointErrorUrn(value: unknown): value is BirraPointErrorUrn {
  return typeof value === 'string' && (BIRRAPOINT_ERROR_URNS as readonly string[]).includes(value);
}
