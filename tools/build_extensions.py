"""构建默认独立扩展包，启动程序不会补回用户移除的包。"""
from pathlib import Path
import argparse
import json
import subprocess
import zipfile

ROOT = Path(__file__).resolve().parents[1]
PACKAGES = [
    ('status-overview', '状态总览', 'OverviewWorkspace', 'DataBarVertical', 10),
    ('team-status', '部队状况', 'TeamsWorkspace', 'PeopleTeam', 20),
    ('held-swords', '所持刀剑', 'SwordsWorkspace', 'List', 30),
    ('harvest-records', '收获记录', 'HarvestWorkspace', 'History', 40),
]

def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--no-build', action='store_true', help='使用已有 Release 程序集')
    parser.add_argument('--output', type=Path, default=ROOT / 'extension')
    args = parser.parse_args()
    project = ROOT / '_src/MATR.DefaultExtensions/MATR.DefaultExtensions.csproj'
    if not args.no_build:
        subprocess.run(['dotnet', 'build', str(project), '-c', 'Release', '--no-restore', '-p:UsedAvaloniaProducts='], check=True, cwd=ROOT)
    assembly = project.parent / 'bin/Release/net10.0/MATR.DefaultExtensions.dll'
    if not assembly.is_file():
        raise SystemExit('缺少默认扩展程序集，请先构建 Release。')
    args.output.mkdir(parents=True, exist_ok=True)
    for name, title, entry, icon, order in PACKAGES:
        manifest = dict(id='matr.' + name, name=title, version='1.0.0', apiVersion=1,
                        entryAssembly=assembly.name, entryType='MATR.DefaultExtensions.' + entry,
                        icon=icon, order=order)
        target = args.output / (name + '.matrext')
        temporary = target.with_suffix('.tmp')
        with zipfile.ZipFile(temporary, 'w', zipfile.ZIP_DEFLATED) as archive:
            archive.writestr('manifest.json', json.dumps(manifest, ensure_ascii=False, indent=2))
            archive.write(assembly, assembly.name)
        temporary.replace(target)
        print('已生成：' + str(target.relative_to(ROOT) if target.is_relative_to(ROOT) else target))
    # 仅淘汰已知旧占位包，不处理用户自行安装的其他扩展。
    preview = args.output / 'overview-preview.matrext'
    if preview.is_file():
        preview.unlink()

if __name__ == '__main__':
    main()
