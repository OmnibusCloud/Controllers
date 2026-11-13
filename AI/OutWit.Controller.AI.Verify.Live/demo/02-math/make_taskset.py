"""Writes the math kit: generators.jsonl, checker.json and candidates.csv.

A synthetic math corpus, generated and checked. Each generator task is one program (Python
with sympy) that makes a problem of one family from its seed and prints the problem with its
answer as a JSON line; some are flawed the way a hurried generator is (a chain rule forgotten,
a sign flipped, a root lost, a constant dropped, a bound off by one, a factor missed). The
checker (sympy too) verifies every answer by another route: an antiderivative by
differentiating it, roots by substituting them, a closed-form sum on concrete values. Run it
again and the files do not change.

usage: python make_taskset.py   (writes next to itself)
"""
import csv
import json
import os

HERE = os.path.dirname(os.path.abspath(__file__))
RUNTIME = "python-3.14.6-sympy-1.14.0"

GENERATOR = r'''import json, random, sys
import sympy as sp

x, n, k = sp.symbols('x n k')
family, seed, flaw = sys.argv[1], int(sys.argv[2]), sys.argv[3]
rng = random.Random(seed)


def emit(problem, answer):
    print(json.dumps({"family": family, "problem": str(problem), "answer": str(answer)}))


if family == "derivative":
    a, b, p = rng.randint(2, 9), rng.randint(2, 5), rng.randint(1, 4)
    f = a * x**p * sp.sin(b * x)
    answer = sp.diff(f, x)
    if flaw == "chain":
        answer = a * p * x**(p - 1) * sp.sin(b * x) + a * x**p * sp.cos(b * x)
    if flaw == "prose":
        print("The derivative is " + str(answer))
        sys.exit(0)
    emit(f, answer)
elif family == "integral":
    p, c = rng.randint(1, 3), rng.randint(1, 3)
    g = x**p * sp.exp(c * x)
    answer = sp.expand(sp.integrate(g, x))
    if flaw == "sign":
        terms = sp.Add.make_args(answer)
        answer = sp.Add(*terms[:-1]) - terms[-1]
    emit(g, answer)
elif family == "roots":
    roots = sorted(rng.sample(range(-6, 7), rng.randint(2, 3)))
    poly = sp.expand(sp.prod([x - r for r in roots]))
    if flaw == "drop":
        roots = roots[:-1]
    emit(poly, roots)
elif family == "simplify":
    p = rng.randint(3, 7)
    expr = (x**p - 1) / (x - 1)
    answer = sum(x**i for i in range(p))
    if flaw == "constant":
        answer = answer - 1
    emit(expr, answer)
elif family == "sum":
    p = rng.randint(1, 3)
    upper = n - 1 if flaw == "bound" else n
    answer = sp.factor(sp.summation(k**p, (k, 1, upper)))
    emit(sp.Sum(k**p, (k, 1, n)), answer)
elif family == "limit":
    a, b = rng.randint(1, 5), rng.randint(2, 4)
    expr = (1 + sp.Integer(a) / x)**(b * x)
    answer = sp.exp(a) if flaw == "factor" else sp.exp(a * b)
    emit(sp.Limit(expr, x, sp.oo), answer)
else:
    raise ValueError("no generator for the family " + family)
'''

CHECKER = r'''import json, sys
import sympy as sp

x, n, k = sp.symbols('x n k')
names = {"x": x, "n": n, "k": k}

try:
    item = json.loads(sys.stdin.readline())
    family = item["family"]
    problem = sp.sympify(item["problem"], locals=names)
    answer = sp.sympify(item["answer"], locals=names)
except Exception as error:
    print("MISMATCH malformed: " + type(error).__name__)
    sys.exit(0)

if family == "derivative":
    ok = sp.simplify(answer - sp.diff(problem, x)) == 0
elif family == "integral":
    ok = sp.simplify(sp.diff(answer, x) - problem) == 0
elif family == "roots":
    roots = list(answer)
    ok = (len(set(roots)) == len(roots) == sp.degree(problem, x)
          and all(problem.subs(x, r) == 0 for r in roots))
elif family == "simplify":
    ok = sp.simplify(answer - problem) == 0
elif family == "sum":
    ok = all(answer.subs(n, m) == problem.subs(n, m).doit() for m in range(1, 9))
elif family == "limit":
    ok = sp.simplify(answer - problem.doit()) == 0
else:
    ok = False

print("OK" if ok else "MISMATCH " + family)
'''

FAMILIES = [
    ("derivative", "chain"),
    ("integral", "sign"),
    ("roots", "drop"),
    ("simplify", "constant"),
    ("sum", "bound"),
    ("limit", "factor"),
]
SEEDS = [1, 2, 3, 4, 5, 6]
FLAWED_SEEDS = {2, 5}


def main():
    rows = []
    for family, family_flaw in FAMILIES:
        for seed in SEEDS:
            flaw = family_flaw if seed in FLAWED_SEEDS else "none"
            rows.append((family, seed, flaw, "Pass", "Fail" if flaw != "none" else "Pass"))
    # A generator that writes prose instead of the format, and one that has no generator at all.
    rows.append(("derivative", 7, "prose", "Pass", "Fail"))
    rows.append(("matrix", 1, "none", "RuntimeError", "-"))

    with open(os.path.join(HERE, "generators.jsonl"), "w", encoding="utf-8", newline="\n") as out:
        for index, (family, seed, flaw, _, _) in enumerate(rows):
            task = {"index": index, "runtime": RUNTIME, "entry": "gen.py", "sources": {"gen.py": GENERATOR},
                    "args": [family, str(seed), flaw]}
            out.write(json.dumps(task, separators=(",", ":")) + "\n")

    checker = {"runtime": RUNTIME, "entry": "check.py", "sources": {"check.py": CHECKER},
               "expected_stdout": "OK\n", "expected_exit": 0}
    with open(os.path.join(HERE, "checker.json"), "w", encoding="utf-8", newline="\n") as out:
        out.write(json.dumps(checker, separators=(",", ":")) + "\n")

    with open(os.path.join(HERE, "candidates.csv"), "w", encoding="utf-8", newline="\n") as out:
        writer = csv.writer(out, lineterminator="\n")
        writer.writerow(["index", "family", "seed", "flaw", "generation", "check"])
        for index, row in enumerate(rows):
            writer.writerow([index, *row])

    print(f"{len(rows)} generators, {sum(1 for r in rows if r[4] != '-')} checks")


if __name__ == "__main__":
    main()
