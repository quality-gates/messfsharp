#!/usr/bin/env python3
"""Compare convergent and linear callable-alias scaling through the CLI.

Build Release first. Timings are medians of three whole-process runs, so compare
trends and ratios rather than an absolute machine-specific time limit.
"""
from pathlib import Path
from statistics import median
from tempfile import TemporaryDirectory
from time import perf_counter
import subprocess

root = Path(__file__).resolve().parents[1]
cli = root / 'src/MessFSharp/bin/Release/net10.0/messfsharp.dll'


def source(layers, convergent):
    lines = ['module Sample', 'let probe flag =', '    let f0 x = x + 1']
    for i in range(1, layers + 1):
        other = f'f{i - 1}' if convergent else 'f0'
        lines.append(f'    let f{i} = if flag then f{i - 1} else {other}')
    return '\n'.join(lines + [f'    f{layers} 0', ''])


with TemporaryDirectory(prefix='messfsharp-alias-benchmark-') as temp:
    print('layers   join (s)   control (s)   ratio')
    for layers in (12, 16, 18, 20):
        times = []
        for convergent in (True, False):
            path = Path(temp) / 'sample.fs'
            path.write_text(source(layers, convergent))
            durations = []
            for _ in range(3):
                start = perf_counter()
                subprocess.run(
                    ['dotnet', str(cli), str(path), 'text', 'explicitness',
                     '--only', 'ImplicitOutput', '--ignore-violations-on-exit'],
                    cwd=root, check=True, stdout=subprocess.DEVNULL,
                )
                durations.append(perf_counter() - start)
            times.append(median(durations))
        print(f'{layers:6} {times[0]:10.3f} {times[1]:13.3f} {times[0] / times[1]:7.2f}')
