"""Builds the RLVR demo taskset: ten small problems, seven candidate solutions each, as a
model under reinforcement learning would sample them - correct ones in four languages and
the mistakes models make (an edge case, the output format, an algorithm too slow, a crash,
lost precision). Every candidate is graded by the problem's test cases; the verdict is its
reward. Deterministic: the same script writes the same files.

    python make_taskset.py            writes taskset.jsonl and candidates.csv next to it
"""
import csv
import json
import os

HERE = os.path.dirname(os.path.abspath(__file__))

PY = 'python-3.14.6'
JS = 'quickjs-0.15.1'
RB = 'ruby-3.4.1'
LUA = 'lua-5.4.8'
ENTRY = {PY: 'main.py', JS: 'main.js', RB: 'main.rb', LUA: 'main.lua'}

# The budget of every candidate, the same for all: the slowest correct solution (Ruby's prime
# test, whose 32-bit integers turn into bignums past 2^30) needs about half of it, and a
# runaway one ends as Timeout within a few seconds on any node.
LIMITS = {'fuel': 20_000_000_000}

STD = 'import * as std from "qjs:std";\n'

PROBLEMS = [
    {
        'id': 'p01-sum',
        'statement': 'Print the sum of the integers on stdin (none: 0).',
        'cases': [('1\n2\n3\n', '6\n'), ('5\n', '5\n'), ('-2\n2\n', '0\n'), ('', '0\n')],
        'candidates': [
            ('py-correct', PY, 'Pass', 'import sys\nprint(sum(int(x) for x in sys.stdin.read().split()))\n'),
            ('py-empty-input', PY, 'RuntimeError', 'import sys\nxs = sys.stdin.read().split()\nprint(int(xs[0]) + sum(int(x) for x in xs[1:]))\n'),
            ('py-float', PY, 'Fail', 'import sys\nprint(float(sum(int(x) for x in sys.stdin.read().split())))\n'),
            ('js-correct', JS, 'Pass', STD + 'const xs = std.in.readAsString().split(/\\s+/).filter(Boolean).map(Number);\nconsole.log(xs.reduce((a, b) => a + b, 0));\n'),
            ('rb-correct', RB, 'Pass', 'puts STDIN.read.split.map(&:to_i).sum\n'),
            ('lua-correct', LUA, 'Pass', "local s = 0\nfor n in io.read('a'):gmatch('%-?%d+') do s = s + math.tointeger(n) end\nprint(s)\n"),
            ('js-concat', JS, 'Fail', STD + "const xs = std.in.readAsString().split(/\\s+/).filter(Boolean);\nconsole.log(xs.reduce((a, b) => a + b, ''));\n"),
        ],
    },
    {
        'id': 'p02-reverse-words',
        'statement': 'Print the words of the line in reverse order, one space between them.',
        'cases': [('one two three\n', 'three two one\n'), ('single\n', 'single\n'), ('  a   b  \n', 'b a\n'), ('x y\n', 'y x\n')],
        'candidates': [
            ('py-correct', PY, 'Pass', "print(' '.join(input().split()[::-1]))\n"),
            ('py-split-on-space', PY, 'Fail', "print(' '.join(input().split(' ')[::-1]))\n"),
            ('py-reverses-letters', PY, 'Fail', 'print(input()[::-1])\n'),
            ('js-correct', JS, 'Pass', STD + "console.log(std.in.getline().trim().split(/\\s+/).reverse().join(' '));\n"),
            ('rb-correct', RB, 'Pass', "puts gets.split.reverse.join(' ')\n"),
            ('lua-correct', LUA, 'Pass', "local w = {}\nfor x in io.read('l'):gmatch('%S+') do table.insert(w, 1, x) end\nprint(table.concat(w, ' '))\n"),
            ('js-split-on-space', JS, 'Fail', STD + "console.log(std.in.getline().split(' ').reverse().join(' '));\n"),
        ],
    },
    {
        'id': 'p03-vowels',
        'statement': 'Print how many vowels (a, e, i, o, u, any case) the line has.',
        'cases': [('Hello World\n', '3\n'), ('AEIOU\n', '5\n'), ('rhythm\n', '0\n'), ('Queueing\n', '5\n')],
        'candidates': [
            ('py-correct', PY, 'Pass', "print(sum(c in 'aeiou' for c in input().lower()))\n"),
            ('py-case', PY, 'Fail', "print(sum(c in 'aeiou' for c in input()))\n"),
            ('py-counts-y', PY, 'Fail', "print(sum(c in 'aeiouy' for c in input().lower()))\n"),
            ('js-correct', JS, 'Pass', STD + 'console.log((std.in.getline().match(/[aeiou]/gi) || []).length);\n'),
            ('rb-correct', RB, 'Pass', "puts gets.downcase.count('aeiou')\n"),
            ('lua-correct', LUA, 'Pass', "local _, n = io.read('l'):lower():gsub('[aeiou]', '')\nprint(n)\n"),
            ('js-no-match', JS, 'RuntimeError', STD + 'console.log(std.in.getline().match(/[aeiou]/gi).length);\n'),
        ],
    },
    {
        'id': 'p04-fibonacci',
        'statement': 'Print F(n), F(0) = 0, F(1) = 1, for 0 <= n <= 90.',
        'cases': [('0\n', '0\n'), ('1\n', '1\n'), ('10\n', '55\n'), ('90\n', '2880067194370816120\n')],
        'candidates': [
            ('py-correct', PY, 'Pass', 'a, b = 0, 1\nfor _ in range(int(input())):\n    a, b = b, a + b\nprint(a)\n'),
            ('py-recursive', PY, 'Timeout', 'def f(n):\n    return n if n < 2 else f(n - 1) + f(n - 2)\nprint(f(int(input())))\n'),
            ('py-off-by-one', PY, 'Fail', 'a, b = 1, 1\nfor _ in range(int(input())):\n    a, b = b, a + b\nprint(a)\n'),
            ('js-correct', JS, 'Pass', STD + 'let a = 0n, b = 1n;\nfor (let i = Number(std.in.getline()); i > 0; i--) [a, b] = [b, a + b];\nconsole.log(a.toString());\n'),
            ('rb-correct', RB, 'Pass', 'a, b = 0, 1\ngets.to_i.times { a, b = b, a + b }\nputs a\n'),
            ('lua-correct', LUA, 'Pass', "local a, b = 0, 1\nfor _ = 1, io.read('n') do a, b = b, a + b end\nprint(a)\n"),
            ('js-precision', JS, 'Fail', STD + 'let a = 0, b = 1;\nfor (let i = Number(std.in.getline()); i > 0; i--) [a, b] = [b, a + b];\nconsole.log(String(a));\n'),
        ],
    },
    {
        'id': 'p05-prime',
        'statement': 'Print YES if n (1 <= n <= 10^12) is prime, else NO.',
        'cases': [('1\n', 'NO\n'), ('2\n', 'YES\n'), ('999999999989\n', 'YES\n'), ('1000000000000\n', 'NO\n')],
        'candidates': [
            ('py-correct', PY, 'Pass', "n = int(input())\nok = n >= 2 and (n == 2 or n % 2 == 1)\ni = 3\nwhile ok and i * i <= n:\n    ok = n % i != 0\n    i += 2\nprint('YES' if ok else 'NO')\n"),
            ('py-to-n', PY, 'Timeout', "n = int(input())\nok = n >= 2\nfor i in range(2, n):\n    if n % i == 0:\n        ok = False\n        break\nprint('YES' if ok else 'NO')\n"),
            ('py-one-is-prime', PY, 'Fail', "n = int(input())\nok = n == 2 or n % 2 == 1\ni = 3\nwhile ok and i * i <= n:\n    ok = n % i != 0\n    i += 2\nprint('YES' if ok else 'NO')\n"),
            ('js-correct', JS, 'Pass', STD + "const n = Number(std.in.getline());\nlet ok = n >= 2 && (n === 2 || n % 2 === 1);\nfor (let i = 3; ok && i * i <= n; i += 2) ok = n % i !== 0;\nconsole.log(ok ? 'YES' : 'NO');\n"),
            ('rb-correct', RB, 'Pass', "n = gets.to_i\nok = n >= 2 && (n == 2 || n.odd?)\ni = 3\nwhile ok && i * i <= n\n  ok = n % i != 0\n  i += 2\nend\nputs ok ? 'YES' : 'NO'\n"),
            ('lua-correct', LUA, 'Pass', "local n = math.tointeger(io.read('n'))\nlocal ok = n >= 2 and (n == 2 or n % 2 == 1)\nlocal i = 3\nwhile ok and i * i <= n do ok = n % i ~= 0; i = i + 2 end\nprint(ok and 'YES' or 'NO')\n"),
            ('js-two', JS, 'Fail', STD + "const n = Number(std.in.getline());\nlet ok = n >= 2 && n % 2 === 1;\nfor (let i = 3; ok && i * i <= n; i += 2) ok = n % i !== 0;\nconsole.log(ok ? 'YES' : 'NO');\n"),
        ],
    },
    {
        'id': 'p06-gcd',
        'statement': 'Print the greatest common divisor of the two numbers on the line.',
        'cases': [('12 18\n', '6\n'), ('7 13\n', '1\n'), ('0 5\n', '5\n'), ('100 10\n', '10\n')],
        'candidates': [
            ('py-correct', PY, 'Pass', 'import math\na, b = map(int, input().split())\nprint(math.gcd(a, b))\n'),
            ('py-min', PY, 'Fail', 'a, b = map(int, input().split())\nprint(min(a, b))\n'),
            ('py-subtraction', PY, 'Timeout', 'a, b = map(int, input().split())\nwhile a != b:\n    if a > b:\n        a -= b\n    else:\n        b -= a\nprint(a)\n'),
            ('js-correct', JS, 'Pass', STD + "let [a, b] = std.in.getline().split(' ').map(Number);\nwhile (b) [a, b] = [b, a % b];\nconsole.log(a);\n"),
            ('rb-correct', RB, 'Pass', 'a, b = gets.split.map(&:to_i)\nputs a.gcd(b)\n'),
            ('lua-correct', LUA, 'Pass', "local a, b = io.read('n', 'n')\na, b = math.tointeger(a), math.tointeger(b)\nwhile b ~= 0 do a, b = b, a % b end\nprint(a)\n"),
            ('js-no-base-case', JS, 'RuntimeError', STD + "const g = (a, b) => g(b, a % b);\nconst [a, b] = std.in.getline().split(' ').map(Number);\nconsole.log(g(a, b));\n"),
        ],
    },
    {
        'id': 'p07-brackets',
        'statement': 'Print YES if the brackets ()[]{} of the line are balanced, else NO.',
        'cases': [('([]{})\n', 'YES\n'), ('([)]\n', 'NO\n'), ('((\n', 'NO\n'), (')(\n', 'NO\n')],
        'candidates': [
            ('py-correct', PY, 'Pass', "pairs = {')': '(', ']': '[', '}': '{'}\nstack = []\nok = True\nfor c in input():\n    if c in '([{':\n        stack.append(c)\n    elif c in pairs:\n        ok = ok and bool(stack) and stack.pop() == pairs[c]\nprint('YES' if ok and not stack else 'NO')\n"),
            ('py-counts-only', PY, 'Fail', "s = input()\nok = all(s.count(o) == s.count(c) for o, c in ('()', '[]', '{}'))\nprint('YES' if ok else 'NO')\n"),
            ('py-empty-pop', PY, 'RuntimeError', "pairs = {')': '(', ']': '[', '}': '{'}\nstack = []\nok = True\nfor c in input():\n    if c in '([{':\n        stack.append(c)\n    elif c in pairs:\n        ok = ok and stack.pop() == pairs[c]\nprint('YES' if ok and not stack else 'NO')\n"),
            ('js-correct', JS, 'Pass', STD + "const pairs = { ')': '(', ']': '[', '}': '{' };\nconst stack = [];\nlet ok = true;\nfor (const c of std.in.getline()) {\n  if ('([{'.includes(c)) stack.push(c);\n  else if (c in pairs) ok = ok && stack.length > 0 && stack.pop() === pairs[c];\n}\nconsole.log(ok && stack.length === 0 ? 'YES' : 'NO');\n"),
            ('rb-correct', RB, 'Pass', "pairs = { ')' => '(', ']' => '[', '}' => '{' }\nstack = []\nok = true\ngets.chomp.each_char do |c|\n  if '([{'.include?(c)\n    stack.push(c)\n  elsif pairs.key?(c)\n    ok &&= !stack.empty? && stack.pop == pairs[c]\n  end\nend\nputs ok && stack.empty? ? 'YES' : 'NO'\n"),
            ('lua-correct', LUA, 'Pass', "local pairs_ = { [')'] = '(', [']'] = '[', ['}'] = '{' }\nlocal stack, ok = {}, true\nfor c in io.read('l'):gmatch('.') do\n  if c == '(' or c == '[' or c == '{' then stack[#stack + 1] = c\n  elseif pairs_[c] then ok = ok and #stack > 0 and table.remove(stack) == pairs_[c] end\nend\nprint((ok and #stack == 0) and 'YES' or 'NO')\n"),
            ('js-forgets-leftovers', JS, 'Fail', STD + "const pairs = { ')': '(', ']': '[', '}': '{' };\nconst stack = [];\nlet ok = true;\nfor (const c of std.in.getline()) {\n  if ('([{'.includes(c)) stack.push(c);\n  else if (c in pairs) ok = ok && stack.length > 0 && stack.pop() === pairs[c];\n}\nconsole.log(ok ? 'YES' : 'NO');\n"),
        ],
    },
    {
        'id': 'p08-max-subarray',
        'statement': 'Print the largest sum of a non-empty run of consecutive numbers of the line.',
        'cases': [('1 -2 3 4 -1\n', '7\n'), ('-3 -1 -2\n', '-1\n'), ('5\n', '5\n'), ('2 -1 2\n', '3\n')],
        'candidates': [
            ('py-correct', PY, 'Pass', 'xs = list(map(int, input().split()))\nbest = cur = xs[0]\nfor x in xs[1:]:\n    cur = max(x, cur + x)\n    best = max(best, cur)\nprint(best)\n'),
            ('py-starts-at-zero', PY, 'Fail', 'xs = list(map(int, input().split()))\nbest = cur = 0\nfor x in xs:\n    cur = max(0, cur + x)\n    best = max(best, cur)\nprint(best)\n'),
            ('py-cubic', PY, 'Pass', 'xs = list(map(int, input().split()))\nprint(max(sum(xs[i:j + 1]) for i in range(len(xs)) for j in range(i, len(xs))))\n'),
            ('js-correct', JS, 'Pass', STD + "const xs = std.in.getline().trim().split(/\\s+/).map(Number);\nlet best = xs[0], cur = xs[0];\nfor (const x of xs.slice(1)) { cur = Math.max(x, cur + x); best = Math.max(best, cur); }\nconsole.log(best);\n"),
            ('rb-correct', RB, 'Pass', 'xs = gets.split.map(&:to_i)\nbest = cur = xs[0]\nxs.drop(1).each { |x| cur = [x, cur + x].max; best = [best, cur].max }\nputs best\n'),
            ('lua-correct', LUA, 'Pass', "local xs = {}\nfor n in io.read('l'):gmatch('%-?%d+') do xs[#xs + 1] = math.tointeger(n) end\nlocal best, cur = xs[1], xs[1]\nfor i = 2, #xs do cur = math.max(xs[i], cur + xs[i]); best = math.max(best, cur) end\nprint(best)\n"),
            ('js-positives', JS, 'Fail', STD + "const xs = std.in.getline().trim().split(/\\s+/).map(Number);\nconsole.log(xs.filter(x => x > 0).reduce((a, b) => a + b, 0));\n"),
        ],
    },
    {
        'id': 'p09-top-word',
        'statement': 'Print the most frequent word (letters, any case, counted in lower case) and its count; a tie goes to the word first in alphabetical order.',
        'cases': [('the cat the dog\n', 'the 2\n'), ('b a\n', 'a 1\n'), ('Apple apple BANANA\n', 'apple 2\n'), ('x y y x z\n', 'x 2\n')],
        'candidates': [
            ('py-correct', PY, 'Pass', "import re, sys\nfrom collections import Counter\nc = Counter(re.findall('[a-z]+', sys.stdin.read().lower()))\nw = min(c, key=lambda k: (-c[k], k))\nprint(w, c[w])\n"),
            ('py-first-seen', PY, 'Fail', "import re, sys\nfrom collections import Counter\nc = Counter(re.findall('[a-z]+', sys.stdin.read().lower()))\nw, n = c.most_common(1)[0]\nprint(w, n)\n"),
            ('py-case', PY, 'Fail', "import re, sys\nfrom collections import Counter\nc = Counter(re.findall('[A-Za-z]+', sys.stdin.read()))\nw = min(c, key=lambda k: (-c[k], k))\nprint(w, c[w])\n"),
            ('js-correct', JS, 'Pass', STD + "const c = new Map();\nfor (const w of std.in.readAsString().toLowerCase().match(/[a-z]+/g) || []) c.set(w, (c.get(w) || 0) + 1);\nconst [w, n] = [...c].sort((a, b) => b[1] - a[1] || (a[0] < b[0] ? -1 : 1))[0];\nconsole.log(w + ' ' + n);\n"),
            ('rb-correct', RB, 'Pass', "c = STDIN.read.downcase.scan(/[a-z]+/).tally\nw, n = c.min_by { |k, v| [-v, k] }\nputs \"#{w} #{n}\"\n"),
            ('lua-correct', LUA, 'Pass', "local c, best, n = {}, nil, 0\nfor w in io.read('a'):lower():gmatch('%a+') do c[w] = (c[w] or 0) + 1 end\nfor w, k in pairs(c) do\n  if k > n or (k == n and w < best) then best, n = w, k end\nend\nprint(best .. ' ' .. n)\n"),
            ('js-format', JS, 'Fail', STD + "const c = new Map();\nfor (const w of std.in.readAsString().toLowerCase().match(/[a-z]+/g) || []) c.set(w, (c.get(w) || 0) + 1);\nconst [w, n] = [...c].sort((a, b) => b[1] - a[1] || (a[0] < b[0] ? -1 : 1))[0];\nconsole.log(w + ': ' + n);\n"),
        ],
    },
    {
        'id': 'p10-binary',
        'statement': 'Print the value of the binary number on the line (up to 62 digits).',
        'cases': [('101\n', '5\n'), ('110\n', '6\n'), ('11111111\n', '255\n'), ('1' * 62 + '\n', '4611686018427387903\n')],
        'candidates': [
            ('py-correct', PY, 'Pass', 'print(int(input(), 2))\n'),
            ('py-reversed', PY, 'Fail', 'print(sum(int(c) * 2 ** i for i, c in enumerate(input())))\n'),
            ('py-float', PY, 'Fail', 's = input()\nprint(int(sum(int(c) * 2.0 ** (len(s) - 1 - i) for i, c in enumerate(s))))\n'),
            ('js-correct', JS, 'Pass', STD + "console.log(BigInt('0b' + std.in.getline()).toString());\n"),
            ('rb-correct', RB, 'Pass', 'puts gets.chomp.to_i(2)\n'),
            ('lua-correct', LUA, 'Pass', "print(math.tointeger(tonumber(io.read('l'), 2)))\n"),
            ('js-parseint', JS, 'Fail', STD + 'console.log(String(parseInt(std.in.getline(), 2)));\n'),
        ],
    },
]


def main():
    tasks, rows = [], []
    for problem in PROBLEMS:
        suite = [{'stdin': stdin, 'expected_stdout': expected} for stdin, expected in problem['cases']]
        for name, runtime, verdict, code in problem['candidates']:
            index = len(tasks)
            tasks.append({
                'index': index,
                'runtime': runtime,
                'entry': ENTRY[runtime],
                'sources': {ENTRY[runtime]: code},
                'suite': suite,
                'limits': LIMITS,
            })
            rows.append({'index': index, 'problem': problem['id'], 'candidate': name, 'runtime': runtime, 'expected': verdict})

    with open(os.path.join(HERE, 'taskset.jsonl'), 'w', encoding='utf-8', newline='\n') as out:
        for task in tasks:
            out.write(json.dumps(task, ensure_ascii=False, separators=(',', ':')) + '\n')

    with open(os.path.join(HERE, 'candidates.csv'), 'w', encoding='utf-8', newline='') as out:
        writer = csv.DictWriter(out, fieldnames=['index', 'problem', 'candidate', 'runtime', 'expected'], lineterminator='\n')
        writer.writeheader()
        writer.writerows(rows)

    print(f'{len(tasks)} tasks, {len(PROBLEMS)} problems')


if __name__ == '__main__':
    main()
