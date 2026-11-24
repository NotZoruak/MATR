"""检查桌面产物的核心程序集固定路径加载行为。"""

import argparse
import os
from pathlib import Path
import shutil
import subprocess
import tempfile


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("output_dir", type=Path, help="桌面构建或发布目录")
    args = parser.parse_args()
    source = args.output_dir.resolve()
    core_relative = Path("runtimes/libs/MFAAvalonia.Core.dll")
    workspace = Path(__file__).resolve().parent.parent
    scratch = workspace / "debug"
    scratch.mkdir(exist_ok=True)

    with tempfile.TemporaryDirectory(prefix="core-layout-", dir=scratch) as temporary:
        target = Path(temporary)
        shutil.copytree(source, target, dirs_exist_ok=True)
        core = target / core_relative
        if not core.is_file():
            raise AssertionError(f"固定目录缺少核心程序集：{core}")
        root_core = target / "MFAAvalonia.Core.dll"
        root_core.unlink(missing_ok=True)

        def run():
            trace = target / "host-trace.txt"
            trace.unlink(missing_ok=True)
            environment = dict(os.environ)
            environment["DOTNET_HOST_TRACE"] = "1"
            environment["DOTNET_HOST_TRACEFILE"] = str(trace)
            process = subprocess.run(
                ["dotnet", str(target / "MATR.dll"), "--help"],
                cwd=target,
                env=environment,
                capture_output=True,
                timeout=30,
            )
            return process, trace.read_text(encoding="utf-8-sig", errors="replace")

        def assert_fixed_path(trace):
            entries = [
                line for line in trace.splitlines()
                if "Adding tpa entry:" in line and "MFAAvalonia.Core.dll" in line
            ]
            if not entries or any(str(core).lower() not in line.lower() for line in entries):
                raise AssertionError(f"运行时未固定加载依赖目录核心程序集：{entries}")

        process, trace = run()
        if process.returncode != 0:
            raise AssertionError(f"无根目录副本时启动失败：{process.stderr.decode(errors='replace')}")
        assert_fixed_path(trace)

        decoy = "此文件用于确认程序不会加载根目录旧核心副本".encode("utf-8")
        root_core.write_bytes(decoy)
        process, trace = run()
        if process.returncode != 0:
            raise AssertionError(f"根目录残留无效副本时启动失败：{process.stderr.decode(errors='replace')}")
        assert_fixed_path(trace)
        if root_core.read_bytes() != decoy:
            raise AssertionError("启动不应通过删除或改写根目录副本规避错误加载")

        shutil.copyfile(core, root_core)
        core.unlink()
        process, _ = run()
        if process.returncode == 0:
            raise AssertionError("固定目录缺失时错误回退到根目录核心程序集")

    print("核心程序集加载检查通过：正常启动、忽略根目录副本、固定目录缺失时拒绝回退。")


if __name__ == "__main__":
    main()
