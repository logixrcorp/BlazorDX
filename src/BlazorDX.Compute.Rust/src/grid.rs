//! Pure grid algorithms with no FFI concerns. Keeping the logic here, separate
//! from the `extern "C"` surface in `lib.rs`, means it can be unit-tested on the
//! host target and reasoned about without unsafe code.

use core::cmp::Ordering;

/// Returns a permutation of row indices that orders `values` ascending (or
/// descending). NaN is treated as equal so the sort is always total and never
/// panics. Indices are `u32` because grids never hold more than 4 billion rows
/// and narrower indices halve the marshaling cost across the wasm boundary.
pub fn sort_indices(values: &[f64], descending: bool) -> Vec<u32> {
    let mut order: Vec<u32> = (0..values.len() as u32).collect();
    order.sort_by(|&left, &right| {
        let comparison = values[left as usize]
            .partial_cmp(&values[right as usize])
            .unwrap_or(Ordering::Equal);
        if descending {
            comparison.reverse()
        } else {
            comparison
        }
    });
    order
}

/// Returns the indices of every row whose value is greater than or equal to
/// `threshold`, preserving the original row order.
pub fn filter_indices_gte(values: &[f64], threshold: f64) -> Vec<u32> {
    values
        .iter()
        .enumerate()
        .filter(|(_, &value)| value >= threshold)
        .map(|(index, _)| index as u32)
        .collect()
}

/// Computes summary statistics over a column in a single pass, returned as
/// `[count, sum, min, max, mean]`. NaN values are ignored for sum/min/max/mean;
/// an empty column yields zero count and NaN statistics.
pub fn aggregate(values: &[f64]) -> [f64; 5] {
    let mut count = 0u64;
    let mut sum = 0.0;
    let mut min = f64::INFINITY;
    let mut max = f64::NEG_INFINITY;

    for &value in values {
        if value.is_nan() {
            continue;
        }
        count += 1;
        sum += value;
        if value < min {
            min = value;
        }
        if value > max {
            max = value;
        }
    }

    if count == 0 {
        return [0.0, 0.0, f64::NAN, f64::NAN, f64::NAN];
    }

    [count as f64, sum, min, max, sum / count as f64]
}

#[cfg(test)]
mod tests {
    use super::*;

    #[test]
    fn sorts_ascending_by_value() {
        let values = [3.0, 1.0, 2.0];
        assert_eq!(sort_indices(&values, false), vec![1, 2, 0]);
    }

    #[test]
    fn sorts_descending_by_value() {
        let values = [3.0, 1.0, 2.0];
        assert_eq!(sort_indices(&values, true), vec![0, 2, 1]);
    }

    #[test]
    fn filters_at_or_above_threshold() {
        let values = [10.0, 5.0, 20.0, 5.0];
        assert_eq!(filter_indices_gte(&values, 10.0), vec![0, 2]);
    }

    #[test]
    fn nan_does_not_panic() {
        let values = [1.0, f64::NAN, 0.5];
        let order = sort_indices(&values, false);
        assert_eq!(order.len(), 3);
    }

    #[test]
    fn aggregate_computes_summary_statistics() {
        let values = [2.0, 4.0, 6.0];
        let [count, sum, min, max, mean] = aggregate(&values);
        assert_eq!(count, 3.0);
        assert_eq!(sum, 12.0);
        assert_eq!(min, 2.0);
        assert_eq!(max, 6.0);
        assert_eq!(mean, 4.0);
    }
}
