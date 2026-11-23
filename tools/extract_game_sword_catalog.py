"""从本地已解码的客户端静态表生成所持刀剑资料，不执行 Lua。"""
import argparse
import hashlib
import json
import re
from pathlib import Path


def read_table(directory, name):
    text = (directory / (name + ".lua")).read_text(encoding="utf-8")
    block = text[text.index("local __key_map"):]
    block = block[:block.index("}")]
    keys = {int(index) - 1: key for key, index in re.findall(r"(\w+)\s*=\s*(\d+)", block)}
    rows = []
    for line in text.splitlines():
        match = re.match(r"\s*\[\d+\]\s*=\s*\{(.*)\},?\s*$", line)
        if match:
            values = json.loads("[" + match[1].rstrip().rstrip(",") + "]")
            rows.append({key: values[index] for index, key in keys.items() if index < len(values)})
    if not rows:
        raise ValueError("静态表没有数据：" + name)
    return rows


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--cfg-dir", required=True, type=Path)
    parser.add_argument("--output", default="_src/MATR.DefaultExtensions/Resources/GameSwordCatalog.json", type=Path)
    args = parser.parse_args()
    fields = "sword_id name symbol type type_real max_level max_ranbu_level hp hp_up atk atk_up def def_up mobile mobile_up back back_up scout scout_up hide hide_up loyalties grow_type".split()
    swords = {str(row["sword_id"]): {key: row[key] for key in fields} for row in read_table(args.cfg_dir, "sword_master")}
    levels = {str(row["type"]) + ":" + str(row["level"]): row["exp"] for row in read_table(args.cfg_dir, "sword_level_master")}
    equipment = {}
    for name in ["equip_master", "artifact_master"]:
        rows = read_table(args.cfg_dir, name)
        id_key = next(key for key in rows[0] if key.endswith("_id"))
        equipment[name] = {str(row[id_key]): row.get("name", str(row[id_key])) for row in rows}
        if name == "equip_master":
            equipment["equip_soldier"] = {str(row[id_key]): row["soldier"] for row in rows if "soldier" in row}
            equipment["equip_mobile"] = {str(row[id_key]): row["mobile"] for row in rows}
    sources = {name: hashlib.sha256((args.cfg_dir / (name + ".lua")).read_bytes()).hexdigest()
               for name in ["sword_master", "sword_level_master", "equip_master", "artifact_master"]}
    document = {"sources": sources, "swords": swords, "levels": levels, "equipment": equipment}
    args.output.parent.mkdir(parents=True, exist_ok=True)
    args.output.write_text(json.dumps(document, ensure_ascii=False, indent=2) + "\n", encoding="utf-8")
    print("已生成刀剑资料：", len(swords), "；经验档位：", len(levels))


if __name__ == "__main__":
    main()
