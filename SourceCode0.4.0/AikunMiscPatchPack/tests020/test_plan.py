"""选择测试范围并核对发布结果；只使用标准库，不启动游戏。"""
from pathlib import Path
import argparse
import hashlib
import json
import sys

ROOT = Path(__file__).resolve().parents[1]

def read(path):
    return json.loads(path.read_text(encoding='utf-8-sig'))

def sha(path):
    return hashlib.sha256(path.read_bytes()).hexdigest().upper()

def runner_sha():
    if not all((ROOT/'tests020'/name).is_file() for name in ('Run-EngineTests.ps1','prepare_sandbox.py')):
        raise ValueError('公开源码不含私人游戏宿主；请先配置隔离宿主，再生成与核验完整回归证据。')
    files = sorted((ROOT / 'tests020').glob('*.cs'))
    files += [ROOT / 'tests020' / name for name in (
        'Probe.csproj', 'Run-EngineTests.ps1', 'prepare_sandbox.py', 'test_plan.py', 'test-profiles.json')]
    return hashlib.sha256(''.join(f'{p.relative_to(ROOT).as_posix()}:{sha(p)}\n' for p in files).encode()).hexdigest().upper()

def select_plan(suite='targeted', profile='current', mode='auto'):
    if suite not in ('targeted', 'regression') or mode not in ('auto', 'full', 'minimal'):
        raise ValueError('未知测试范围或依赖环境。')
    if suite == 'regression':
        if profile != 'current':
            raise ValueError('完整回归包含全部项目，不能同时指定专项 Profile。')
        return dict(Suite=suite, Profile='all', Mode='full' if mode=='auto' else mode, RequestedSuites=['legacy-regression'], Description='发布前完整回归')
    config = read(ROOT / 'tests020/test-profiles.json')
    if profile == 'current':
        profile = config['CurrentProfile']
    if profile not in config['Profiles']:
        raise ValueError(f'未知专项 {profile}，请检查 test-profiles.json。')
    plan = config['Profiles'][profile]
    if mode == 'auto':
        mode = plan.get('DefaultMode', 'minimal')
    if mode not in ('full', 'minimal'):
        raise ValueError('专项的默认依赖环境无效。')
    if not plan['Primary'] or not plan['Related']:
        raise ValueError('专项必须明确列出本次修改及相关检查。')
    checks = list(dict.fromkeys(plan['Related'] + plan['Primary']))
    return dict(Suite=suite, Profile=profile, Mode=mode, RequestedSuites=checks, Description=plan['Description'])

def validate_result(result, request):
    for key in ('Suite', 'Profile', 'Mode', 'RunId', 'RunnerSha256', 'RequestedSuites'):
        if result.get(key) != request[key]:
            raise ValueError(f'测试结果的 {key} 与本轮请求不符。')
    if result.get('AssemblyHash') != request['AssemblyHash'] or result.get('ProbeHash') != request['ProbeHash']:
        raise ValueError('测试结果对应旧 DLL 或旧探针。')
    if type(result.get('Failed')) is not int or result['Failed'] != 0 or result.get('Failures') != []:
        raise ValueError('本轮原生测试失败或缺少明确的成功状态。')
    if type(result.get('Passed')) is not int or result['Passed'] <= 0 or result['Passed'] != len(result.get('Checks', [])):
        raise ValueError('缺少有效的检查计数。')
    if result.get('ExecutedSuites') != request['RequestedSuites']:
        raise ValueError('实际执行范围与请求不一致，拒绝接受漏测结果。')
    if result.get('RegressionComplete') is not (request['Suite'] == 'regression'):
        raise ValueError('专项与完整回归标记不一致。')

def validate_release_result(result, mode, assembly_hash, probe_hash, runner_hash):
    request = dict(Suite='regression', Profile='all', Mode=mode, RunId=result.get('RunId'),
                   RunnerSha256=runner_hash, RequestedSuites=['legacy-regression'],
                   AssemblyHash=assembly_hash, ProbeHash=probe_hash)
    if not isinstance(result.get('RunId'), str) or not result['RunId']:
        raise ValueError('缺少本轮运行标识。')
    validate_result(result, request)

def validate_release():
    version = read(ROOT / 'AikunMiscPatchPack.json')['version']
    output = ROOT / 'verification' / version
    assembly_hash = sha(ROOT / 'src/bin/Release/net9.0/AikunMiscPatchPack.dll')
    probe_hash = sha(ROOT / 'tests020/bin/Release/net9.0/Aikun020Probe.dll')
    for mode in ('full', 'minimal'):
        path = output / f'regression-{mode}-result.json'
        if not path.exists():
            raise ValueError(f'缺少 {mode} 完整回归结果。请运行 ./tests020/Run-EngineTests.ps1 -Suite regression -Mode {mode}')
        validate_release_result(read(path), mode, assembly_hash, probe_hash, runner_sha())

if __name__ == '__main__':
    sys.stdout.reconfigure(encoding='utf-8')
    sys.stderr.reconfigure(encoding='utf-8')
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--suite', choices=('targeted', 'regression'), default='targeted')
    parser.add_argument('--profile', default='current')
    parser.add_argument('--mode', choices=('auto', 'full', 'minimal'), default='auto')
    parser.add_argument('--validate-release', action='store_true')
    args = parser.parse_args()
    try:
        if args.validate_release:
            validate_release()
            print('RELEASE_VALIDATION_PASS: full + minimal 完整回归与当前 DLL、探针和测试代码一致。')
        else:
            print(json.dumps(select_plan(args.suite, args.profile, args.mode), ensure_ascii=False, indent=2))
    except (ValueError, KeyError, OSError) as error:
        parser.exit(1, str(error) + '\n')
