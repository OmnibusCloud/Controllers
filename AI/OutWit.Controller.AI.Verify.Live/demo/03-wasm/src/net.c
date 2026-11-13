/* Asks the host for a network connection: an import the sandbox does not provide. */
#include <stdio.h>

__attribute__((import_module("env"), import_name("connect_host")))
extern int connect_host(const char *host, int port);

int main(void) {
  printf("%d\n", connect_host("example.com", 80));
  return 0;
}
