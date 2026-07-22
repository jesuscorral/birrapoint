// FileReader, not Blob.prototype.text() — the Blob polyfill in this project's Jest jsdom
// environment doesn't implement .text()/.arrayBuffer(), so this keeps blob-to-text reads
// identical under test and in a real browser.
export function blobToText(blob: Blob): Promise<string> {
  return new Promise((resolve, reject) => {
    const reader = new FileReader();
    reader.onload = () => resolve(typeof reader.result === 'string' ? reader.result : '');
    reader.onerror = () => reject(reader.error ?? new Error('Failed to read blob as text'));
    reader.readAsText(blob);
  });
}
