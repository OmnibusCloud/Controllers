"""The verified corpus of a math kit run: every generated item with its check, per family the
items generated, verified and rejected, the verified items written out as JSON lines, and
whether each verdict is the one the kit expects. Standard library only.

    python corpus.py <DIR> [N]   DIR = the folder verify-live check saved (--save DIR), N = the run (1)
"""
import csv
import json
import os
import sys

HERE = os.path.dirname(os.path.abspath(__file__))


def main(folder, run):
    with open(os.path.join(HERE, 'candidates.csv'), encoding='utf-8') as f:
        candidates = {int(row['index']): row for row in csv.DictReader(f)}
    with open(os.path.join(folder, f'report-{run}.json'), encoding='utf-8') as f:
        checks = {r['index']: r['verdict'] for r in json.load(f)['results']}
    with open(os.path.join(folder, f'corpus-{run}.jsonl'), encoding='utf-8') as f:
        generated = {r['index']: r for r in map(json.loads, f)}

    families, wrong, verified = {}, [], []
    for index, row in sorted(candidates.items()):
        item = generated.get(index, {'verdict': 'missing', 'stdout': ''})
        check = checks.get(index, '-')
        counts = families.setdefault(row['family'], [0, 0, 0, 0])
        counts[0] += item['verdict'] == 'Pass'
        counts[1] += check == 'Pass'
        counts[2] += check == 'Fail'
        counts[3] += item['verdict'] != 'Pass'
        if item['verdict'] != row['generation'] or check != row['check']:
            wrong.append(f'{index} {row["family"]}/{row["seed"]}/{row["flaw"]}: generated {item["verdict"]}, '
                         f'checked {check}; the kit expects {row["generation"]}, {row["check"]}')
        if check == 'Pass':
            verified.append(item['stdout'].strip())

    print(f'{"family":12} {"generated":>9} {"verified":>9} {"rejected":>9} {"failed":>7}')
    for family, (made, good, bad, failed) in families.items():
        print(f'{family:12} {made:9} {good:9} {bad:9} {failed:7}')
    totals = [sum(c[i] for c in families.values()) for i in range(4)]
    print(f'{"total":12} {totals[0]:9} {totals[1]:9} {totals[2]:9} {totals[3]:7}')
    distinct = len(set(verified))
    print(f'verified items: {len(verified)} ({distinct} distinct problems with their answers)')

    out = os.path.join(folder, f'verified-{run}.jsonl')
    with open(out, 'w', encoding='utf-8', newline='\n') as f:
        for line in verified:
            f.write(line + '\n')
    print(f'written: {out}')

    print(f'verdicts the kit does not expect: {len(wrong)}')
    for line in wrong:
        print('  ' + line)
    return 1 if wrong else 0


if __name__ == '__main__':
    if len(sys.argv) < 2:
        print(__doc__)
        sys.exit(2)
    sys.exit(main(sys.argv[1], sys.argv[2] if len(sys.argv) > 2 else '1'))
