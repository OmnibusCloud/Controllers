/* The sum of the integers on stdin, 64-bit. */
#include <stdio.h>

int main(void) {
  long long total = 0, value;
  while (scanf("%lld", &value) == 1)
    total += value;
  printf("%lld\n", total);
  return 0;
}
