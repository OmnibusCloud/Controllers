"""The rewards table of an RLVR run: every candidate's verdict from the run's report, its reward
(1 for Pass, 0 otherwise), per problem and per runtime, and whether each verdict is the one the
kit expects. Standard library only.

    python rewards.py <report.json>        the report verify-live saved (--save DIR)
"""
import csv
import json
import os
import sys

HERE = os.path.dirname(os.path.abspath(__file__))
SHORT = {'Pass': 'pass', 'Fail': 'fail', 'RuntimeError': 'crash', 'Timeout': 'timeout',
         'MemoryExceeded': 'memory', 'OutputExceeded': 'output', 'CompileError': 'compile',
         'RuntimeUnavailable': 'n/a'}


def main(report_path):
    with open(os.path.join(HERE, 'candidates.csv'), encoding='utf-8') as f:
        candidates = {int(row['index']): row for row in csv.DictReader(f)}
    with open(report_path, encoding='utf-8') as f:
        report = json.load(f)
    verdicts = {r['index']: r['verdict'] for r in report['results']}

    problems, names = {}, []
    for index, row in sorted(candidates.items()):
        problems.setdefault(row['problem'], []).append(index)
        if row['candidate'] not in names:
            names.append(row['candidate'])

    width = max(len(p) for p in problems)
    print(f'{"problem":{width}}  reward  ' + '  '.join(f'{n:>13}' for n in names[:7]))
    wrong, by_runtime = [], {}
    for problem, indexes in problems.items():
        cells, reward = [], 0
        for index in indexes:
            row, verdict = candidates[index], verdicts.get(index, 'missing')
            reward += verdict == 'Pass'
            total, passed = by_runtime.get(row['runtime'], (0, 0))
            by_runtime[row['runtime']] = (total + 1, passed + (verdict == 'Pass'))
            if verdict != row['expected']:
                wrong.append(f'{index} {problem}/{row["candidate"]}: {verdict}, the kit expects {row["expected"]}')
            cells.append(f'{SHORT.get(verdict, verdict):>13}')
        print(f'{problem:{width}}  {reward}/{len(indexes)}     ' + '  '.join(cells))

    earned = sum(v == 'Pass' for v in verdicts.values())
    print(f'\nrewards earned: {earned} of {len(candidates)}')
    for runtime, (total, passed) in sorted(by_runtime.items()):
        print(f'  {runtime:16} {passed:3} of {total}')
    print(f'verdicts the kit does not expect: {len(wrong)}')
    for line in wrong:
        print('  ' + line)
    return 1 if wrong else 0


if __name__ == '__main__':
    if len(sys.argv) != 2:
        print(__doc__)
        sys.exit(2)
    sys.exit(main(sys.argv[1]))
