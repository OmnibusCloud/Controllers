"""Turns a Corpus report into the kit's tables and checks every shard against shards.tsv.

    python check.py <report.json> [<integrity.json>]

Prints the funnel, the drops by stage, the languages kept and the languages lost, then one line
per shard that differs from the kit's list (verdict, content hash, signatures hash, document
counts), the kit's identities (the SHA-256 of the lines "index:content_sha256", and of the lines
"index:signatures_sha256", in index order) and, with the integrity document, the pairs. Exits 1
when a shard differs or the report kept text.
"""

import hashlib
import json
import os
import sys

STAGES = ['extract', 'language', 'lines', 'quality']


def load_expected(path):
    rows = {}
    with open(path, encoding='utf-8') as handle:
        header = handle.readline().rstrip('\n').split('\t')
        for line in handle:
            values = dict(zip(header, line.rstrip('\n').split('\t')))
            rows[int(values['index'])] = values
    return rows


def digest(lines):
    return hashlib.sha256(''.join(lines).encode('ascii')).hexdigest()


def main(argv):
    if len(argv) < 2:
        print(__doc__)
        return 2

    kit = os.path.dirname(os.path.abspath(__file__))
    expected = load_expected(os.path.join(kit, 'shards.tsv'))
    with open(argv[1], encoding='utf-8') as handle:
        report = json.load(handle)

    summary = report['summary']
    funnel = summary.get('funnel')
    print(f"report     : {report['schema']}, controller {report['controller']}, output {summary.get('output', 'full')}")
    print(f"shards     : {summary['tasks']} ({', '.join(f'{name} {count}' for name, count in summary['verdicts'].items())})")
    print(f"fetched    : {summary['bytes_fetched']:,} bytes; the product would be {summary['bytes_produced']:,} bytes compressed")
    if funnel:
        print(f"funnel     : records {funnel['records']:,} > extracted {funnel['extracted']:,} > language {funnel['language']:,}"
              f" > lines {funnel['lines']:,} > quality {funnel['quality']:,} = out {funnel['out']:,}")
        print('dropped    : ' + ', '.join(f"{stage} {funnel['dropped'][stage]:,}" for stage in STAGES))

    drops = sorted(summary['drops'].items(), key=lambda item: (-item[1], item[0]))
    print('drops      : ' + ', '.join(f'{name} {count:,}' for name, count in drops[:8]))
    languages = sorted(summary['languages'].items(), key=lambda item: (-item[1], item[0]))
    print('languages  : ' + ', '.join(f'{name} {count:,}' for name, count in languages))
    lost = sorted(summary.get('unsupported_languages', {}).items(), key=lambda item: (-item[1], item[0]))
    print('not kept   : ' + ', '.join(f'{name} {count:,}' for name, count in lost[:10]) + (f' (+{len(lost) - 10} more)' if len(lost) > 10 else ''))

    unexpected = 0
    kept_text = 0
    identity = []
    signed = []
    for shard in report['shards']:
        index = shard['index']
        signatures = shard.get('signatures_sha256', '')
        identity.append(f"{index}:{shard['content_sha256']}\n")
        signed.append(f'{index}:{signatures}\n')
        if shard['text_blob'] or shard['signatures_blob']:
            kept_text += 1
        want = expected.get(index)
        stats = shard.get('stats') or {}
        problems = []
        if want is None:
            problems.append('not in the kit')
        else:
            if shard['verdict'] != 'Completed':
                problems.append(f"verdict {shard['verdict']}: {shard['error']}")
            else:
                if shard['content_sha256'] != want['content_sha256']:
                    problems.append(f"content {shard['content_sha256'][:12]}… instead of {want['content_sha256'][:12]}…")
                if signatures != want['signatures_sha256']:
                    problems.append(f"signatures {signatures[:12]}… instead of {want['signatures_sha256'][:12]}…")
            for field in ('records_in', 'documents_extracted', 'documents_out'):
                if stats and str(stats.get(field)) != want[field]:
                    problems.append(f'{field} {stats.get(field)} instead of {want[field]}')
        if problems:
            unexpected += 1
            print(f'  [{index:3}] ' + '; '.join(problems))

    missing = sorted(set(expected) - {shard['index'] for shard in report['shards']})
    for index in missing:
        unexpected += 1
        print(f'  [{index:3}] missing from the report')

    print(f'identity   : {digest(identity)}')
    print(f'signatures : {digest(signed)}')
    print(f'shards the kit does not expect: {unexpected}')
    if kept_text:
        print(f'shards that kept text or signatures: {kept_text} (the kit keeps statistics and hashes only)')

    if len(argv) > 2:
        with open(argv[2], encoding='utf-8') as handle:
            integrity = json.load(handle)
        totals = integrity['summary']
        print(f"integrity  : {integrity['schema']}, sampled {totals['sampled']}, compared {totals['compared']},"
              f" version skew {totals['version_skew']}, incomplete {totals['incomplete']}, mismatches {totals['mismatches']}"
              f" (signatures compared {totals.get('signatures_compared', 0)}, signature mismatches {totals.get('signature_mismatches', 0)})")

    return 1 if unexpected or kept_text else 0


if __name__ == '__main__':
    sys.exit(main(sys.argv))
