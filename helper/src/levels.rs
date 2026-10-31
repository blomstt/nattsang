//! The visualizer's levels: how loud the music is in 28 bands from 40 Hz to
//! 16 kHz, measured from the samples as they go to the speakers.
//!
//! Every HOP samples (30 times a second) the last SIZE samples, mixed to
//! mono and shaped by a Hann window, go through an FFT; a band's level is
//! the power of its bins in decibels, a full-scale sine filling its band at
//! 0 dB, and mapped from FLOOR_DB..TOP_DB to 0..100. The bands are spaced
//! evenly by octave, so music, which is loudest low down, shows about evenly
//! across them; TILT lifts the highs a little more. Cheap: one 2048-point FFT
//! per frame, no allocation once made.

pub const BANDS: usize = 28;
pub const RATE: f32 = 44100.0;
/// Samples per frame: 30 frames a second.
pub const HOP: usize = 1470;
/// Samples in each measurement (46 ms).
pub const SIZE: usize = 2048;
const LOW_HZ: f32 = 40.0;
const HIGH_HZ: f32 = 16000.0;
/// A band this loud is 0, this loud full.
const FLOOR_DB: f32 = -60.0;
const TOP_DB: f32 = -12.0;
/// Decibels added an octave above 1 kHz (taken off below it).
const TILT: f32 = 1.5;

pub struct Analyzer {
    ring: Vec<f32>, // the last SIZE mono samples; the oldest at `at`
    at: usize,
    filled: usize,
    since: usize, // samples since the last frame
    window: Vec<f32>,
    edges: [usize; BANDS + 1], // band b is bins edges[b]..edges[b + 1]
    tilt: [f32; BANDS],
    cos: Vec<f32>,
    sin: Vec<f32>,
    re: Vec<f32>,
    im: Vec<f32>,
}

impl Analyzer {
    pub fn new() -> Self {
        let window = (0..SIZE).map(|i| 0.5 - 0.5 * (2.0 * std::f32::consts::PI * i as f32 / SIZE as f32).cos()).collect();
        let bin = RATE / SIZE as f32;
        let mut edges = [0; BANDS + 1];
        for (b, e) in edges.iter_mut().enumerate() {
            let hz = LOW_HZ * (HIGH_HZ / LOW_HZ).powf(b as f32 / BANDS as f32);
            *e = (hz / bin).round() as usize;
        }
        // Each band at least one bin of its own, low down where they're narrower than a bin.
        for b in 1..=BANDS {
            edges[b] = edges[b].max(edges[b - 1] + 1).min(SIZE / 2);
        }
        let mut tilt = [0.0; BANDS];
        for (b, t) in tilt.iter_mut().enumerate() {
            let centre = ((edges[b] + edges[b + 1]) as f32 / 2.0) * bin;
            *t = TILT * (centre / 1000.0).log2();
        }
        let half = SIZE / 2;
        let angle = |k: usize| -2.0 * std::f32::consts::PI * k as f32 / SIZE as f32;
        Analyzer {
            ring: vec![0.0; SIZE],
            at: 0,
            filled: 0,
            since: 0,
            window,
            edges,
            tilt,
            cos: (0..half).map(|k| angle(k).cos()).collect(),
            sin: (0..half).map(|k| angle(k).sin()).collect(),
            re: vec![0.0; SIZE],
            im: vec![0.0; SIZE],
        }
    }

    /// Forgets what it has heard: after a pause, or levels turned off.
    pub fn reset(&mut self) {
        self.filled = 0;
        self.since = 0;
        self.at = 0;
    }

    /// Takes interleaved stereo samples; for each frame that falls due in
    /// them, out(how many stereo samples of these it had taken, the levels).
    pub fn feed(&mut self, samples: &[f64], mut out: impl FnMut(usize, &[u8; BANDS])) {
        for (i, pair) in samples.chunks_exact(2).enumerate() {
            self.ring[self.at] = ((pair[0] + pair[1]) * 0.5) as f32;
            self.at = (self.at + 1) % SIZE;
            self.filled = (self.filled + 1).min(SIZE);
            self.since += 1;
            if self.since >= HOP && self.filled == SIZE {
                self.since = 0;
                let levels = self.levels();
                out(i + 1, &levels);
            }
        }
    }

    /// The levels of the last SIZE samples, 0 to 100 a band.
    pub fn levels(&mut self) -> [u8; BANDS] {
        for i in 0..SIZE {
            self.re[i] = self.ring[(self.at + i) % SIZE] * self.window[i];
            self.im[i] = 0.0;
        }
        fft(&mut self.re, &mut self.im, &self.cos, &self.sin);
        // A full-scale sine's peak bin is SIZE/4 through the Hann window,
        // and its two neighbours half that: 1.5 × peak² in all.
        let reference = 1.5 * (SIZE as f32 / 4.0).powi(2);
        let mut levels = [0u8; BANDS];
        for (b, level) in levels.iter_mut().enumerate() {
            let power: f32 = (self.edges[b]..self.edges[b + 1]).map(|k| self.re[k] * self.re[k] + self.im[k] * self.im[k]).sum();
            let db = 10.0 * (power / reference).max(1e-12).log10() + self.tilt[b];
            *level = (((db - FLOOR_DB) / (TOP_DB - FLOOR_DB)).clamp(0.0, 1.0) * 100.0).round() as u8;
        }
        levels
    }
}

/// In place, radix 2, re and im SIZE long; cos and sin the first half of
/// e^(-2πik/SIZE).
fn fft(re: &mut [f32], im: &mut [f32], cos: &[f32], sin: &[f32]) {
    let n = re.len();
    let mut j = 0;
    for i in 1..n {
        let mut bit = n >> 1;
        while j & bit != 0 {
            j ^= bit;
            bit >>= 1;
        }
        j |= bit;
        if i < j {
            re.swap(i, j);
            im.swap(i, j);
        }
    }
    let mut len = 2;
    while len <= n {
        let step = n / len;
        for start in (0..n).step_by(len) {
            for k in 0..len / 2 {
                let (c, s) = (cos[k * step], sin[k * step]);
                let (a, b) = (start + k, start + k + len / 2);
                let tr = re[b] * c - im[b] * s;
                let ti = re[b] * s + im[b] * c;
                re[b] = re[a] - tr;
                im[b] = im[a] - ti;
                re[a] += tr;
                im[a] += ti;
            }
        }
        len <<= 1;
    }
}

#[cfg(test)]
mod tests {
    use super::*;

    // `seconds` of a sine at hz, amplitude amp, as interleaved stereo.
    fn sine(hz: f32, amp: f64, seconds: f32) -> Vec<f64> {
        let n = (RATE * seconds) as usize;
        (0..n).flat_map(|i| {
            let v = amp * (2.0 * std::f64::consts::PI * hz as f64 * i as f64 / RATE as f64).sin();
            [v, v]
        }).collect()
    }

    fn last(samples: &[f64]) -> [u8; BANDS] {
        let mut a = Analyzer::new();
        let mut got = [0; BANDS];
        a.feed(samples, |_, l| got = *l);
        got
    }

    fn band_of(hz: f32) -> usize {
        let a = Analyzer::new();
        let k = (hz / (RATE / SIZE as f32)).round() as usize;
        (0..BANDS).find(|&b| a.edges[b] <= k && k < a.edges[b + 1]).unwrap()
    }

    fn loudest(l: &[u8; BANDS]) -> usize {
        (0..BANDS).max_by_key(|&b| l[b]).unwrap()
    }

    #[test]
    fn silence_is_zero() {
        assert_eq!(last(&vec![0.0; 44100 * 2]), [0; BANDS]);
    }

    #[test]
    fn a_sine_lights_its_own_band() {
        for hz in [60.0, 250.0, 1000.0, 4000.0, 12000.0] {
            let l = last(&sine(hz, 1.0, 0.5));
            let b = band_of(hz);
            assert!(loudest(&l).abs_diff(b) <= 1, "{hz} Hz: loudest band {} not {b}: {l:?}", loudest(&l));
            assert!(l[b] >= 90, "{hz} Hz full scale: {l:?}");
            // Two octaves away it's quiet: a Hann window's skirts fall fast.
            for far in (0..BANDS).filter(|&x| x.abs_diff(b) >= 7) {
                assert!(l[far] <= 20, "{hz} Hz: band {far} at {}: {l:?}", l[far]);
            }
        }
    }

    #[test]
    fn quieter_is_lower() {
        let b = band_of(1000.0);
        let loud = last(&sine(1000.0, 0.1, 0.5))[b]; // -20 dB
        let soft = last(&sine(1000.0, 0.1 * 0.0316, 0.5))[b]; // 30 dB less
        let expected = (30.0 / (TOP_DB - FLOOR_DB) * 100.0) as i32;
        assert!(((loud as i32 - soft as i32) - expected).abs() <= 3, "loud {loud}, soft {soft}");
    }

    #[test]
    fn noise_reaches_every_band() {
        let mut seed = 1u32;
        let noise: Vec<f64> = (0..44100).flat_map(|_| {
            seed = seed.wrapping_mul(1664525).wrapping_add(1013904223);
            let v = (seed >> 8) as f64 / (1u32 << 24) as f64 - 0.5;
            [v, v]
        }).collect();
        let l = last(&noise);
        assert!(l.iter().all(|&v| v > 0), "{l:?}");
        // White noise rises an octave at a time, as the bands widen.
        assert!(l[BANDS - 1] > l[2], "{l:?}");
    }

    #[test]
    fn thirty_frames_a_second() {
        let mut a = Analyzer::new();
        let mut at = Vec::new();
        a.feed(&sine(440.0, 0.5, 1.0), |i, _| at.push(i));
        // The first once SIZE samples are in, then every HOP.
        assert_eq!(at.len(), (44100 - SIZE) / HOP + 1);
        assert_eq!(at[0], SIZE);
        assert!(at.windows(2).all(|w| w[1] - w[0] == HOP));
    }

    #[test]
    fn chunks_give_the_same_frames() {
        let s = sine(700.0, 0.7, 0.4);
        let mut whole = Vec::new();
        Analyzer::new().feed(&s, |_, l| whole.push(*l));
        let mut parts = Vec::new();
        let mut a = Analyzer::new();
        for chunk in s.chunks(2 * 333) {
            a.feed(chunk, |_, l| parts.push(*l));
        }
        assert_eq!(whole, parts);
    }

    #[test]
    fn reset_waits_for_a_full_measure() {
        let mut a = Analyzer::new();
        a.feed(&sine(440.0, 0.5, 0.2), |_, _| {});
        a.reset();
        let mut n = 0;
        a.feed(&sine(440.0, 0.5, 0.04), |_, _| n += 1); // fewer than SIZE samples
        assert_eq!(n, 0);
    }

    #[test]
    fn bands_rise_and_fit() {
        let a = Analyzer::new();
        assert!(a.edges.windows(2).all(|w| w[1] > w[0]));
        assert!(a.edges[BANDS] <= SIZE / 2);
    }

    // cargo test --release -- --nocapture cost: a minute of music's frames.
    #[test]
    fn cost() {
        let s = sine(440.0, 0.5, 60.0);
        let start = std::time::Instant::now();
        let mut n = 0;
        Analyzer::new().feed(&s, |_, _| n += 1);
        let took = start.elapsed();
        println!("{n} frames for 60 s of audio in {took:?}: {:.3} % of one core", took.as_secs_f64() / 60.0 * 100.0);
    }
}
