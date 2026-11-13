/* The number of primes up to the argument: a sieve. */
#include <stdio.h>
#include <stdlib.h>

int main(int argc, char **argv) {
  long limit = argc > 1 ? atol(argv[1]) : 0, count = 0;
  char *composite = calloc((size_t)limit + 1, 1);
  for (long i = 2; i <= limit; i++) {
    if (composite[i])
      continue;
    count++;
    for (long long j = (long long)i * i; j <= limit; j += i)
      composite[j] = 1;
  }
  printf("%ld\n", count);
  free(composite);
  return 0;
}
