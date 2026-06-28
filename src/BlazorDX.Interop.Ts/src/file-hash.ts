// Browser-side file-integrity hashing via the Web Crypto SubtleCrypto API. When files are
// selected (or dropped) we compute each file's hash in the browser so the upload can carry it
// and the receiving side can re-hash the bytes it actually received and confirm they match
// before the file is written — catching corruption in transit end to end.
//
// SubtleCrypto.digest has no incremental/streaming API, so each file is read in full with
// Blob.arrayBuffer() and digested once using the platform's native (fast, vetted) hash rather
// than shipping a hand-rolled one. Files are addressed by the <input> element's id, so no File
// object crosses the [JSImport] boundary (matching file-dnd.ts); the result is returned as a
// JSON array of {name, size, hash} (a JSON string is the shape the marshaler accepts).

interface Described {
  name: string;
  size: number;
  hash: string;
}

// Maps a .NET HashAlgorithmName to the SubtleCrypto identifier. Unknown names fall back to
// SHA-256, matching the server-side FileHasher default (SHA-1 is supported but never the default).
function subtleName(algorithm: string): string {
  switch (algorithm.toUpperCase()) {
    case "SHA1":
    case "SHA-1":
      return "SHA-1";
    case "SHA384":
    case "SHA-384":
      return "SHA-384";
    case "SHA512":
    case "SHA-512":
      return "SHA-512";
    default:
      return "SHA-256";
  }
}

function toHex(buffer: ArrayBuffer): string {
  const bytes = new Uint8Array(buffer);
  let hex = "";
  for (let i = 0; i < bytes.length; i++) {
    hex += bytes[i].toString(16).padStart(2, "0");
  }
  return hex;
}

// Hashes every file currently selected on the input with the given id and returns a JSON array of
// {name, size, hash} (lowercase hex). Returns "[]" if the element is missing or holds no files.
export async function hashInputFiles(elementId: string, algorithm: string): Promise<string> {
  const input = document.getElementById(elementId) as HTMLInputElement | null;
  if (input === null || input.files === null) {
    return "[]";
  }

  const algo = subtleName(algorithm);
  const results: Described[] = [];
  for (const file of Array.from(input.files)) {
    const buffer = await file.arrayBuffer();
    const digest = await crypto.subtle.digest(algo, buffer);
    results.push({ name: file.name, size: file.size, hash: toHex(digest) });
  }
  return JSON.stringify(results);
}
