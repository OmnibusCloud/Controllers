/*
 * Reads and writes the C shadow stack pointer for lua-asyncify.c: an unwind drops frames
 * without running their epilogues, so the protected call puts the pointer back itself (the
 * same thing Ruby's WASI build does in machine_core.S). Preprocessed by clang (.S).
 */
	.globaltype	__stack_pointer, i32

	.section	.text.lua_asyncify_get_sp,"",@
	.globl	lua_asyncify_get_sp
	.type	lua_asyncify_get_sp,@function
lua_asyncify_get_sp:
	.functype	lua_asyncify_get_sp () -> (i32)
	global.get	__stack_pointer
	end_function

	.section	.text.lua_asyncify_set_sp,"",@
	.globl	lua_asyncify_set_sp
	.type	lua_asyncify_set_sp,@function
lua_asyncify_set_sp:
	.functype	lua_asyncify_set_sp (i32) -> ()
	local.get	0
	global.set	__stack_pointer
	end_function
