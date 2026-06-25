// Native HTML5 drag-and-drop for the file manager. Two gestures share one
// module:
//
//   1. Move items within/between panes — a draggable item carries its id in the
//      DataTransfer; a registered drop target reports the (sourceId, targetId)
//      pair back to .NET, which performs the move on the model.
//   2. Drop OS files to upload — a drop target inspects DataTransfer.files (the
//      File API) and reports each dropped file's name/size/type back to .NET.
//
// Everything is addressed by element id so no ElementReference crosses the
// [JSImport] boundary (matching overlay.ts / image-editor.ts). The .NET side
// owns the lifecycle: registering a target attaches listeners; unregistering
// tears them down. Drag-and-drop is purely an enhancement — the keyboard "move"
// buttons and the InputFile upload path work without this module.

type Cleanup = () => void;

// The MIME type carrying our internal move payload (an item id). Distinct from
// OS-file drops, which arrive as DataTransfer.files.
const MOVE_TYPE = "application/x-blazordx-fm-item";

const registered = new Map<string, Cleanup>();

// .NET callback shapes. onMove receives the dragged item id and the drop target
// id; onFiles receives a JSON array of {name,size,contentType} describing the
// dropped OS files (a JSON string is the shape the [JSImport] function marshaler
// accepts — array-of-array params are unsupported).
type MoveCallback = (sourceId: string, targetId: string) => void;
type FilesCallback = (filesJson: string) => void;

// Marks an element draggable and stamps its id into the DataTransfer on
// dragstart. Idempotent: re-registering the same id replaces the prior wiring.
export function registerDraggable(elementId: string): void {
  const element = document.getElementById(elementId);
  if (element === null) {
    return;
  }
  unregister(elementId);

  element.setAttribute("draggable", "true");
  const onDragStart = (event: DragEvent) => {
    if (event.dataTransfer === null) {
      return;
    }
    event.dataTransfer.setData(MOVE_TYPE, elementId);
    event.dataTransfer.effectAllowed = "move";
  };
  element.addEventListener("dragstart", onDragStart);
  registered.set(elementId, () => {
    element.removeEventListener("dragstart", onDragStart);
    element.removeAttribute("draggable");
  });
}

// Wires a drop target. Accepts both internal item moves and OS file drops; the
// relevant .NET callback fires depending on what was dropped. dragover is
// cancelled so the browser permits the drop and shows the copy/move cursor.
export function registerDropTarget(
  elementId: string,
  onMove: MoveCallback,
  onFiles: FilesCallback,
): void {
  const element = document.getElementById(elementId);
  if (element === null) {
    return;
  }
  unregister(elementId);

  const onDragOver = (event: DragEvent) => {
    event.preventDefault();
    if (event.dataTransfer !== null) {
      const hasFiles = Array.from(event.dataTransfer.types).includes("Files");
      event.dataTransfer.dropEffect = hasFiles ? "copy" : "move";
    }
    element.classList.add("dx-fm-drop-over");
  };
  const onDragLeave = () => element.classList.remove("dx-fm-drop-over");
  const onDrop = (event: DragEvent) => {
    event.preventDefault();
    element.classList.remove("dx-fm-drop-over");
    const data = event.dataTransfer;
    if (data === null) {
      return;
    }

    if (data.files.length > 0) {
      const described = Array.from(data.files).map((file) => ({
        name: file.name,
        size: file.size,
        contentType: file.type,
      }));
      onFiles(JSON.stringify(described));
      return;
    }

    const sourceId = data.getData(MOVE_TYPE);
    if (sourceId !== "" && sourceId !== elementId) {
      onMove(sourceId, elementId);
    }
  };

  element.addEventListener("dragover", onDragOver);
  element.addEventListener("dragleave", onDragLeave);
  element.addEventListener("drop", onDrop);
  registered.set(elementId, () => {
    element.removeEventListener("dragover", onDragOver);
    element.removeEventListener("dragleave", onDragLeave);
    element.removeEventListener("drop", onDrop);
    element.classList.remove("dx-fm-drop-over");
  });
}

// Tears down whatever was registered for the id (draggable or drop target).
export function unregister(elementId: string): void {
  const cleanup = registered.get(elementId);
  if (cleanup !== undefined) {
    cleanup();
    registered.delete(elementId);
  }
}
