"""从本机游戏安装包和已下载的热更新提取剪影，不修改模拟器文件。需要 Pillow。"""

import argparse
import hashlib
import io
import json
import re
import subprocess
import tempfile
import zipfile
from pathlib import Path

from PIL import Image

ROOT = Path(__file__).resolve().parent.parent
REMOTE = "/data/data/com.youzu.djlw/files/version/res/game_assets/images/silhouette"


def decode_image(data):
    # 客户端 UF 图片把 PNG 高度移到六字节前缀，恢复后按标准 PNG 校验解码。
    if data[:2] == b"UF" and data[6:14] == b"\x89PNG\r\n\x1a\n":
        data = data[6:26] + data[2:6] + data[26:]
    image = Image.open(io.BytesIO(data)).convert("RGBA")
    if image.size != (300, 300):
        raise ValueError(f"剪影尺寸异常：{image.size}")
    return image


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--adb", required=True, help="ADB 程序路径")
    parser.add_argument("--serial", required=True, help="设备序列号")
    parser.add_argument("--apk", type=Path, help="已有 APK，可避免重复拉取")
    parser.add_argument("--output", type=Path, default=ROOT / "assets/resource/base/image/silhouette")
    args = parser.parse_args()

    def adb(*command):
        return subprocess.run([args.adb, "-s", args.serial, *command],
                              check=True, capture_output=True).stdout

    catalog = json.loads((ROOT / "assets/resource/base/SwordBookCatalog.json").read_text(encoding="utf-8"))
    names = {int(item["number"]): item["name"] for item in catalog if item["number"].isdigit()}
    assets = {}
    with tempfile.TemporaryDirectory(prefix="matr-silhouette-") as temp:
        apk = args.apk
        if apk is None:
            package = adb("shell", "pm", "path", "com.youzu.djlw").decode().strip().splitlines()
            remote_apk = next(line.removeprefix("package:") for line in package if line.endswith("/base.apk"))
            apk = Path(temp) / "base.apk"
            adb("pull", remote_apk, str(apk))
        with zipfile.ZipFile(apk) as archive:
            for name in archive.namelist():
                match = re.fullmatch(r"assets/res/game_assets/images/silhouette/(\d+)/([12])\.png", name)
                if match:
                    assets[tuple(map(int, match.groups()))] = (archive.read(name), "安装包")
        # 热更新覆盖相同编号和部位；需要先执行 adb root 或使用已启用 root 的设备。
        listing = adb("shell", "find", REMOTE, "-type", "f").decode().splitlines()
        for name in listing:
            match = re.fullmatch(re.escape(REMOTE) + r"/(\d+)/([12])\.png", name.strip())
            if match:
                assets[tuple(map(int, match.groups()))] = (adb("exec-out", "cat", name.strip()), "热更新")
        if not assets:
            raise RuntimeError("没有找到剪影素材")
        unknown = sorted({sid for sid, _ in assets if sid not in names})
        if unknown:
            raise ValueError(f"名册缺少以下编号，需先核实名称：{unknown}")
        # 先校验全部图片，避免读取失败留下部分更新的图库。
        converted = []
        for (sid, view), (data, source) in sorted(assets.items()):
            image = decode_image(data)
            mask = image.getchannel("A").point(lambda value: 0 if value > 127 else 255)
            if mask.getextrema() == (255, 255):
                raise ValueError(f"素材没有有效轮廓：{sid}/{view}")
            filename = f"{sid:05d}_{names[sid]}_{'head' if view == 1 else 'foot'}.png"
            converted.append((filename, mask, {"file": filename, "source": source,
                              "source_sha256": hashlib.sha256(data).hexdigest()}))
        args.output.mkdir(parents=True, exist_ok=True)
        for filename, mask, _ in converted:
            mask.save(args.output / filename)
        manifest = {"package": "com.youzu.djlw", "alpha_threshold": 127,
                    "templates": [entry for _, _, entry in converted]}
        (args.output / "manifest.json").write_text(json.dumps(manifest, ensure_ascii=False, indent=2) + "\n", encoding="utf-8")
        print(f"已提取 {len(converted)} 张剪影，{len(set(sid for sid, _ in assets))} 个编号，目录：{args.output}")


if __name__ == "__main__":
    main()
