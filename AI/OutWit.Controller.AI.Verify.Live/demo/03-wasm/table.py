"""The verdict table of a raw-wasm kit run: every module's verdict from the run's report, per
problem, and whether each verdict is the one the kit expects. Standard library only.

    python table.py <report.json>        the report verify-live saved (--save DIR)
"""
import csv
import json
import os
import sys

HERE = os.path.dirname(os.path.abspath(__file__))


def main(report_path):
    with open(os.path.join(HERE, 'candidates.csv'), encoding='utf-8') as f:
        candidates = {int(row['index']): row for row in csv.DictReader(f)}
    with open(report_path, encoding='utf-8') as f:
        verdicts = {r['index']: r['verdict'] for r in json.load(f)['results']}

    print(f'{"problem":8} {"candidate":13} {"language":8} {"verdict":13} why')
    wrong = []
    for index, row in sorted(candidates.items()):
        verdict = verdicts.get(index, 'missing')
        mark = '' if verdict == row['verdict'] else f'   (the kit expects {row["verdict"]})'
        print(f'{row["problem"]:8} {row["candidate"]:13} {row["language"]:8} {verdict:13} {row["why"]}{mark}')
        if verdict != row['verdict']:
            wrong.append(index)

    print(f'verdicts the kit does not expect: {len(wrong)}')
    return 1 if wrong else 0


if __name__ == '__main__':
    if len(sys.argv) < 2:
        print(__doc__)
        sys.exit(2)
    sys.exit(main(sys.argv[1]))
