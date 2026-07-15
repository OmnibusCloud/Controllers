# OutWit.Controller.AI.Verify

Sandboxed, deterministic, resource-metered execution of untrusted programs
against specifications — on anonymous volunteer nodes. One primitive
(`Verify.ExecuteBatch`) carries several products: code-vs-tests verification
(RLVR-style reward checking), code-corpus generation with compile/test
checking, math problem generation with CAS checking, and formal proof
checking — generation and verification are the same activity with different
programs.

**Status: v0.1.0-dev — project skeleton. No activities are implemented yet.**

Isolation targets WASM (wasmtime): deterministic, cross-platform
(Windows / Linux / macOS, x64 / arm64), no admin rights or Docker required on
contributor machines, no ambient authority — network and filesystem do not
exist inside the sandbox unless explicitly virtualized in. Verdicts are
verifiable by construction: code passes its test suite or it does not; a
proof checks or it does not.
