// The number of primes below the argument: the limit itself is left out.
fn main() {
    let limit: usize = std::env::args().nth(1).and_then(|arg| arg.parse().ok()).unwrap_or(0);
    let mut composite = vec![false; limit + 1];
    let mut count = 0;
    for i in 2..limit {
        if composite[i] {
            continue;
        }
        count += 1;
        if i > limit / i {
            continue; // usize is 32 bits on wasm32: i * i would overflow
        }
        let mut j = i * i;
        while j <= limit {
            composite[j] = true;
            j += i;
        }
    }
    println!("{}", count);
}
