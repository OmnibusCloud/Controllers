/* Starts a thread: built for wasm32-wasip1-threads, with a shared memory the sandbox refuses. */
#include <pthread.h>
#include <stdio.h>

static void *work(void *argument) {
  *(int *)argument *= 2;
  return NULL;
}

int main(void) {
  pthread_t thread;
  int value = 21;
  pthread_create(&thread, NULL, work, &value);
  pthread_join(thread, NULL);
  printf("%d\n", value);
  return 0;
}
