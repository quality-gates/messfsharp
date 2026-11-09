#!/usr/bin/env python3
"""Run the CLI pass with an owner for each process group."""
import os
import pathlib
import signal
import subprocess
import sys
import time

root = pathlib.Path(__file__).resolve().parents[3]
evidence = pathlib.Path(__file__).resolve().parent
scratch = root / '.explore'
active = {}


def note(message):
    print(message, flush=True)


def members(group):
    result = []
    for item in pathlib.Path('/proc').iterdir():
        if not item.name.isdigit():
            continue
        try:
            stat = (item / 'stat').read_text()
            fields = stat[stat.rfind(')') + 2:].split()
            if int(fields[2]) == group and fields[0] != 'Z':
                result.append(int(item.name))
        except (FileNotFoundError, ProcessLookupError, PermissionError):
            continue
    return result


def cleanup_group(group):
    pids = members(group)
    if pids:
        note(f'teardown group={group} members={pids} signal=TERM')
        try:
            os.killpg(group, signal.SIGTERM)
        except ProcessLookupError:
            pass
        for _ in range(30):
            if not members(group):
                break
            time.sleep(0.1)
        pids = members(group)
        if pids:
            note(f'teardown group={group} members={pids} signal=KILL')
            try:
                os.killpg(group, signal.SIGKILL)
            except ProcessLookupError:
                pass
            time.sleep(0.2)
    remaining = members(group)
    note(f'verify group={group} live_members={remaining}')
    if remaining:
        raise RuntimeError(f'process group {group} survived')
    active.pop(group, None)


def launch(label, command, env, interrupt_before_register=False):
    note(f'register intent={label} owner={os.getpid()} teardown=kill-process-group')
    old_mask = signal.pthread_sigmask(signal.SIG_BLOCK, {signal.SIGTERM, signal.SIGINT})
    try:
        child = subprocess.Popen(
            command, cwd=root, env=env, start_new_session=True,
            preexec_fn=lambda: signal.pthread_sigmask(signal.SIG_SETMASK, old_mask),
        )
        if interrupt_before_register:
            note('inject SIGTERM between launch and registration')
            os.kill(os.getpid(), signal.SIGTERM)
        active[child.pid] = label
        note(f'registered label={label} pid={child.pid} pgid={child.pid}')
    finally:
        signal.pthread_sigmask(signal.SIG_SETMASK, old_mask)
    return child


def run(label, command, env):
    child = launch(label, command, env)
    try:
        rc = child.wait()
        note(f'completed label={label} exit={rc}')
    finally:
        cleanup_group(child.pid)
    if rc:
        raise RuntimeError(f'{label} exited {rc}')


def interrupted(signum, _frame):
    raise SystemExit(128 + signum)


signal.signal(signal.SIGTERM, interrupted)
signal.signal(signal.SIGINT, interrupted)
env = os.environ.copy()
env.update({
    'HOME': str(scratch / 'home'),
    'DOTNET_ROOT': str(scratch / 'sdk'),
    'DOTNET_CLI_HOME': str(scratch / 'home'),
    'NUGET_PACKAGES': str(scratch / 'nuget'),
    'DOTNET_CLI_TELEMETRY_OPTOUT': '1',
    'DOTNET_SKIP_FIRST_TIME_EXPERIENCE': '1',
    'DOTNET_CLI_DO_NOT_USE_MSBUILD_SERVER': '1',
    'MSBUILDDISABLENODEREUSE': '1',
    'PATH': str(scratch / 'sdk') + ':' + os.environ['PATH'],
})
try:
    note('registered policy: all child process groups; no browsers, containers, images, volumes or listening ports')
    if sys.argv[1:] == ['--test-interrupt']:
        launch('interrupt-test', ['sleep', '60'], env, interrupt_before_register=True)
    scratch.mkdir(exist_ok=True)
    (scratch / 'home').mkdir(exist_ok=True)
    run('sdk-download', ['bash', '-c', 'curl -fsSL https://dot.net/v1/dotnet-install.sh -o .explore/install.sh && bash .explore/install.sh --version 10.0.302 --install-dir .explore/sdk --no-path'], env)
    run('locked-restore', ['dotnet', 'restore', 'messfsharp.sln', '--locked-mode', '--disable-build-servers'], env)
    run('release-build', ['dotnet', 'build', 'src/MessFSharp/MessFSharp.fsproj', '-c', 'Release', '--no-restore', '--disable-build-servers'], env)
    run('journey-replay-one', ['bash', '-c', 'cp docs/exploratory-testing/2026-11-09-evidence/replay.sh .explore/run.sh; bash .explore/run.sh > docs/exploratory-testing/2026-11-09-owned-evidence/run-1.log 2>&1'], env)
    run('journey-replay-two', ['bash', '-c', 'bash .explore/run.sh > docs/exploratory-testing/2026-11-09-owned-evidence/run-2.log 2>&1; cmp docs/exploratory-testing/2026-11-09-owned-evidence/run-1.log docs/exploratory-testing/2026-11-09-owned-evidence/run-2.log'], env)
    run('compare-persisted-reports', ['bash', '-c', 'python3 docs/exploratory-testing/2026-11-09-owned-evidence/compare.py > docs/exploratory-testing/2026-11-09-owned-evidence/comparison.log'], env)
finally:
    note('owner exit cleanup')
    for group in list(active):
        cleanup_group(group)
    note('owner cleanup complete')
