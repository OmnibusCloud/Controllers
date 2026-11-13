// The most frequent word of stdin, case-insensitive; a tie goes to the first in alphabetical order.
use std::collections::BTreeMap;
use std::io::Read;

fn main() {
    let mut text = String::new();
    std::io::stdin().read_to_string(&mut text).unwrap();
    let mut counts: BTreeMap<String, usize> = BTreeMap::new();
    for word in text.split(|c: char| !c.is_ascii_alphabetic()).filter(|word| !word.is_empty()) {
        *counts.entry(word.to_ascii_lowercase()).or_insert(0) += 1;
    }
    let mut best: Option<(&String, usize)> = None;
    for (word, &count) in &counts {
        if best.map_or(true, |(_, top)| count > top) {
            best = Some((word, count));
        }
    }
    if let Some((word, count)) = best {
        println!("{} {}", word, count);
    }
}
