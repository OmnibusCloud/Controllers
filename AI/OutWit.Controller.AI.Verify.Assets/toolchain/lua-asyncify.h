/*
 * Included in front of every Lua source file of the asyncify build (-include, after
 * lua-wasi.h). Lua raises an error by a non-local jump to the nearest protected call
 * (LUAI_THROW / LUAI_TRY in ldo.c). Here the jump is an asyncify unwind that is stopped
 * at that protected call and never rewound: the frames in between are dropped, exactly
 * what longjmp does, and the module needs no exception-handling feature.
 *
 * LUAI_TRY appears once in Lua 5.4 (luaD_rawrunprotected) and its block is "(*f)(L, ud);".
 * The macro calls that function itself, through lua_asyncify_call, which lives in its own
 * file (no inlining into an instrumented caller) and is left out of the instrumentation.
 */
#ifndef LUA_ASYNCIFY_H
#define LUA_ASYNCIFY_H

/* ldo.c includes setjmp.h, which refuses to compile without exception handling; nothing
   of it is used once LUAI_THROW / LUAI_TRY are ours. */
#define _SETJMP_H 1

struct lua_State;
typedef void (*lua_asyncify_body)(struct lua_State *L, void *ud);

void lua_asyncify_throw(void);
int lua_asyncify_call(lua_asyncify_body f, struct lua_State *L, void *ud);

#define LUAI_THROW(L,c)   lua_asyncify_throw()
#define LUAI_TRY(L,c,a)   if (lua_asyncify_call(f, L, ud)) { if ((c)->status == 0) (c)->status = -1; }
#define luai_jmpbuf       int

#endif
