//! pdftext: the text of one PDF, for the PDF stage of the Corpus controller.
//!
//! A WASI command module (wasm32-wasip1) that the controller's sandbox runs once per
//! document, with the document's folder preopened as /in and an empty folder as /out:
//!
//! ```text
//! pdftext /in/doc.pdf /out
//! ```
//!
//! - /out/text.txt: the text, UTF-8, LF line ends, U+000C between pages and nothing after
//!   the last page, pages in document order. Written only when the exit code is 0.
//! - /out/result.json: `{"pages":N,"encrypted":B,"error":null}`, or with `"error"` one of
//!   `"encrypted"` (a password is needed), `"unreadable"` (not a PDF this reader can open
//!   or count the pages of), `"no_pages"`. Written last, after the text.
//! - exit 0: the text is written (a page that fails to extract is an empty page); 2: the
//!   document is refused with the code in result.json; anything else (64 usage, 66 the
//!   input cannot be read, 73 an output cannot be written) is an internal failure.
//!
//! The extraction is pdf_oxide 0.3.78 (MIT OR Apache-2.0): `PdfDocument::extract_text`
//! page by page. The output is a function of the input bytes alone. The host bounds the
//! run (fuel, a linear-memory ceiling); nothing here reads the clock or depends on
//! threads, and the environment is emptied before the library runs (see
//! `clear_environment`).

use std::ffi::OsString;
use std::fs;
use std::path::Path;
use std::process::ExitCode;

use pdf_oxide::{Error, PdfDocument};

/// The exit code of a document refused with a code.
const EXIT_REFUSED: u8 = 2;

/// The exit code of a wrong command line (sysexits EX_USAGE).
const EXIT_USAGE: u8 = 64;

/// The exit code of an input that cannot be read (sysexits EX_NOINPUT).
const EXIT_NO_INPUT: u8 = 66;

/// The exit code of an output that cannot be written (sysexits EX_CANTCREAT).
const EXIT_CANT_CREATE: u8 = 73;

/// What separates two pages in text.txt.
const PAGE_BREAK: char = '\u{000C}';

const TEXT_FILE: &str = "text.txt";
const RESULT_FILE: &str = "result.json";

/// Why a document gives no text: the codes of result.json's "error".
#[derive(Clone, Copy)]
enum Refusal {
    Encrypted,
    Unreadable,
    NoPages,
}

impl Refusal {
    fn code(self) -> &'static str {
        match self {
            Refusal::Encrypted => "encrypted",
            Refusal::Unreadable => "unreadable",
            Refusal::NoPages => "no_pages",
        }
    }
}

/// What one run found: the page count, whether the document carries an /Encrypt
/// dictionary, and either the text or the refusal.
struct Extraction {
    pages: usize,
    encrypted: bool,
    text: Result<String, Refusal>,
}

impl Extraction {
    fn refused(refusal: Refusal, encrypted: bool) -> Self {
        Extraction { pages: 0, encrypted, text: Err(refusal) }
    }

    /// result.json: three fields in a fixed order, no spaces, one line.
    fn result_json(&self) -> String {
        let error = match &self.text {
            Ok(_) => "null".to_string(),
            Err(refusal) => format!("\"{}\"", refusal.code()),
        };
        format!("{{\"pages\":{},\"encrypted\":{},\"error\":{}}}\n", self.pages, self.encrypted, error)
    }
}

fn main() -> ExitCode {
    clear_environment();

    let args: Vec<OsString> = std::env::args_os().collect();
    if args.len() != 3 {
        eprintln!("usage: pdftext <in.pdf> <out-dir>");
        return ExitCode::from(EXIT_USAGE);
    }

    let input = Path::new(&args[1]);
    let output = Path::new(&args[2]);

    let bytes = match fs::read(input) {
        Ok(bytes) => bytes,
        Err(error) => {
            eprintln!("pdftext: cannot read {}: {error}", input.display());
            return ExitCode::from(EXIT_NO_INPUT);
        }
    };

    let extraction = extract(bytes);

    if let Ok(text) = &extraction.text {
        if let Err(error) = fs::write(output.join(TEXT_FILE), text.as_bytes()) {
            eprintln!("pdftext: cannot write {TEXT_FILE}: {error}");
            return ExitCode::from(EXIT_CANT_CREATE);
        }
    }

    if let Err(error) = fs::write(output.join(RESULT_FILE), extraction.result_json()) {
        eprintln!("pdftext: cannot write {RESULT_FILE}: {error}");
        return ExitCode::from(EXIT_CANT_CREATE);
    }

    match extraction.text {
        Ok(_) => ExitCode::SUCCESS,
        Err(_) => ExitCode::from(EXIT_REFUSED),
    }
}

/// Opens the document and extracts every page.
///
/// An encrypted document is read when the empty user password opens it (pdf_oxide tries it
/// when the encryption is first touched) and refused as "encrypted" otherwise, as it is when
/// its security handler is one the library cannot run. A page whose extraction fails is an
/// empty page: the document still has its page count, and its other pages keep their text.
fn extract(bytes: Vec<u8>) -> Extraction {
    let document = match PdfDocument::from_bytes(bytes) {
        Ok(document) => document,
        Err(Error::EncryptedPdf) => return Extraction::refused(Refusal::Encrypted, true),
        Err(_) => return Extraction::refused(Refusal::Unreadable, false),
    };

    let encrypted = document.is_encrypted();
    if encrypted && !document.is_authenticated() {
        return Extraction::refused(Refusal::Encrypted, true);
    }

    let pages = match document.page_count() {
        Ok(pages) => pages,
        Err(Error::EncryptedPdf) => return Extraction::refused(Refusal::Encrypted, true),
        Err(_) => return Extraction::refused(Refusal::Unreadable, encrypted),
    };
    if pages == 0 {
        return Extraction::refused(Refusal::NoPages, encrypted);
    }

    let mut text = String::new();
    for index in 0..pages {
        if index > 0 {
            text.push(PAGE_BREAK);
        }
        if let Ok(page) = document.extract_text(index) {
            push_page(&mut text, &page);
        }
    }

    Extraction { pages, encrypted, text: Ok(text) }
}

/// Appends one page's text with the contract's line ends: CR LF and a lone CR become LF,
/// and a form feed inside a page (a glyph mapped to U+000C) becomes LF, so U+000C in
/// text.txt only ever separates pages.
fn push_page(text: &mut String, page: &str) {
    let mut characters = page.chars().peekable();
    while let Some(character) = characters.next() {
        match character {
            '\r' => {
                if characters.peek() == Some(&'\n') {
                    characters.next();
                }
                text.push('\n');
            }
            PAGE_BREAK => text.push('\n'),
            other => text.push(other),
        }
    }
}

/// Empties the process environment before the library runs.
///
/// pdf_oxide reads `PDF_OXIDE_MAX_DECOMPRESS_MB` (decoders/flate.rs) to change its cap on
/// one Flate stream's output (256 MiB by default): a variable set by a host would change
/// which streams decode and so the text. The sandbox passes no environment; emptying it
/// here keeps the output a function of the input bytes under any host, the wasmtime
/// command line included.
fn clear_environment() {
    let names: Vec<OsString> = std::env::vars_os().map(|(name, _)| name).collect();
    for name in names {
        std::env::remove_var(name);
    }
}
