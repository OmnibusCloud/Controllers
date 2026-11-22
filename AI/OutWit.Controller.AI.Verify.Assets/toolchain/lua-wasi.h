/*
 * Included in front of every Lua source file of the WASI build (-include).
 *
 * WASI preview 1 has no processes and no temporary files, and its C library does not
 * provide the three calls Lua's standard libraries make for them. They are stated here
 * as "not implemented" instead of faked: os.execute, os.tmpname and io.tmpfile return
 * nil and the system's message, and os.execute() without a command says that no shell
 * is available. Nothing else of Lua is changed.
 */
#ifndef LUA_WASI_H
#define LUA_WASI_H

#include <errno.h>
#include <stddef.h>
#include <stdio.h>
#include <stdlib.h>

#ifndef L_tmpnam
#define L_tmpnam 20
#endif

static inline int lua_wasi_system(const char *command)
{
    if (command == NULL)
        return 0;

    errno = ENOSYS;
    return -1;
}

static inline char *lua_wasi_tmpnam(char *buffer)
{
    (void)buffer;
    errno = ENOSYS;
    return NULL;
}

static inline FILE *lua_wasi_tmpfile(void)
{
    errno = ENOSYS;
    return NULL;
}

#define system lua_wasi_system
#define tmpnam lua_wasi_tmpnam
#define tmpfile lua_wasi_tmpfile

#endif
