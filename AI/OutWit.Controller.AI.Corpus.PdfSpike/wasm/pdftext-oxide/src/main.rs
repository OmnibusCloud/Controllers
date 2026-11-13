//! pdftext-oxide <in.pdf> <out.txt>: the text of a PDF by pdf_oxide (MIT OR Apache-2.0), pages
//! separated by a form feed, for the PDF spike's wasm route. A page that fails is left empty.

use std::env;
use std::fs;
use std::process::ExitCode;

fn main() -> ExitCode {
    let args: Vec<String> = env::args().collect();
    if args.len() != 3 {
        eprintln!("usage: pdftext-oxide <in.pdf> <out.txt>");
        return ExitCode::from(64);
    }

    let bytes = match fs::read(&args[1]) {
        Ok(bytes) => bytes,
        Err(error) => {
            eprintln!("read: {error}");
            return ExitCode::from(66);
        }
    };

    let document = match pdf_oxide::PdfDocument::from_bytes(bytes) {
        Ok(document) => document,
        Err(error) => {
            eprintln!("error: {error}");
            return ExitCode::from(2);
        }
    };

    let pages = match document.page_count() {
        Ok(pages) => pages,
        Err(error) => {
            eprintln!("error: {error}");
            return ExitCode::from(2);
        }
    };

    let mut text = String::new();
    for index in 0..pages {
        if index > 0 {
            text.push('\u{000C}');
        }
        if let Ok(page) = document.extract_text(index) {
            text.push_str(&page);
        }
    }

    match fs::write(&args[2], text) {
        Ok(()) => ExitCode::SUCCESS,
        Err(error) => {
            eprintln!("write: {error}");
            ExitCode::from(73)
        }
    }
}
