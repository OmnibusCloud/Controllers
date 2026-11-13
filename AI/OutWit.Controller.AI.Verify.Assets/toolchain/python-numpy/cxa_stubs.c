/* C++ throw without an exception runtime.
 *
 * numpy's C++ (pocketfft, the sort and casting templates) is compiled with -fexceptions. With
 * the WASI SDK CPython 3.14 builds with (24) and no -fwasm-exceptions, clang compiles every
 * catch away and leaves a throw as two calls, which that SDK's libc++abi does not define. Here
 * they allocate the exception and stop the guest: a C++ throw is a trap, never an unwind, so
 * the module carries no exception-handling instruction and runs with the feature off, as the
 * sandbox runs every module. numpy validates its arguments in Python or C before it reaches
 * C++ code that throws (pocketfft throws on plan sizes numpy checks first).
 *
 * The same shape as wasi-python's cxa_stubs.c (Apache-2.0, github.com/ClayGendron/wasi_python). */
#include <stdlib.h>
#include <unistd.h>

void *__cxa_allocate_exception(size_t size) {
    void *exception = malloc(size);
    if (exception == NULL)
        abort();
    return exception;
}

__attribute__((noreturn)) void __cxa_throw(void *exception, void *type, void (*destructor)(void *)) {
    static const char message[] = "Fatal Python error: a C++ exception was thrown, and this build cannot catch it\n";
    (void)exception;
    (void)type;
    (void)destructor;
    (void)write(2, message, sizeof message - 1);
    abort();
}
