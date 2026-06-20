//! C-ABI surface for the `dx_grid` wasm module.
//!
//! The TypeScript bridge (`grid-interop.ts`) drives this module: it allocates a
//! buffer with [`alloc`], copies a column of `f64` values into wasm memory, calls
//! one of the kernels, then reads the resulting `u32` index buffer back out.
//! Every kernel returns a permutation or subset of row *indices*, never row data,
//! which keeps the amount of memory crossing the boundary independent of row size.

mod chart;
mod grid;

use core::mem;

/// Allocates `len` bytes inside the module's linear memory and returns a pointer
/// the host can write into. The host must later return it via [`dealloc`].
#[no_mangle]
pub extern "C" fn alloc(len: usize) -> *mut u8 {
    let mut buffer = Vec::<u8>::with_capacity(len);
    let pointer = buffer.as_mut_ptr();
    mem::forget(buffer); // ownership passes to the host until dealloc
    pointer
}

/// Frees a buffer previously returned by [`alloc`].
///
/// # Safety
/// `pointer`/`len` must come from a prior [`alloc`] call and be freed once.
#[no_mangle]
pub unsafe extern "C" fn dealloc(pointer: *mut u8, len: usize) {
    drop(Vec::from_raw_parts(pointer, 0, len));
}

/// Sorts row indices by the `f64` column at `values_ptr` (`len` elements) and
/// writes the resulting `u32` permutation (`len` elements) to `out_ptr`.
///
/// # Safety
/// Both buffers must be valid for `len` elements of their respective types.
#[no_mangle]
pub unsafe extern "C" fn sort_indices_f64(
    values_ptr: *const f64,
    len: usize,
    descending: u32,
    out_ptr: *mut u32,
) {
    let values = core::slice::from_raw_parts(values_ptr, len);
    let order = grid::sort_indices(values, descending != 0);
    core::slice::from_raw_parts_mut(out_ptr, len).copy_from_slice(&order);
}

/// Writes the `u32` indices of rows whose value is `>= threshold` to `out_ptr`
/// and returns how many matched. `out_ptr` must have room for `len` indices.
///
/// # Safety
/// `values_ptr` must be valid for `len` `f64`s and `out_ptr` for `len` `u32`s.
#[no_mangle]
pub unsafe extern "C" fn filter_indices_gte_f64(
    values_ptr: *const f64,
    len: usize,
    threshold: f64,
    out_ptr: *mut u32,
) -> usize {
    let values = core::slice::from_raw_parts(values_ptr, len);
    let matches = grid::filter_indices_gte(values, threshold);
    core::slice::from_raw_parts_mut(out_ptr, matches.len()).copy_from_slice(&matches);
    matches.len()
}

/// Computes `[count, sum, min, max, mean]` over the column at `values_ptr` and
/// writes the five `f64`s to `out_ptr`.
///
/// # Safety
/// `values_ptr` must be valid for `len` `f64`s and `out_ptr` for 5 `f64`s.
#[no_mangle]
pub unsafe extern "C" fn aggregate_f64(values_ptr: *const f64, len: usize, out_ptr: *mut f64) {
    let values = core::slice::from_raw_parts(values_ptr, len);
    let stats = grid::aggregate(values);
    core::slice::from_raw_parts_mut(out_ptr, 5).copy_from_slice(&stats);
}

/// Counts values into `bins` equal-width buckets over `[min, max]`, writing the
/// `u32` per-bin counts to `out_ptr` and returning `bins`. `out_ptr` must have
/// room for `bins` counts.
///
/// # Safety
/// `values_ptr` must be valid for `len` `f64`s and `out_ptr` for `bins` `u32`s.
#[no_mangle]
pub unsafe extern "C" fn histogram_f64(
    values_ptr: *const f64,
    len: usize,
    bins: usize,
    min: f64,
    max: f64,
    out_ptr: *mut u32,
) -> usize {
    let values = core::slice::from_raw_parts(values_ptr, len);
    let counts = chart::histogram(values, bins, min, max);
    core::slice::from_raw_parts_mut(out_ptr, counts.len()).copy_from_slice(&counts);
    counts.len()
}

/// LTTB-downsamples the (x, y) series to roughly `threshold` points, writing the
/// kept `u32` indices to `out_ptr` and returning their count. `out_ptr` must have
/// room for `len` indices.
///
/// # Safety
/// `x_ptr`/`y_ptr` must be valid for `len` `f64`s and `out_ptr` for `len` `u32`s.
#[no_mangle]
pub unsafe extern "C" fn downsample_lttb_f64(
    x_ptr: *const f64,
    y_ptr: *const f64,
    len: usize,
    threshold: usize,
    out_ptr: *mut u32,
) -> usize {
    let x = core::slice::from_raw_parts(x_ptr, len);
    let y = core::slice::from_raw_parts(y_ptr, len);
    let indices = chart::downsample_lttb(x, y, threshold);
    core::slice::from_raw_parts_mut(out_ptr, indices.len()).copy_from_slice(&indices);
    indices.len()
}
