/* The number of primes up to the argument: trial division by every smaller number. */
#include <stdio.h>
#include <stdlib.h>

int main(int argc, char **argv) {
  long limit = argc > 1 ? atol(argv[1]) : 0, count = 0;
  for (long n = 2; n <= limit; n++) {
    int prime = 1;
    for (long d = 2; d < n; d++)
      if (n % d == 0) {
        prime = 0;
        break;
      }
    count += prime;
  }
  printf("%ld\n", count);
  return 0;
}
