//! Host-native Criterion benchmarks for the five dx_grid kernels.
//!
//! IMPORTANT: Criterion runs on the HOST target (e.g. x86_64), not wasm. These
//! numbers measure the opt-level / algorithm delta between profiles, NOT the
//! wasm `simd128` speedup. True in-browser wasm-SIMD validation is a follow-up
//! Playwright/E2E item (see README.md).
//!
//! The kernel modules are pulled in directly with `#[path]` rather than via the
//! library crate. That keeps the library's `crate-type` a pure `cdylib`, so the
//! shipped wasm binary stays byte-for-byte identical to the size build instead
//! of having an `rlib` added just to satisfy the benchmark linker.

#[path = "../src/grid.rs"]
mod grid;
#[path = "../src/chart.rs"]
mod chart;

use criterion::{black_box, criterion_group, criterion_main, Criterion};

const N: usize = 100_000;

/// Deterministic pseudo-random-ish f64 column so runs are comparable.
fn sample_values() -> Vec<f64> {
    (0..N)
        .map(|i| {
            let i = i as f64;
            (i * 12.9898).sin() * 43758.547 % 1000.0
        })
        .collect()
}

fn bench_kernels(c: &mut Criterion) {
    let values = sample_values();
    let xs: Vec<f64> = (0..N).map(|i| i as f64).collect();
    let ys: Vec<f64> = (0..N).map(|i| (i as f64 / 50.0).sin()).collect();

    c.bench_function("sort_indices", |b| {
        b.iter(|| grid::sort_indices(black_box(&values), false))
    });

    c.bench_function("filter_indices_gte", |b| {
        b.iter(|| grid::filter_indices_gte(black_box(&values), black_box(0.0)))
    });

    c.bench_function("aggregate", |b| {
        b.iter(|| grid::aggregate(black_box(&values)))
    });

    c.bench_function("histogram", |b| {
        b.iter(|| chart::histogram(black_box(&values), black_box(256), -1000.0, 1000.0))
    });

    c.bench_function("downsample_lttb", |b| {
        b.iter(|| chart::downsample_lttb(black_box(&xs), black_box(&ys), black_box(2000)))
    });
}

criterion_group!(benches, bench_kernels);
criterion_main!(benches);
