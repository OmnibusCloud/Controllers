/*
 * A raw-WASI fixture built by a real C toolchain: reads whitespace-separated integers
 * from stdin, adds the ones given as arguments, prints the count and the sum. With
 * "--exit N" as the first two arguments it exits with N instead; with "--grow" it
 * allocates until the allocator refuses and prints how many megabytes it was given.
 */
#include <stdio.h>
#include <stdlib.h>
#include <string.h>

int main(int argc, char **argv)
{
    if (argc >= 3 && strcmp(argv[1], "--exit") == 0)
        return atoi(argv[2]);

    if (argc >= 2 && strcmp(argv[1], "--grow") == 0)
    {
        long megabytes = 0;
        for (;;)
        {
            char *block = malloc(1024 * 1024);
            if (block == NULL)
                break;

            memset(block, 1, 1024 * 1024);
            megabytes++;
        }

        printf("%ld\n", megabytes);
        return 0;
    }

    long long sum = 0;
    long count = 0;
    long long value;

    while (scanf("%lld", &value) == 1)
    {
        sum += value;
        count++;
    }

    for (int i = 1; i < argc; i++)
    {
        sum += atoll(argv[i]);
        count++;
    }

    printf("%ld %lld\n", count, sum);
    return 0;
}
