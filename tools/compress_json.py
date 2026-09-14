"""MATR pipeline JSON 排版工具。

默认行为：把四元素数组和单键值对压缩到单行，并删除空的 param。
--sort-keys：在排版前按键位顺序重排字段，顺序对齐 @nekosu/prettier-plugin-maafw-sort。
--interface：把 assets/interface.json 一并纳入处理，其 pipeline_override 内的 node 同样重排。

用法：
    python tools/compress_json.py [路径 ...]                              仅排版
    python tools/compress_json.py --sort-keys [--interface] [路径 ...]     排版并重排字段
"""
import json, sys, glob, os


def clean_empty_param(obj):
    """递归删除所有值为 {} 的 "param" 键。"""
    if isinstance(obj, dict):
        return {k: clean_empty_param(v) for k, v in obj.items() if not (k == "param" and v == {})}
    elif isinstance(obj, list):
        return [clean_empty_param(v) for v in obj]
    return obj


# node 内字段的顺序，取自 @nekosu/prettier-plugin-maafw-sort 的 processPipelineTask；
# 该顺序按执行流程排列：门槛字段 -> 识别 -> 动作前等待 -> 动作 -> 动作后等待 -> 流程控制。
# is_sub 与 interrupt 已于 MaaFramework 5.1 废弃，不列入顺序表，资源中也不允许再使用。
FIELD_ORDER = [
    "desc", "doc",
    "enabled", "max_hit",
    "sub_name",
    "recognition",
    "inverse",
    "pre_wait_freezes", "pre_delay",
    "action",
    "anchor",
    "repeat", "repeat_wait_freezes", "repeat_delay",
    "post_wait_freezes", "post_delay",
    "timeout", "rate_limit", "next", "on_error",
    "focus", "attach",
]
FIELD_RANK = {name: index for index, name in enumerate(FIELD_ORDER)}

# recognition.param 与 action.param 的键序，同样取自该插件
RECO_KEYS = [
    "custom_recognition", "custom_recognition_param",
    "roi", "roi_offset",
    "template", "green_mask", "method",
    "detector", "ratio",
    "lower", "upper", "connected",
    "expected", "replace", "only_rec", "model", "color_filter", "labels",
    "threshold", "count",
    "all_of", "any_of", "box_index",
    "order_by", "index",
]
ACT_KEYS = [
    "custom_action", "custom_action_param",
    "target", "target_offset",
    "begin", "begin_offset", "end", "end_offset",
    "end_hold", "only_hover", "duration", "contact", "pressure",
    "swipes",
    "dx", "dy",
    "key",
    "input_text",
    "package",
    "exec", "args", "detach",
    "cmd", "shell_timeout",
    "filename", "format", "quality",
]
SWIPE_KEYS = [
    "starting", "begin", "begin_offset", "end", "end_offset",
    "duration", "end_hold", "only_hover", "contact", "pressure",
]
RECO_RANK = {name: index for index, name in enumerate(RECO_KEYS)}
ACT_RANK = {name: index for index, name in enumerate(ACT_KEYS)}
SWIPE_RANK = {name: index for index, name in enumerate(SWIPE_KEYS)}
SECTION_RANK = {"type": 0, "param": 1}


def reorder(obj, rank):
    """按给定键位顺序重排字典，未列入顺序表的键保持原有相对顺序并追加在末尾。"""
    known = sorted((k for k in obj if k in rank), key=lambda k: rank[k])
    unknown = [k for k in obj if k not in rank]
    return {k: obj[k] for k in known + unknown}


def sort_node(node):
    """重排单个 node：顶层字段、recognition.param、action.param 以及 swipes 内的键。"""
    if not isinstance(node, dict):
        return node
    result = dict(node)
    for field, rank in (("recognition", RECO_RANK), ("action", ACT_RANK)):
        section = result.get(field)
        if not isinstance(section, dict):
            continue
        section = dict(section)
        param = section.get("param")
        if isinstance(param, dict):
            param = reorder(param, rank)
            swipes = param.get("swipes")
            if isinstance(swipes, list):
                param["swipes"] = [
                    reorder(item, SWIPE_RANK) if isinstance(item, dict) else item for item in swipes
                ]
            section["param"] = param
        result[field] = reorder(section, SECTION_RANK)
    return reorder(result, FIELD_RANK)


def sort_pipeline_root(data):
    """重排 pipeline 文件顶层「node 名 -> node 对象」结构中的所有 node。"""
    if not isinstance(data, dict):
        return data
    return {name: sort_node(node) if isinstance(node, dict) else node for name, node in data.items()}


def sort_interface_overrides(data):
    """递归查找 interface.json 中的 pipeline_override，并重排其内的 node。"""
    if isinstance(data, dict):
        return {
            key: sort_pipeline_root(value)
            if key == "pipeline_override" and isinstance(value, dict)
            else sort_interface_overrides(value)
            for key, value in data.items()
        }
    if isinstance(data, list):
        return [sort_interface_overrides(item) for item in data]
    return data


def is_interface_path(path):
    """按文件名判断是否为 interface 配置，与 prettier 插件的默认正则口径一致。"""
    return os.path.basename(path).lower() in ("interface.json", "interface.jsonc")


def parse_args(argv):
    """解析命令行参数：--sort-keys 启用字段重排，--interface 追加 interface.json，其余按路径处理。"""
    sort_keys = False
    with_interface = False
    paths = []
    for arg in argv:
        if arg == "--sort-keys":
            sort_keys = True
        elif arg == "--interface":
            with_interface = True
        elif arg.startswith("-"):
            print(f"错误: 未知参数 {arg},当前支持的参数为 --sort-keys 与 --interface", file=sys.stderr)
            sys.exit(2)
        else:
            paths.append(arg)
    return paths, sort_keys, with_interface


def fmt(obj, indent=0):
    sp = "  " * indent
    sp1 = "  " * (indent + 1)

    if isinstance(obj, dict):
        if not obj:
            return "{}"
        # 单键值对且值非嵌套 → 单行
        if len(obj) == 1:
            k, v = next(iter(obj.items()))
            if not isinstance(v, (dict, list)):
                return f'{{"{k}": {fmt(v, 0)}}}'
        items = []
        for k, v in obj.items():
            items.append(f'{sp1}"{k}": {fmt(v, indent + 1)}')
        return "{\n" + ",\n".join(items) + f"\n{sp}}}"

    elif isinstance(obj, list):
        # 纯数字数组 → 单行
        if all(isinstance(x, (int, float)) for x in obj):
            inner = ", ".join(str(x) for x in obj)
            return f"[{inner}]"
        if not obj:
            return "[]"
        # 全字符串数组 → 超过 5 个保持多行，否则单行
        if all(isinstance(x, str) for x in obj):
            if len(obj) <= 5:
                inner = ", ".join(f'"{x}"' for x in obj)
                return f"[{inner}]"
            items = [f"{sp1}\"{x}\"" for x in obj]
            return "[\n" + ",\n".join(items) + f"\n{sp}]"
        items = [f"{sp1}{fmt(x, indent + 1)}" for x in obj]
        return "[\n" + ",\n".join(items) + f"\n{sp}]"

    elif isinstance(obj, bool):
        return "true" if obj else "false"
    elif isinstance(obj, str):
        return json.dumps(obj, ensure_ascii=False)
    elif isinstance(obj, (int, float)):
        return json.dumps(obj)
    elif obj is None:
        return "null"
    return json.dumps(obj, ensure_ascii=False)


if __name__ == "__main__":
    paths, sort_keys, with_interface = parse_args(sys.argv[1:])
    # 基于脚本位置推导项目根目录,保证从任意目录运行都能找到资源文件
    project_root = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
    if not paths:
        paths = sorted(glob.glob(os.path.join(project_root, "assets", "resource", "base", "pipeline", "*.json")))
    # --interface 表示把 interface.json 一并纳入处理范围,与是否传入其它路径无关
    if with_interface:
        paths.append(os.path.join(project_root, "assets", "interface.json"))
    if not paths:
        print("错误: 未找到 pipeline JSON 文件,请检查项目结构或显式传入路径", file=sys.stderr)
        sys.exit(1)
    for path in paths:
        with open(path, "r", encoding="utf-8") as f:
            data = json.load(f)
        data = clean_empty_param(data)
        if sort_keys:
            data = (
                sort_interface_overrides(data)
                if is_interface_path(path)
                else sort_pipeline_root(data)
            )
        with open(path, "w", encoding="utf-8") as f:
            f.write(fmt(data) + "\n")
        print(f"Compressed: {path}" + (" [已按键位重排]" if sort_keys else ""))
