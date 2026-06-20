// Canvas bridge for DxImageEditor. The .NET side owns the edit state; this module
// just holds the decoded source image and re-paints a <canvas> non-destructively
// from it whenever the edits change. All DOM/canvas work lives here because the
// WebAssembly sandbox can't touch the DOM.

// The original decoded image per canvas id — edits never mutate it, so every render
// starts from the pristine source.
const sources = new Map<string, HTMLImageElement>();

interface Edits {
  brightness: number; // percent, 100 = unchanged
  contrast: number;
  saturate: number;
  grayscale: number; // 0..100
  sepia: number;
  invert: number;
  rotate: number; // degrees, 0/90/180/270
  flipH: boolean;
  flipV: boolean;
}

// Decodes a data URL into an image and caches it for the given canvas. Returns a
// promise so the .NET side can await the decode before the first render.
export function loadImage(canvasId: string, dataUrl: string): Promise<void> {
  return new Promise((resolve, reject) => {
    const image = new Image();
    image.onload = () => {
      sources.set(canvasId, image);
      resolve();
    };
    image.onerror = () => reject(new Error("image decode failed"));
    image.src = dataUrl;
  });
}

// Re-paints the canvas from the cached source with the supplied edits and returns
// the resulting PNG data URL (empty string if no image has been loaded yet).
export function render(canvasId: string, editsJson: string): string {
  const canvas = document.getElementById(canvasId) as HTMLCanvasElement | null;
  const image = sources.get(canvasId);
  if (canvas === null || image === undefined) {
    return "";
  }

  const e = JSON.parse(editsJson) as Edits;
  const quarter = e.rotate === 90 || e.rotate === 270;
  const w = image.naturalWidth;
  const h = image.naturalHeight;
  canvas.width = quarter ? h : w;
  canvas.height = quarter ? w : h;

  const ctx = canvas.getContext("2d");
  if (ctx === null) {
    return "";
  }

  ctx.clearRect(0, 0, canvas.width, canvas.height);
  ctx.save();
  ctx.translate(canvas.width / 2, canvas.height / 2);
  ctx.rotate((e.rotate * Math.PI) / 180);
  ctx.scale(e.flipH ? -1 : 1, e.flipV ? -1 : 1);
  ctx.filter =
    `brightness(${e.brightness}%) contrast(${e.contrast}%) saturate(${e.saturate}%) ` +
    `grayscale(${e.grayscale}%) sepia(${e.sepia}%) invert(${e.invert}%)`;
  ctx.drawImage(image, -w / 2, -h / 2);
  ctx.restore();

  // Reading pixels back fails on a canvas tainted by a cross-origin image; the
  // picture still paints, so swallow it and just report no data URL.
  try {
    return canvas.toDataURL("image/png");
  } catch {
    return "";
  }
}

// Triggers a client-side download of the current canvas contents.
export function download(canvasId: string, filename: string, mime: string): void {
  const canvas = document.getElementById(canvasId) as HTMLCanvasElement | null;
  if (canvas === null) {
    return;
  }
  canvas.toBlob((blob) => {
    if (blob === null) {
      return;
    }
    const url = URL.createObjectURL(blob);
    const anchor = document.createElement("a");
    anchor.href = url;
    anchor.download = filename;
    document.body.appendChild(anchor);
    anchor.click();
    document.body.removeChild(anchor);
    URL.revokeObjectURL(url);
  }, mime);
}

// Drops the cached source so a disposed editor doesn't leak its decoded bitmap.
export function dispose(canvasId: string): void {
  sources.delete(canvasId);
}
