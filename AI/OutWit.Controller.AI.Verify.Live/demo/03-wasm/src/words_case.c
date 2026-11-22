/* The most frequent word of stdin, case kept as written (the bug); a tie goes to the first in alphabetical order. */
#include <ctype.h>
#include <stdio.h>
#include <string.h>

#define MAX_WORDS 4096
#define MAX_LENGTH 64

static char words[MAX_WORDS][MAX_LENGTH];
static int counts[MAX_WORDS];
static int distinct;

static void add(const char *word) {
  for (int i = 0; i < distinct; i++)
    if (strcmp(words[i], word) == 0) {
      counts[i]++;
      return;
    }
  if (distinct < MAX_WORDS) {
    strcpy(words[distinct], word);
    counts[distinct++] = 1;
  }
}

int main(void) {
  char word[MAX_LENGTH];
  int length = 0, c;
  while ((c = getchar()) != EOF) {
    if (isalpha(c) && c < 128) {
      if (length < MAX_LENGTH - 1)
        word[length++] = (char)c;
    } else if (length > 0) {
      word[length] = 0;
      add(word);
      length = 0;
    }
  }
  if (length > 0) {
    word[length] = 0;
    add(word);
  }
  int best = -1;
  for (int i = 0; i < distinct; i++)
    if (best < 0 || counts[i] > counts[best] || (counts[i] == counts[best] && strcmp(words[i], words[best]) < 0))
      best = i;
  if (best >= 0)
    printf("%s %d\n", words[best], counts[best]);
  return 0;
}
