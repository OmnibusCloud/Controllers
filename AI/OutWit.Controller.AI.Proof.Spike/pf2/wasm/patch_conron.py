"""PF-2 spike: the two changes con-ron needs to build and run for wasm32-wasip1, applied to a
checkout (nothing of con-ron is kept in this repository).

    patch_conron.py <con-ron checkout>

1. `con-ron-dump`'s default allocator is mimalloc, a C library with no WASI build here: the
   dependency is taken without default features (the system allocator, as the crate's own
   comment says `--no-default-features` does).
2. The checker runs on a spawned thread with a 1 GiB stack; WASI preview 1 has no threads, so
   under `target_family = "wasm"` it runs on the main thread instead (the wasm stack is set by the
   linker and the runtime). The verdict code is the same function's.
"""
from __future__ import annotations

import os
import sys


def replace_once(path: str, old: str, new: str) -> None:
    with open(path, encoding="utf-8") as handle:
        text = handle.read()
    if text.count(old) != 1:
        raise SystemExit(f"{path}: the text to patch occurs {text.count(old)} times")
    with open(path, "w", encoding="utf-8", newline="\n") as handle:
        handle.write(text.replace(old, new))


def main() -> int:
    root = sys.argv[1]
    replace_once(os.path.join(root, "crates", "con-ron", "Cargo.toml"),
                 'con-ron-dump = { path = "../con-ron-dump" }',
                 'con-ron-dump = { path = "../con-ron-dump", default-features = false }')
    replace_once(os.path.join(root, "crates", "con-ron", "src", "bin", "con-ron.rs"),
                 "    let file = a.files[0].clone();\n    let h = std::thread::Builder::new()",
                 "    let file = a.files[0].clone();\n"
                 "    #[cfg(target_family = \"wasm\")]\n"
                 "    {\n"
                 "        return ExitCode::from(check_main(&a, &file));\n"
                 "    }\n"
                 "    #[allow(unreachable_code)]\n"
                 "    let h = std::thread::Builder::new()")
    print("patched", root)
    return 0


if __name__ == "__main__":
    sys.exit(main())
