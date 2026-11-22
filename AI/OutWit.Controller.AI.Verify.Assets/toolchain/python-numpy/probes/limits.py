# What of numpy works under WASI in this build, one line per area. Nothing here may stop the
# guest: each area is tried and its answer or its exception printed.
import io
import sys

import numpy as np


def area(name, function):
    try:
        print(f"{name}: {function()}")
    except BaseException as error:  # noqa: BLE001 - the answer is the exception
        print(f"{name}: {type(error).__name__}: {error}")


def builtins():
    import _wasinumpy

    return f"{len(_wasinumpy.modules)} modules built in, numpy {_wasinumpy.numpy_version}"


def config():
    config = np.show_config(mode="dicts")
    deps = config["Build Dependencies"]
    return f"blas {deps['blas'].get('name')!r}, lapack {deps['lapack'].get('name')!r}, host {config['Machine Information']['host']}"


def integers():
    return (f"default int {np.dtype(int)}, intp {np.dtype(np.intp)}, arange(1e6).sum() {np.arange(1_000_000).sum()} "
            f"(int64: {np.arange(1_000_000, dtype=np.int64).sum()})")


def longdouble():
    return f"{np.finfo(np.longdouble).bits} bits, eps {np.finfo(np.longdouble).eps}, 1/3 = {np.longdouble(1) / 3!r}"


def fft():
    return np.round(np.fft.rfft(np.arange(8.0)), 6).tolist()


def singular():
    np.linalg.inv(np.zeros((2, 2)))
    return "no error"


def eig_svd_lstsq():
    m = np.array([[2.0, 1.0], [1.0, 3.0]])
    return (np.linalg.eigvalsh(m).round(9).tolist(), np.linalg.svd(m, compute_uv=False).round(9).tolist(),
            np.linalg.lstsq(m, np.array([1.0, 2.0]), rcond=None)[0].round(9).tolist(), round(float(np.linalg.det(m)), 9))


def float_errors():
    with np.errstate(all="raise"):
        try:
            np.array([1.0]) / np.array([0.0])
            divide = "silent"
        except FloatingPointError as error:
            divide = f"FloatingPointError {error}"
        try:
            np.array([1], dtype=np.int64) // np.array([0], dtype=np.int64)
            integer = "silent"
        except FloatingPointError as error:
            integer = f"FloatingPointError {error}"
    return f"float 1/0: {divide}; integer 1//0: {integer}"


def save_load():
    buffer = io.BytesIO()
    np.save(buffer, np.arange(5))
    buffer.seek(0)
    return np.load(buffer).tolist()


def random_apis():
    np.random.seed(1)
    legacy = (np.random.rand(2).round(9).tolist(), np.random.poisson(3, 4).tolist(), np.random.randint(0, 9, 4).tolist())
    g = np.random.default_rng(1)
    modern = (g.poisson(3, 4).tolist(), g.integers(0, 9, 4).tolist(), g.multinomial(10, [0.2, 0.3, 0.5]).tolist(),
              g.choice(5, 3, replace=False).tolist())
    return f"legacy {legacy}, Generator {modern}"


def unseeded():
    return f"{np.random.default_rng().integers(0, 10**9)} {np.random.randint(0, 10**9)}"


def threads():
    import threading

    t = threading.Thread(target=lambda: None)
    t.start()
    t.join()
    return "a thread ran"


def strings_dates():
    return (np.char.upper(np.array(["ab", "cd"])).tolist(), np.strings.str_len(np.array(["abc", "d"])).tolist(),
            str(np.datetime64("2026-11-13") + np.timedelta64(30, "D")))


def polynomial():
    return np.polynomial.Polynomial([-6.0, 1.0, 1.0]).roots().round(9).tolist()


def masked():
    return str(np.ma.masked_array([1, 2, 3], mask=[0, 1, 0]).sum())


def testing():
    np.testing.assert_allclose([1.0, 2.0], [1.0, 2.0 + 1e-12])
    return "assert_allclose ok"


def ctypes_interface():
    return f"ctypeslib.ctypes {np.ctypeslib.ctypes}; ndarray.ctypes {type(np.zeros(1).ctypes).__name__}"


def big_array():
    a = np.ones((2000, 2000))
    return f"{a.nbytes // 2**20} MiB array, sum {a.sum()}"


def printing():
    return np.array2string(np.linspace(0, 1, 5), precision=3) + " " + repr(np.float32(0.1)) + " " + repr(np.float64(0.1))


area("builtins", builtins)
area("config", config)
area("integers", integers)
area("longdouble", longdouble)
area("fft", fft)
area("singular inv", singular)
area("eig/svd/lstsq/det", eig_svd_lstsq)
area("float errors", float_errors)
area("save/load", save_load)
area("random", random_apis)
area("unseeded random", unseeded)
area("threads", threads)
area("strings/dates", strings_dates)
area("polynomial", polynomial)
area("masked", masked)
area("testing", testing)
area("ctypes", ctypes_interface)
area("30 MiB array", big_array)
area("printing", printing)
print("platform:", sys.platform, sys.version.split()[0], sys.hash_info.algorithm)
