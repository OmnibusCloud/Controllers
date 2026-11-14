"""A minimal UCI driver: one Stockfish process, one search thread, the session protocol the
chess controller is planned to use (handshake once; per search ucinewgame, isready, position,
go nodes). Standard library only, Python 3.9+.
"""

import os
import subprocess
import threading
import time

DEFAULT_OPTIONS = (
    ("Threads", "1"),
    ("Hash", "16"),
    ("NumaPolicy", "none"),
    ("UCI_ShowWDL", "true"),
)


class EngineError(Exception):
    pass


def _priority_kwargs(low_priority):
    if not low_priority:
        return {}
    if os.name == "nt":
        return {"creationflags": subprocess.BELOW_NORMAL_PRIORITY_CLASS}
    return {"preexec_fn": lambda: os.nice(10)}


def run_command(exe, args, prefix=None, low_priority=False, timeout=None):
    """Runs the engine with command-line arguments (bench, compiler, export_net) and returns
    (exit code, stdout, stderr, seconds)."""
    t0 = time.perf_counter()
    proc = subprocess.run(list(prefix or []) + [exe] + list(args), stdin=subprocess.DEVNULL,
                          stdout=subprocess.PIPE, stderr=subprocess.PIPE, timeout=timeout,
                          **_priority_kwargs(low_priority))
    seconds = time.perf_counter() - t0
    return (proc.returncode, proc.stdout.decode("ascii", "replace"),
            proc.stderr.decode("ascii", "replace"), seconds)


def parse_info(line):
    """Parses one 'info' line into a dict. Keys: depth, seldepth, multipv, score, bound, wdl,
    nodes, nps, hashfull, tbhits, time, pv, string, currmove."""
    tokens = line.split()
    info = {}
    i = 1
    n = len(tokens)
    while i < n:
        t = tokens[i]
        if t in ("depth", "seldepth", "multipv", "nodes", "nps", "hashfull", "tbhits", "time",
                 "currmovenumber"):
            info[t] = int(tokens[i + 1])
            i += 2
        elif t == "score":
            kind = tokens[i + 1]
            info["score"] = {kind: int(tokens[i + 2])}
            i += 3
            if i < n and tokens[i] in ("lowerbound", "upperbound"):
                info["bound"] = tokens[i]
                i += 1
        elif t == "wdl":
            info["wdl"] = [int(tokens[i + 1]), int(tokens[i + 2]), int(tokens[i + 3])]
            i += 4
        elif t == "pv":
            info["pv"] = tokens[i + 1:]
            break
        elif t == "string":
            info["string"] = " ".join(tokens[i + 1:])
            break
        elif t == "currmove":
            info["currmove"] = tokens[i + 1]
            i += 2
        else:
            i += 1
    return info


class Engine:
    """One engine process. Not thread-safe: one owner thread at a time."""

    def __init__(self, exe, multipv=1, prefix=None, low_priority=False, hash_mb=16,
                 search_timeout=None):
        self.exe = exe
        self.search_timeout = search_timeout
        self.tail = []
        self.info_strings = []
        t0 = time.perf_counter()
        self.proc = subprocess.Popen(list(prefix or []) + [exe], stdin=subprocess.PIPE,
                                     stdout=subprocess.PIPE, stderr=subprocess.DEVNULL,
                                     universal_newlines=True, encoding="ascii", errors="replace",
                                     bufsize=1, **_priority_kwargs(low_priority))
        self.send("uci")
        ident = self.read_until(lambda l: l == "uciok", keep=lambda l: l.startswith("id name"))
        self.id_name = ident[0][len("id name "):] if ident else ""
        for name, value in DEFAULT_OPTIONS:
            if name == "Hash":
                value = str(hash_mb)
            self.send("setoption name %s value %s" % (name, value))
        self.send("setoption name MultiPV value %d" % multipv)
        self.send("isready")
        self.read_until(lambda l: l == "readyok")
        self.start_seconds = time.perf_counter() - t0

    # -- plumbing ---------------------------------------------------------------------------

    def send(self, line):
        try:
            self.proc.stdin.write(line + "\n")
            self.proc.stdin.flush()
        except OSError as e:
            raise EngineError("write failed (%s); last output: %s" % (e, " | ".join(self.tail)))

    def readline(self):
        line = self.proc.stdout.readline()
        if not line:
            raise EngineError("engine exited (code %s); last output: %s"
                              % (self.proc.poll(), " | ".join(self.tail)))
        line = line.rstrip("\r\n")
        self.tail.append(line)
        if len(self.tail) > 50:
            del self.tail[:-50]
        return line

    def read_until(self, done, keep=None):
        kept = []
        while True:
            line = self.readline()
            if done(line):
                return kept
            if keep is not None and keep(line):
                kept.append(line)

    def close(self):
        try:
            self.send("quit")
            self.proc.wait(timeout=10)
        except Exception:
            self.proc.kill()
            self.proc.wait()

    def __enter__(self):
        return self

    def __exit__(self, *exc):
        self.close()

    # -- protocol ---------------------------------------------------------------------------

    def new_game(self):
        """ucinewgame + isready; returns the seconds until readyok (clears hash and histories)."""
        t0 = time.perf_counter()
        self.send("ucinewgame")
        self.send("isready")
        self.read_until(lambda l: l == "readyok")
        return time.perf_counter() - t0

    def set_position(self, fen, moves=()):
        cmd = "position fen " + fen
        if moves:
            cmd += " moves " + " ".join(moves)
        self.send(cmd)

    def legal_moves(self, fen, moves=()):
        """The legal moves of the position, from 'go perft 1', sorted by UCI string."""
        self.set_position(fen, moves)
        self.send("go perft 1")
        found = self.read_until(lambda l: l.startswith("Nodes searched"),
                                keep=lambda l: ": " in l and not l.startswith("info"))
        return sorted(l.split(":")[0].strip() for l in found)

    def search(self, fen, moves, nodes):
        """One search from cleared state. Returns a dict with the last info line per multipv
        index, bestmove, ponder, and timings."""
        newgame_seconds = self.new_game()
        self.set_position(fen, moves)
        lines = {}
        timer = None
        if self.search_timeout:
            timer = threading.Timer(self.search_timeout, self.proc.kill)
            timer.start()
        t0 = time.perf_counter()
        try:
            self.send("go nodes %d" % nodes)
            while True:
                line = self.readline()
                if line.startswith("bestmove"):
                    parts = line.split()
                    best = parts[1] if len(parts) > 1 else None
                    ponder = parts[3] if len(parts) > 3 and parts[2] == "ponder" else None
                    break
                if line.startswith("info string "):
                    if len(self.info_strings) < 20:
                        self.info_strings.append(line[len("info string "):])
                elif line.startswith("info") and " score " in line:
                    info = parse_info(line)
                    lines[info.get("multipv", 1)] = info
        finally:
            if timer is not None:
                timer.cancel()
        search_seconds = time.perf_counter() - t0
        if best == "(none)":
            best = None
        return {"lines": [lines[k] for k in sorted(lines)], "best": best, "ponder": ponder,
                "newgame_seconds": newgame_seconds, "search_seconds": search_seconds}

    def fen_after(self, fen, moves=()):
        """The engine's FEN of a position (via 'd') and whether the side to move is in check."""
        self.set_position(fen, moves)
        self.send("d")
        out_fen = None
        checkers = ""
        while True:
            line = self.readline()
            if line.startswith("Fen: "):
                out_fen = line[5:].strip()
            elif line.startswith("Checkers:"):
                checkers = line[len("Checkers:"):].strip()
                break
        return out_fen, bool(checkers)
