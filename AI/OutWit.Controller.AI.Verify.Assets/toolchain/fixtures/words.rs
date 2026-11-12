// A raw-WASI fixture built by a real Rust toolchain: counts the words of stdin and prints
// them by frequency, then by name. With "--panic" as an argument it panics instead, which
// a WASI command reports as a trap.
use std::collections::BTreeMap;
use std::io::{self, Read, Write};

fn main() {
    if std::env::args().any(|argument| argument == "--panic") {
        panic!("asked to");
    }

    let mut text = String::new();
    io::stdin().read_to_string(&mut text).expect("stdin is text");

    let mut counts: BTreeMap<String, usize> = BTreeMap::new();
    for word in text.split_whitespace() {
        *counts.entry(word.to_lowercase()).or_insert(0) += 1;
    }

    let mut ordered: Vec<(&String, &usize)> = counts.iter().collect();
    ordered.sort_by(|left, right| right.1.cmp(left.1).then(left.0.cmp(right.0)));

    let stdout = io::stdout();
    let mut out = stdout.lock();
    for (word, count) in ordered {
        writeln!(out, "{count} {word}").expect("stdout is writable");
    }
}
