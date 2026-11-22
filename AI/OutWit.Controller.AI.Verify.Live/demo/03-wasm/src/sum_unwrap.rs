// The sum of the integers on stdin, parsed as i32 and unwrapped: a value past i32 panics.
use std::io::Read;

fn main() {
    let mut text = String::new();
    std::io::stdin().read_to_string(&mut text).unwrap();
    let total: i64 = text.split_whitespace().map(|word| word.parse::<i32>().unwrap() as i64).sum();
    println!("{}", total);
}
