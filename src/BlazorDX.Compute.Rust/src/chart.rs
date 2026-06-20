//! Chart data reduction. Largest-Triangle-Three-Buckets (LTTB) downsampling keeps
//! the visual shape of a large series while cutting it to a target point count —
//! the heavy lifting a data-viz layer should offload from the UI thread.

/// Returns the indices of the points to keep when reducing the series to roughly
/// `threshold` points. The first and last points are always kept. If the series
/// is already at or below the threshold (or the threshold is degenerate), every
/// index is returned unchanged.
pub fn downsample_lttb(x: &[f64], y: &[f64], threshold: usize) -> Vec<u32> {
    let n = x.len();
    if threshold >= n || threshold < 3 {
        return (0..n as u32).collect();
    }

    let mut sampled: Vec<u32> = Vec::with_capacity(threshold);
    sampled.push(0); // always keep the first point

    let bucket_size = (n - 2) as f64 / (threshold - 2) as f64;
    let mut anchor = 0usize;

    for i in 0..threshold - 2 {
        // Average point of the *next* bucket (the triangle's far vertex).
        let avg_start = ((i + 1) as f64 * bucket_size) as usize + 1;
        let mut avg_end = ((i + 2) as f64 * bucket_size) as usize + 1;
        if avg_end > n {
            avg_end = n;
        }
        let avg_count = (avg_end - avg_start).max(1);
        let mut avg_x = 0.0;
        let mut avg_y = 0.0;
        for j in avg_start..avg_end {
            avg_x += x[j];
            avg_y += y[j];
        }
        avg_x /= avg_count as f64;
        avg_y /= avg_count as f64;

        // Pick the point in the current bucket that forms the largest triangle.
        let range_start = (i as f64 * bucket_size) as usize + 1;
        let range_end = ((i + 1) as f64 * bucket_size) as usize + 1;
        let (anchor_x, anchor_y) = (x[anchor], y[anchor]);
        let mut max_area = -1.0;
        let mut next_anchor = range_start;
        for j in range_start..range_end {
            let area = ((anchor_x - avg_x) * (y[j] - anchor_y)
                - (anchor_x - x[j]) * (avg_y - anchor_y))
                .abs()
                * 0.5;
            if area > max_area {
                max_area = area;
                next_anchor = j;
            }
        }

        sampled.push(next_anchor as u32);
        anchor = next_anchor;
    }

    sampled.push((n - 1) as u32); // always keep the last point
    sampled
}

/// Counts how many values fall into each of `bins` equal-width buckets spanning
/// `[min, max]`. Values outside the range are ignored; a value exactly equal to
/// `max` lands in the last bin. Returns one count per bin. A degenerate request
/// (`bins == 0`, or a non-positive range) yields an empty/zeroed result.
pub fn histogram(values: &[f64], bins: usize, min: f64, max: f64) -> Vec<u32> {
    let mut counts = vec![0u32; bins];
    let span = max - min;
    if bins == 0 || span <= 0.0 {
        return counts;
    }

    let last = bins - 1;
    for &v in values {
        if v.is_nan() || v < min || v > max {
            continue;
        }
        let mut bin = ((v - min) / span * bins as f64) as usize;
        if bin > last {
            bin = last; // v == max
        }
        counts[bin] += 1;
    }

    counts
}

#[cfg(test)]
mod tests {
    use super::*;

    #[test]
    fn histogram_bins_values_and_includes_max() {
        let values = [0.0, 1.0, 1.0, 2.0, 3.0, 4.0];
        // 4 bins over [0, 4]: widths [0,1)[1,2)[2,3)[3,4]
        let counts = histogram(&values, 4, 0.0, 4.0);
        assert_eq!(counts, vec![1, 2, 1, 2]); // 4.0 lands in the last bin
    }

    #[test]
    fn histogram_ignores_out_of_range_and_nan() {
        let values = [-1.0, 0.5, f64::NAN, 9.0];
        let counts = histogram(&values, 2, 0.0, 1.0);
        assert_eq!(counts, vec![0, 1]); // 0.5 sits on the bin boundary -> bin 1
    }

    #[test]
    fn keeps_endpoints_and_hits_threshold() {
        let x: Vec<f64> = (0..1000).map(|i| i as f64).collect();
        let y: Vec<f64> = x.iter().map(|v| (v / 50.0).sin()).collect();
        let indices = downsample_lttb(&x, &y, 100);
        assert_eq!(indices.len(), 100);
        assert_eq!(indices[0], 0);
        assert_eq!(*indices.last().unwrap(), 999);
    }

    #[test]
    fn small_series_is_returned_whole() {
        let x = [0.0, 1.0, 2.0];
        let y = [0.0, 1.0, 0.0];
        assert_eq!(downsample_lttb(&x, &y, 100), vec![0, 1, 2]);
    }
}
