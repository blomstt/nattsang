//! The audio output: PulseAudio, as librespot's own PulseAudio sink does it
//! (16-bit, 44.1 kHz stereo, the server's buffering, drained on pause),
//! with the visualizer's tap on the way. The samples go out unchanged; while
//! the window has asked for levels (LEVELS), the same samples are measured
//! (levels.rs) and each frame sent as `levels`, stamped with when it will
//! be heard: now, plus what PulseAudio still has to play, less how far
//! before the end of what was just written the frame's middle is.
//! Nothing is measured while paused (librespot writes nothing then) or
//! while the levels are off.
//!
//! The volume is Nattsang's own (GAIN, from the `volume` command): the
//! samples are multiplied by it on their way out, after they're measured,
//! so the visualizer shows the music at any volume. The system's volume,
//! and the stream's own in the system's mixer, are left alone. PulseAudio
//! is asked to keep TARGET of sound buffered, not its usual two seconds,
//! so a change of volume (and a pause) is heard within that.

use std::sync::atomic::{AtomicBool, AtomicU64, Ordering};

use libpulse_binding::{def::BufferAttr, sample, stream::Direction};
use libpulse_simple_binding::Simple;
use librespot_playback::{
    NUM_CHANNELS, SAMPLE_RATE,
    audio_backend::{Sink, SinkError, SinkResult},
    convert::Converter,
    decoder::AudioPacket,
};

use crate::levels::{Analyzer, BANDS, RATE, SIZE};

/// The window wants levels (`levels 1`), set from the command loop.
pub static LEVELS: AtomicBool = AtomicBool::new(false);

/// What the samples are multiplied by, 0 to 1 (an f64's bits), set from
/// the command loop (`volume`); 1 until the window says otherwise.
static GAIN: AtomicU64 = AtomicU64::new(0x3ff0_0000_0000_0000); // 1.0

pub fn set_gain(gain: f64) {
    GAIN.store(gain.clamp(0.0, 1.0).to_bits(), Ordering::Relaxed);
}

fn gain() -> f64 {
    f64::from_bits(GAIN.load(Ordering::Relaxed))
}

/// How much sound PulseAudio keeps buffered, in seconds.
const TARGET: f64 = 0.2;

/// The samples at the volume: themselves at full, else a copy multiplied.
fn at_volume(samples: &[f64], gain: f64) -> std::borrow::Cow<'_, [f64]> {
    if gain >= 1.0 {
        std::borrow::Cow::Borrowed(samples)
    } else {
        std::borrow::Cow::Owned(samples.iter().map(|s| s * gain).collect())
    }
}

pub struct Tap {
    pulse: Option<Simple>,
    app_name: String,
    stream_desc: String,
    analyzer: Analyzer,
    measuring: bool,
}

impl Tap {
    pub fn new() -> Self {
        Tap {
            pulse: None,
            app_name: std::env::var("PULSE_PROP_application.name").unwrap_or_default(),
            stream_desc: std::env::var("PULSE_PROP_stream.description").unwrap_or_default(),
            analyzer: Analyzer::new(),
            measuring: false,
        }
    }

    // Measures what was just written: frames heard `latency_us` after now
    // at the end of these samples.
    fn measure(&mut self, samples: &[f64], latency_us: u64) {
        let now = monotonic_us();
        let pairs = samples.len() / NUM_CHANNELS as usize;
        let per_sample = 1e6 / RATE as f64;
        self.analyzer.feed(samples, |taken, levels: &[u8; BANDS]| {
            let before_end = (pairs - taken + SIZE / 2) as f64 * per_sample;
            let heard = (now + latency_us) as f64 - before_end;
            let mut fields = Vec::with_capacity(BANDS + 2);
            fields.push("levels".to_string());
            fields.push((heard.max(0.0) as u64).to_string());
            fields.extend(levels.iter().map(|l| l.to_string()));
            crate::emit(&fields.iter().map(String::as_str).collect::<Vec<_>>());
        });
    }
}

impl Sink for Tap {
    fn start(&mut self) -> SinkResult<()> {
        if self.pulse.is_none() {
            let spec = sample::Spec { format: sample::Format::S16NE, channels: NUM_CHANNELS, rate: SAMPLE_RATE };
            // The server's own choice for all but the target length.
            let bytes = (TARGET * SAMPLE_RATE as f64) as u32 * NUM_CHANNELS as u32 * 2;
            let attr = BufferAttr { maxlength: u32::MAX, tlength: bytes, prebuf: u32::MAX, minreq: u32::MAX, fragsize: u32::MAX };
            let pulse = Simple::new(None, &self.app_name, Direction::Playback, None, &self.stream_desc, &spec, None, Some(&attr))
                .map_err(|e| SinkError::ConnectionRefused(e.to_string().unwrap_or_default()))?;
            self.pulse = Some(pulse);
        }
        Ok(())
    }

    // Paused or stopped: what's buffered plays out, then the stream closes.
    fn stop(&mut self) -> SinkResult<()> {
        self.analyzer.reset();
        let pulse = self.pulse.take().ok_or_else(|| SinkError::NotConnected("not started".into()))?;
        pulse.drain().map_err(|e| SinkError::OnWrite(e.to_string().unwrap_or_default()))
    }

    fn write(&mut self, packet: AudioPacket, converter: &mut Converter) -> SinkResult<()> {
        let pulse = self.pulse.as_ref().ok_or_else(|| SinkError::NotConnected("not started".into()))?;
        let written = match &packet {
            AudioPacket::Samples(samples) => {
                let s16 = converter.f64_to_s16(&at_volume(samples, gain()));
                // SAFETY: an i16 slice seen as the bytes it's made of, native order, as PulseAudio wants them.
                let bytes = unsafe { std::slice::from_raw_parts(s16.as_ptr().cast::<u8>(), s16.len() * 2) };
                pulse.write(bytes)
            }
            AudioPacket::Raw(bytes) => pulse.write(bytes),
        };
        written.map_err(|e| SinkError::OnWrite(e.to_string().unwrap_or_default()))?;

        let wanted = LEVELS.load(Ordering::Relaxed);
        if wanted != self.measuring {
            self.measuring = wanted;
            self.analyzer.reset();
        }
        if let (true, AudioPacket::Samples(samples)) = (wanted, &packet) {
            let latency = pulse.get_latency().map(|l| l.0).unwrap_or(0);
            self.measure(samples, latency);
        }
        Ok(())
    }
}

// Microseconds on CLOCK_MONOTONIC, the clock the window's frames run on
// (g_get_monotonic_time).
fn monotonic_us() -> u64 {
    let mut t = libc::timespec { tv_sec: 0, tv_nsec: 0 };
    unsafe { libc::clock_gettime(libc::CLOCK_MONOTONIC, &mut t) };
    t.tv_sec as u64 * 1_000_000 + t.tv_nsec as u64 / 1000
}

#[cfg(test)]
mod tests {
    use super::*;

    #[test]
    fn full_volume_leaves_the_samples_alone() {
        let samples = [0.5, -0.25, 1.0];
        assert!(matches!(at_volume(&samples, 1.0), std::borrow::Cow::Borrowed(_)));
    }

    #[test]
    fn volume_multiplies_the_samples() {
        let samples = [0.5, -0.25, 1.0];
        assert_eq!(&*at_volume(&samples, 0.5), &[0.25, -0.125, 0.5]);
        assert_eq!(&*at_volume(&samples, 0.0), &[0.0, 0.0, 0.0]);
    }

    #[test]
    fn gain_is_kept_within_0_and_1() {
        set_gain(2.0);
        assert_eq!(gain(), 1.0);
        set_gain(-1.0);
        assert_eq!(gain(), 0.0);
        set_gain(0.343);
        assert_eq!(gain(), 0.343);
        set_gain(1.0);
    }
}
