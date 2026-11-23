"""从已解码客户端表生成按奖励类型与编号索引的物品名称库。"""
import argparse
import hashlib
import json
from extract_game_sword_catalog import read_table
from pathlib import Path


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--cfg-dir', required=True, type=Path)
    parser.add_argument('--output', type=Path, default=Path('_src/MFAAvalonia/Assets/GameItemCatalog.json'))
    args = parser.parse_args()
    # 类型与查表字段依据客户端ItemConst及ItemData.getRewardName。
    tables = [(1, 'consumable_master', 'consumable_id', 'name'),
              (2, 'sword_master', 'sword_id', 'name'),
              (3, 'equip_master', 'equip_id', 'name'),
              (6, 'furniture_master', 'furniture_id', 'name'),
              (10, 'special_voice_master', 'category', 'voice_name'),
              (110, 'particle_master', 'particle_id', 'name')]
    names = {}
    sources = {}
    for kind, table, id_key, name_key in tables:
        sources[table] = hashlib.sha256((args.cfg_dir / (table + '.lua')).read_bytes()).hexdigest()
        for row in read_table(args.cfg_dir, table):
            key = f'{kind}:{row[id_key]}'
            name = str(row[name_key]).strip()
            # 敌方或占位刀剑存在空名称，不为其编造展示文本。
            if not name:
                continue
            if key in names:
                raise ValueError('重复编号：' + key)
            names[key] = name
    names.update({f'5:{i}': name for i, name in enumerate(['委托符', '木炭', '玉钢', '冷却材', '砥石'], 1)})
    document = dict(sources=sources, names=names, typeNames={'4': '小判', '50': '甲州金'})
    args.output.parent.mkdir(parents=True, exist_ok=True)
    args.output.write_text(json.dumps(document, ensure_ascii=False, indent=2) + '\n', encoding='utf-8')
    print(f'已生成物品名称：{len(names)}条，另含小判与甲州金类型名称')


if __name__ == '__main__':
    main()
