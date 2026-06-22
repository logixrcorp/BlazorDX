# ADR 0008 — Build the shared primitive engine before components

**Status:** Accepted

## Context

The enterprise component wishlist (see [ROADMAP.md](../ROADMAP.md)) is ~120
components, but roughly 80% of them reduce to a dozen shared behaviors: anchor
positioning, a dismiss layer, focus trapping, roving-tabindex list navigation,
selection/collection state, virtualization, a data query pipeline, drag-and-drop,
exit motion, a theme-token contract, and icon delivery. Building components
top-down would re-implement these behaviors dozens of times and produce
inconsistent accessibility.

## Decision

Build the primitives first as the engine, then compose components from them.

- Each primitive is headless (Tier 1) and independently testable.
- Components (Tier 2) are thin compositions of primitives plus styling.
- Composite components (e.g. the full DataGrid) are assembled from **feature-sliced
  behaviors** — `sortable`, `filterable`, `selectable`, `editable`, `groupable` as
  separate composable units — rather than one monolithic base class. This keeps
  each unit under the 1000-line cap and independently testable.
- The data pipeline is centralized: `IGridCompute` grows into an `IDataSource`
  engine (sort/filter/group/aggregate/paginate) serving Grid, TreeGrid, PivotGrid,
  Pager, and Charts from one Rust-backed implementation.

## Consequences

- High up-front investment with little user-visible output in Phase 0; the payoff
  is that Phase 1+ components are cheap and uniformly accessible.
- A missing primitive is a visible gap that blocks a whole component cluster, which
  makes prioritization obvious.
- The most-reused and hardest primitive — anchor/floating positioning — was absent
  from the source wishlist and is now a tracked Phase 0 item.
