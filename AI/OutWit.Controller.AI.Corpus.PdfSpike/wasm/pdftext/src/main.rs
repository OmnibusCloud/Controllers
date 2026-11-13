//! pdftext <in.pdf> <out.txt>: the text of a PDF by pdf-extract (MIT), for the PDF spike's
//! wasm route. Runs under wasmtime with fuel, a memory ceiling and a deadline; a panic in the
//! library aborts the instance (a trap), never the host.

use std::env;
use std::fs;
use std::process::ExitCode;

fn main() -> ExitCode {
    let args: Vec<String> = env::args().collect();
    if args.len() != 3 {
        eprintln!("usage: pdftext <in.pdf> <out.txt>");
        return ExitCode::from(64);
    }

    let bytes = match fs::read(&args[1]) {
        Ok(bytes) => bytes,
        Err(error) => {
            eprintln!("read: {error}");
            return ExitCode::from(66);
        }
    };

    match pdf_extract::extract_text_from_mem(&bytes) {
        Ok(text) => match fs::write(&args[2], text) {
            Ok(()) => ExitCode::SUCCESS,
            Err(error) => {
                eprintln!("write: {error}");
                ExitCode::from(73)
            }
        },
        Err(error) => {
            eprintln!("error: {error}");
            ExitCode::from(2)
        }
    }
}
