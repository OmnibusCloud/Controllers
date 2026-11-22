# A small numpy program of the kind a task runs: array arithmetic, a 100 x 100 linear solve,
# seeded random numbers, sorting. Every line it prints is the same on every run and platform.
import numpy as np

a = np.arange(12, dtype=np.float64).reshape(3, 4)
print("array:", a.sum(), a.mean(axis=0).tolist(), (a @ a.T).trace(), np.cumsum(a, axis=1)[-1].tolist())

rng = np.random.default_rng(12345)
m = rng.standard_normal((100, 100)) + 10 * np.eye(100)
b = rng.standard_normal(100)
x = np.linalg.solve(m, b)
print("solve:", bool(np.abs(m @ x - b).max() < 1e-10), repr(float(x.sum())), repr(float(np.linalg.det(m[:8, :8]))))

v = rng.integers(0, 1_000_000, size=100_000)
s = np.sort(v)
print("sort:", bool(np.all(s[:-1] <= s[1:])), int(s[0]), int(s[-1]), repr(float(np.median(s))), np.argsort(v[:8]).tolist())
print("unique:", np.unique(v % 7, return_counts=True)[1].tolist())

w = rng.normal(5.0, 2.0, size=10_000)
print("stats:", repr(float(w.mean())), repr(float(w.std())), np.percentile(w, [5, 50, 95]).round(6).tolist())
