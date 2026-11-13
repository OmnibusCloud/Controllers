// The sum of the integers on stdin, 64-bit.
use std::io::Read;

fn main() {
    let mut text = String::new();
    std::io::stdin().read_to_string(&mut text).unwrap();
    let total: i64 = text.split_whitespace().map(|word| word.parse::<i64>().unwrap()).sum();
    println!("{}", total);
}
