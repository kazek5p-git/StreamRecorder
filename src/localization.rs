use crate::models::Language;
use anyhow::{Context, Result};
use once_cell::sync::OnceCell;
use std::collections::HashMap;
use std::fs;
use std::path::Path;

#[derive(Debug)]
struct LocalizationState {
    language: Language,
    translations: HashMap<String, &'static str>,
}

static LOCALIZATION: OnceCell<LocalizationState> = OnceCell::new();

pub fn initialize(locale_dir: &Path, language: Language) -> Result<()> {
    let file_name = match language {
        Language::Polish => "pl.po",
        Language::English => "en.po",
    };

    let translations = load_po_file(&locale_dir.join(file_name))?;
    let _ = LOCALIZATION.set(LocalizationState {
        language,
        translations,
    });
    Ok(())
}

pub fn current_language() -> Language {
    LOCALIZATION
        .get()
        .map(|state| state.language.clone())
        .unwrap_or_default()
}

pub fn tr(key: &'static str) -> &'static str {
    LOCALIZATION
        .get()
        .and_then(|state| state.translations.get(key).copied())
        .unwrap_or(key)
}

fn load_po_file(path: &Path) -> Result<HashMap<String, &'static str>> {
    if !path.exists() {
        return Ok(HashMap::new());
    }

    let contents = fs::read_to_string(path)
        .with_context(|| format!("failed to read translation file {}", path.display()))?;

    Ok(parse_po(&contents))
}

fn parse_po(contents: &str) -> HashMap<String, &'static str> {
    let mut translations = HashMap::new();
    let mut current_id = String::new();
    let mut current_str = String::new();
    let mut reading_id = false;
    let mut reading_str = false;

    for line in contents.lines() {
        let trimmed = line.trim();

        if trimmed.is_empty() {
            flush_entry(&mut translations, &mut current_id, &mut current_str);
            reading_id = false;
            reading_str = false;
            continue;
        }

        if trimmed.starts_with('#') {
            continue;
        }

        if let Some(value) = trimmed.strip_prefix("msgid ") {
            flush_entry(&mut translations, &mut current_id, &mut current_str);
            current_id = parse_po_string(value);
            current_str.clear();
            reading_id = true;
            reading_str = false;
            continue;
        }

        if let Some(value) = trimmed.strip_prefix("msgstr ") {
            current_str = parse_po_string(value);
            reading_id = false;
            reading_str = true;
            continue;
        }

        if trimmed.starts_with('"') {
            let value = parse_po_string(trimmed);
            if reading_id {
                current_id.push_str(&value);
            } else if reading_str {
                current_str.push_str(&value);
            }
        }
    }

    flush_entry(&mut translations, &mut current_id, &mut current_str);
    translations
}

fn flush_entry(
    translations: &mut HashMap<String, &'static str>,
    current_id: &mut String,
    current_str: &mut String,
) {
    if current_id.is_empty() || current_str.is_empty() {
        current_id.clear();
        current_str.clear();
        return;
    }

    let leaked = Box::leak(current_str.clone().into_boxed_str());
    translations.insert(current_id.clone(), leaked);
    current_id.clear();
    current_str.clear();
}

fn parse_po_string(value: &str) -> String {
    let mut output = String::new();
    let value = value.trim();
    let value = value.strip_prefix('"').unwrap_or(value);
    let value = value.strip_suffix('"').unwrap_or(value);
    let mut chars = value.chars();

    while let Some(ch) = chars.next() {
        if ch != '\\' {
            output.push(ch);
            continue;
        }

        match chars.next() {
            Some('n') => output.push('\n'),
            Some('r') => output.push('\r'),
            Some('t') => output.push('\t'),
            Some('"') => output.push('"'),
            Some('\\') => output.push('\\'),
            Some(other) => {
                output.push('\\');
                output.push(other);
            }
            None => output.push('\\'),
        }
    }

    output
}
