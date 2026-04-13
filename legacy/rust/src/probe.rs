use crate::models::{StreamFormat, StreamProtocol};

#[derive(Debug, Clone)]
pub struct StreamProbe {
    pub protocol: StreamProtocol,
    pub format: StreamFormat,
    pub mime: Option<String>,
}

impl StreamProbe {
    pub fn extension(&self) -> &'static str {
        self.format.extension()
    }
}

pub fn probe_stream(url: &str, content_type: Option<&str>, first_bytes: &[u8]) -> StreamProbe {
    let content_type = content_type.map(normalize_content_type);

    let protocol = if is_hls(url, content_type.as_deref(), first_bytes) {
        StreamProtocol::Hls
    } else {
        StreamProtocol::Http
    };

    let format = detect_format(url, content_type.as_deref(), first_bytes);

    StreamProbe {
        protocol,
        format,
        mime: content_type,
    }
}

fn normalize_content_type(value: &str) -> String {
    value
        .split(';')
        .next()
        .unwrap_or(value)
        .trim()
        .to_ascii_lowercase()
}

fn is_hls(url: &str, content_type: Option<&str>, first_bytes: &[u8]) -> bool {
    url.to_ascii_lowercase().contains(".m3u8")
        || content_type.is_some_and(|mime| {
            mime.contains("application/vnd.apple.mpegurl")
                || mime.contains("application/x-mpegurl")
                || mime.contains("audio/mpegurl")
        })
        || first_bytes.starts_with(b"#EXTM3U")
}

fn detect_format(url: &str, content_type: Option<&str>, first_bytes: &[u8]) -> StreamFormat {
    if let Some(mime) = content_type {
        if mime.contains("audio/mpeg") || mime.contains("audio/mp3") {
            return StreamFormat::Mp3;
        }
        if mime.contains("audio/aac") || mime.contains("audio/aacp") {
            return StreamFormat::AacRaw;
        }
        if mime.contains("audio/ogg") || mime.contains("application/ogg") {
            return StreamFormat::Ogg;
        }
        if mime.contains("audio/flac") || mime.contains("application/flac") {
            return StreamFormat::Flac;
        }
        if mime.contains("audio/x-ms-wma")
            || mime.contains("audio/wma")
            || mime.contains("application/vnd.ms-asf")
        {
            return StreamFormat::Wma;
        }
        if mime.contains("audio/wav") || mime.contains("audio/x-wav") {
            return StreamFormat::Wav;
        }
        if mime.contains("video/mp2t") {
            return StreamFormat::MpegTs;
        }
    }

    if first_bytes.starts_with(b"OggS") {
        return StreamFormat::Ogg;
    }
    if first_bytes.starts_with(b"fLaC") {
        return StreamFormat::Flac;
    }
    if first_bytes.starts_with(b"RIFF") && first_bytes.get(8..12) == Some(b"WAVE".as_slice()) {
        return StreamFormat::Wav;
    }
    if first_bytes.starts_with(b"ID3") {
        return StreamFormat::Mp3;
    }
    if looks_like_adts(first_bytes) {
        return StreamFormat::AacRaw;
    }
    if looks_like_mpeg_ts(first_bytes) {
        return StreamFormat::MpegTs;
    }
    if first_bytes.starts_with(&[0x30, 0x26, 0xB2, 0x75, 0x8E, 0x66, 0xCF, 0x11]) {
        return StreamFormat::Wma;
    }

    let url_lower = url.to_ascii_lowercase();
    if url_lower.ends_with(".mp3") {
        return StreamFormat::Mp3;
    }
    if url_lower.ends_with(".aac") {
        return StreamFormat::AacRaw;
    }
    if url_lower.ends_with(".ogg") || url_lower.ends_with(".opus") {
        return StreamFormat::Ogg;
    }
    if url_lower.ends_with(".flac") {
        return StreamFormat::Flac;
    }
    if url_lower.ends_with(".wma") || url_lower.ends_with(".asf") {
        return StreamFormat::Wma;
    }
    if url_lower.ends_with(".wav") {
        return StreamFormat::Wav;
    }
    if url_lower.ends_with(".ts") {
        return StreamFormat::MpegTs;
    }

    StreamFormat::Unknown
}

fn looks_like_adts(bytes: &[u8]) -> bool {
    if bytes.len() < 2 {
        return false;
    }
    bytes[0] == 0xFF && (bytes[1] & 0xF6) == 0xF0
}

fn looks_like_mpeg_ts(bytes: &[u8]) -> bool {
    bytes.len() >= 376 && bytes[0] == 0x47 && bytes[188] == 0x47
}
