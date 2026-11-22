/* The sum of the integers on stdin, with an int total: a large sum wraps. */
#include <stdio.h>

int main(void) {
  int total = 0;
  long long value;
  while (scanf("%lld", &value) == 1)
    total += (int)value;
  printf("%d\n", total);
  return 0;
}
