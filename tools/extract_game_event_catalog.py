"""从已解码的客户端活动表生成完整名称对照，不执行 Lua。"""
import argparse
import hashlib
import json
from pathlib import Path
from extract_game_sword_catalog import read_table


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--cfg-dir', required=True, type=Path)
    parser.add_argument('--output', type=Path, default=Path(__file__).resolve().parents[1] / '_src/MFAAvalonia/Assets/GameEventCatalog.json')
    args = parser.parse_args()
    events = read_table(args.cfg_dir, 'event_master')
    fields = read_table(args.cfg_dir, 'event_field_master')
    names = {str(row['event_id']): row['name'].strip() for row in events}
    stages = {f"{row['event_id']}:{row['event_field_id']}": row['name'].strip() for row in fields}
    if len(names) != len(events) or len(stages) != len(fields):
        raise ValueError('活动或关卡编号重复')
    if any(not value for value in [*names.values(), *stages.values()]):
        raise ValueError('活动或关卡名称为空')
    if any(str(row['event_id']) not in names for row in fields):
        raise ValueError('关卡引用的活动不存在')
    sources = {name: hashlib.sha256((args.cfg_dir / (name + '.lua')).read_bytes()).hexdigest()
               for name in ['event_master', 'event_field_master']}
    document = dict(sources=sources, events=names, fields=stages)
    args.output.parent.mkdir(parents=True, exist_ok=True)
    args.output.write_text(json.dumps(document, ensure_ascii=False, indent=2) + '\n', encoding='utf-8')
    print(f'已生成活动对照：{len(names)} 条活动、{len(stages)} 条关卡')


if __name__ == '__main__':
    main()
