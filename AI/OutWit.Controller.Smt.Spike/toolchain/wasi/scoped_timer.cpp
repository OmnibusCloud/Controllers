/*++
Copyright (c) 2011 Microsoft Corporation

Module Name:

    scoped_timer.cpp (wasm32-wasip1 replacement, OmnibusCloud SMT spike)

Abstract:

    Z3's scoped timer starts a thread that fires an event handler when a wall-clock
    deadline passes (try-for tactics, timeout parameters, the -T switch). A wasm32-wasip1
    guest has no threads: wasi-libc's pthread_create answers ENOTSUP and it has no
    pthread_atfork at all. In the sandbox the guest's clock is also pinned (it moves only
    when the guest sleeps), so no deadline could pass anyway.

    This file replaces src/util/scoped_timer.cpp for the wasm build: a timer never starts
    and never fires. Every budget is the resource limit (rlimit, deterministic) and the
    host's fuel. Consequence, stated in the spike's report: a tactic wrapped in try-for runs
    until it finishes or the rlimit stops the whole check, where a native build would cut it
    off after its milliseconds and try the next one.

    The original is pinned by its SHA-256 in pins.env (Z3_SCOPED_TIMER_SHA256), so a Z3
    release that changes the timer fails the build instead of passing this shim.

--*/

#include "util/scoped_timer.h"

scoped_timer::scoped_timer(unsigned ms, event_handler * eh) {
    (void)ms;
    (void)eh;
}

scoped_timer::~scoped_timer() {
}

void scoped_timer::initialize() {
}

void scoped_timer::finalize() {
}

void scoped_timer::init_state(unsigned ms, event_handler * eh) {
    (void)ms;
    (void)eh;
}
