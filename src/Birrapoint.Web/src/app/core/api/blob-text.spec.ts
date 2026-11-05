import { blobToText } from './blob-text';

describe('blobToText()', () => {
  it('resolves the text content of a blob', async () => {
    const blob = new Blob(['{"status":"Running"}'], { type: 'application/json' });

    await expect(blobToText(blob)).resolves.toBe('{"status":"Running"}');
  });

  it('resolves an empty string for an empty blob', async () => {
    const blob = new Blob([], { type: 'text/plain' });

    await expect(blobToText(blob)).resolves.toBe('');
  });
});
