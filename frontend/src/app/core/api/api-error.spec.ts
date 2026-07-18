import { ApiError, toApiError } from './api-error';

describe('toApiError', () => {
  it('maps a validation ProblemDetails, preserving the per-field error map', () => {
    const error = toApiError(400, {
      type: 'urn:birrapoint:validation',
      title: 'Validation failed',
      status: 400,
      errors: { name: ['Name is required'] },
    });

    expect(error).toBeInstanceOf(ApiError);
    expect(error.status).toBe(400);
    expect(error.urn).toBe('urn:birrapoint:validation');
    expect(error.title).toBe('Validation failed');
    expect(error.errors).toEqual({ name: ['Name is required'] });
  });

  it('maps a domain-conflict ProblemDetails, preserving its extension fields', () => {
    const error = toApiError(409, {
      type: 'urn:birrapoint:conflict-of-interest',
      title: 'Conflict of interest',
      status: 409,
      detail: 'Judge is entered in this competition.',
      conflicts: [{ judgeId: 'j1', beerEntryIds: ['e1'] }],
    });

    expect(error.urn).toBe('urn:birrapoint:conflict-of-interest');
    expect(error.detail).toBe('Judge is entered in this competition.');
    expect(error.extensions['conflicts']).toEqual([{ judgeId: 'j1', beerEntryIds: ['e1'] }]);
  });

  it('falls back to a null urn for a non-birrapoint ProblemDetails (e.g. the framework default auth challenge)', () => {
    const error = toApiError(401, {
      type: 'https://tools.ietf.org/html/rfc7235#section-3.1',
      title: 'Unauthorized',
      status: 401,
    });

    expect(error.urn).toBeNull();
    expect(error.status).toBe(401);
    expect(error.title).toBe('Unauthorized');
  });

  it('never throws on an unparseable or empty body', () => {
    expect(() => toApiError(0, undefined)).not.toThrow();
    expect(() => toApiError(0, null)).not.toThrow();
    expect(() => toApiError(500, 'plain text body')).not.toThrow();

    const error = toApiError(0, undefined);
    expect(error.urn).toBeNull();
    expect(error.status).toBe(0);
    expect(error.title).toBeTruthy();
  });
});
