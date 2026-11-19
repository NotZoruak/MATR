"""将独立界面程序集和清单打成 .matrext 包。"""
import argparse
import json
from pathlib import Path
import zipfile

parser = argparse.ArgumentParser(description=__doc__)
parser.add_argument('--manifest', required=True, type=Path)
parser.add_argument('--assembly', required=True, type=Path)
parser.add_argument('--output', required=True, type=Path)
parser.add_argument('--file', action='append', type=Path, default=[], help='附加私有依赖或资源，存入包根目录')
args = parser.parse_args()
manifest = json.loads(args.manifest.read_text(encoding='utf-8-sig'))
if manifest.get('apiVersion') != 1 or manifest.get('entryAssembly') != args.assembly.name:
    raise SystemExit('接口版本或入口程序集文件名不匹配。')
files = [args.assembly, *args.file]
if len({p.name.casefold() for p in files}) != len(files) or any(p.name.casefold() == 'manifest.json' for p in files):
    raise SystemExit('包内文件名重复。')
args.output.parent.mkdir(parents=True, exist_ok=True)
temporary = args.output.with_suffix('.tmp')
with zipfile.ZipFile(temporary, 'w', zipfile.ZIP_DEFLATED) as archive:
    archive.writestr('manifest.json', json.dumps(manifest, ensure_ascii=False, indent=2))
    for file in files:
        archive.write(file, file.name)
temporary.replace(args.output)
print('已生成：' + str(args.output))
