/*
 * The two ends of Lua's error jump in the WASI build (see lua-asyncify.h).
 *
 * lua_asyncify_throw starts an asyncify unwind: every instrumented frame above it saves its
 * locals into the buffer and returns at once. lua_asyncify_call is not instrumented (it is on
 * wasm-opt's remove-list); when the protected function returns while an unwind is in progress,
 * it stops the unwind, puts the C shadow stack pointer back where it was (the dropped frames
 * never ran their epilogues) and reports the error. The saved locals are never read: nothing
 * is rewound, which is exactly what longjmp does to the frames it jumps over.
 *
 * The buffer bounds how deep an error can be raised below its protected call. Lua bounds that
 * depth itself (200 nested C calls); the deepest unwind measured on Lua 5.4.8 took 71,812
 * bytes, so 256 KiB leaves a margin of 3.6. Past the buffer the run traps: a deterministic
 * runtime error, never a wrong result.
 */
#include <stddef.h>
#include <stdint.h>

#ifdef LUA_ASYNCIFY_STATS
#include <stdio.h>
#include <stdlib.h>
#endif

__attribute__((import_module("asyncify"), import_name("start_unwind")))
void lua_asyncify_start_unwind(void *data);

__attribute__((import_module("asyncify"), import_name("stop_unwind")))
void lua_asyncify_stop_unwind(void);

/* lua-asyncify-sp.S */
uintptr_t lua_asyncify_get_sp(void);
void lua_asyncify_set_sp(uintptr_t sp);

struct lua_State;
typedef void (*lua_asyncify_body)(struct lua_State *L, void *ud);

#ifndef LUA_ASYNCIFY_BUFFER
#define LUA_ASYNCIFY_BUFFER (256 * 1024)
#endif

/* Binaryen's layout: the current position, then the end. */
static struct
{
    unsigned char *current;
    unsigned char *end;
} unwind_data;

static unsigned char unwind_buffer[LUA_ASYNCIFY_BUFFER] __attribute__((aligned(16)));
static volatile int throwing;

#ifdef LUA_ASYNCIFY_STATS
static size_t high_water;
static unsigned long unwinds;
#endif

void lua_asyncify_throw(void)
{
    throwing = 1;
    unwind_data.current = unwind_buffer;
    unwind_data.end = unwind_buffer + sizeof unwind_buffer;
    lua_asyncify_start_unwind(&unwind_data);
}

int lua_asyncify_call(lua_asyncify_body f, struct lua_State *L, void *ud)
{
    uintptr_t sp = lua_asyncify_get_sp();

    f(L, ud);

    if (!throwing)
        return 0;

    lua_asyncify_stop_unwind();
    throwing = 0;
    lua_asyncify_set_sp(sp);

#ifdef LUA_ASYNCIFY_STATS
    {
        size_t used = (size_t)(unwind_data.current - unwind_buffer);
        if (used > high_water)
            high_water = used;
        unwinds++;
    }
#endif

    return 1;
}

#ifdef LUA_ASYNCIFY_STATS
/* The statistics build of the toolchain's gate: how deep the deepest unwind went. */
__attribute__((destructor)) static void lua_asyncify_report(void)
{
    if (getenv("LUA_ASYNCIFY_HWM") != NULL)
        fprintf(stderr, "asyncify unwinds=%lu high_water=%zu buffer=%d\n", unwinds, high_water, (int)LUA_ASYNCIFY_BUFFER);
}
#endif
