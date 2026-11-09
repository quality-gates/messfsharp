#!/usr/bin/env python3
import pathlib
import subprocess

root = pathlib.Path(__file__).resolve().parents[3]
case = root / '.explore/case'
cli = [str(root / '.explore/sdk/dotnet'), str(root / 'src/MessFSharp/bin/Release/net10.0/messfsharp.dll')]


def report(path, flag):
    output = case / 'reports/comparison.json'
    command = cli + [path, 'json', 'design', '--only', 'GlobalVariable', '--reportfile', str(output)] + flag
    result = subprocess.run(command, cwd=case, capture_output=True, text=True)
    return result.returncode, output.read_bytes()


for path, flag, expected in [
    ('src/Good.fs', '--ignore-violations-on-exit', 2),
    ('src/Bad.fs', '--ignore-errors-on-exit', 1),
]:
    baseline_exit, baseline = report(path, [])
    ignored_exit, ignored = report(path, [flag])
    assert baseline_exit == expected and ignored_exit == 0 and baseline == ignored
    print(f'{path} baseline_exit={baseline_exit} {flag}_exit={ignored_exit} report_bytes_equal=true')
