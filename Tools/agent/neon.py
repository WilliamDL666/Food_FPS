# -*- coding: utf-8 -*-
"""霓城封锁 · Agent 工具入口：把"改 → 打包 → 测 → 读结果"变成一条命令，输出精简摘要给 Agent 读。

用法（在项目根目录）：
  python Tools/agent/neon.py data                 校验数值表（列、类型、范围、跨字段规则）
  python Tools/agent/neon.py build [--full]       Unity 命令行打包（--full = 重建场景 + 烘焙光照 + 打包，很慢）
  python Tools/agent/neon.py test                 规则 / 技能 / 交互自动化测试
  python Tools/agent/neon.py range                靶场：7 把枪 × 胸 / 头 实测 vs 理论 TTK
  python Tools/agent/neon.py match [bomb|ffa|cook|grow]   AI 整场
  python Tools/agent/neon.py shots                渲染检查截图 → Build/Screenshots
  python Tools/agent/neon.py verify [--no-build]  data → build → test → range → match bomb

快速迭代：打包后的程序运行时读 Build/NeonSiegeDemo_Data/StreamingAssets/Data/*.csv，
只调数值时可以直接改那份 CSV 然后跑 range / match，不用重新打包（加 --build-data 让 data 校验那份）。
注意：build 会用 C# 默认值覆盖 Assets/StreamingAssets/Data/*.csv，数值定稿必须写回 C#（见 CLAUDE.md）。

环境变量：NEON_UNITY 指定 Unity.exe 路径。退出码：0 全部通过，1 有失败，2 环境问题。
"""
import csv, json, os, re, subprocess, sys, time

ROOT = os.path.abspath(os.path.join(os.path.dirname(__file__), "..", ".."))
UNITY = os.environ.get("NEON_UNITY", r"D:\DMT\Unity Hub\2022.3.54f1\Editor\Unity.exe")
EXE = os.path.join(ROOT, "Build", "NeonSiegeDemo.exe")
LOGS = os.path.join(ROOT, "Logs", "agent")
DATA_SRC = os.path.join(ROOT, "Assets", "StreamingAssets", "Data")
DATA_BUILD = os.path.join(ROOT, "Build", "NeonSiegeDemo_Data", "StreamingAssets", "Data")
RULES = os.path.join(os.path.dirname(__file__), "data_rules.json")

if hasattr(sys.stdout, "reconfigure"):
    sys.stdout.reconfigure(encoding="utf-8", line_buffering=True)  # 后台运行时也能实时看到进度


def log_path(name):
    os.makedirs(LOGS, exist_ok=True)
    return os.path.join(LOGS, name + "_" + time.strftime("%Y%m%d_%H%M%S") + ".log")


def run(args, log, timeout):
    """跑一个进程，返回 (退出码, 日志全文)。超时返回 -1。"""
    t0 = time.time()
    try:
        code = subprocess.run(args + ["-logFile", log], cwd=ROOT, timeout=timeout).returncode
    except subprocess.TimeoutExpired:
        code = -1
    text = ""
    if os.path.exists(log):
        with open(log, encoding="utf-8", errors="replace") as f:
            text = f.read()
    print("  耗时 %.0fs，退出码 %s，日志 %s" % (time.time() - t0, code, os.path.relpath(log, ROOT)))
    return code, text


def need(path, what):
    if not os.path.exists(path):
        print("[环境] 找不到%s：%s" % (what, path))
        sys.exit(2)


# ---------------------------------------------------------------- data
def read_csv(path):
    with open(path, encoding="utf-8-sig", newline="") as f:
        return list(csv.DictReader(f))


def num(v):
    try:
        return float(v)
    except (TypeError, ValueError):
        return None


def cmd_data(use_build=False):
    folder = DATA_BUILD if use_build else DATA_SRC
    with open(RULES, encoding="utf-8") as f:
        rules = json.load(f)
    errors, warns = [], []
    print("[data] 校验 %s" % folder)
    for table, spec in rules["tables"].items():
        path = os.path.join(folder, table)
        if not os.path.exists(path):
            errors.append("%s 不存在" % table); continue
        rows = read_csv(path)
        cols = list(rows[0].keys()) if rows else []
        missing = [c for c in spec["columns"] if c not in cols]
        extra = [c for c in cols if c not in spec["columns"]]
        if missing: errors.append("%s 缺少列：%s" % (table, ", ".join(missing)))
        if extra: warns.append("%s 多出列（游戏会忽略）：%s" % (table, ", ".join(extra)))
        key = spec.get("key", cols[0] if cols else "")
        if spec.get("kv"):  # key,value 形式的表
            values = {r["key"]: r["value"] for r in rows}
            for k in spec.get("required_keys", []):
                if k not in values: errors.append("%s 缺少键 %s" % (table, k))
            for k, v in values.items():
                check_value(table, k, "value", v, spec.get("ranges", {}).get(k), errors, warns)
            check_rules(table, "-", spec, {k: num(v) for k, v in values.items()}, errors, warns)
            continue
        for r in rows:
            rid = r.get(key, "?")
            for col, rng in spec.get("ranges", {}).items():
                if col in r: check_value(table, rid, col, r[col], rng, errors, warns)
            check_rules(table, rid, spec, {k: num(v) if num(v) is not None else v for k, v in r.items()}, errors, warns)
    if not use_build and os.path.isdir(DATA_BUILD):
        for table in rules["tables"]:
            a, b = os.path.join(DATA_SRC, table), os.path.join(DATA_BUILD, table)
            if os.path.exists(a) and os.path.exists(b) and read_csv(a) != read_csv(b):
                warns.append("%s：Build 里的表和 Assets 里的不一样（有人在打包后的程序里调过参数，记得写回 C# 默认值）" % table)
    for e in errors: print("  ✗ " + e)
    for w in warns: print("  ! " + w)
    print("[data] %s：%d 错误，%d 警告" % ("通过" if not errors else "未通过", len(errors), len(warns)))
    return 0 if not errors else 1


def check_rules(table, rid, spec, ctx, errors, warns):
    for rule in spec.get("rules", []):
        why = rule["why"]
        try:
            ok = eval(rule["expr"], {"__builtins__": {}}, ctx)
        except Exception as e:
            ok, why = False, why + "（表达式出错：%s）" % e
        if not ok:
            (errors if rule.get("level") == "error" else warns).append("%s[%s] %s  —— %s" % (table, rid, rule["expr"], why))


def check_value(table, rid, col, raw, rng, errors, warns):
    v = num(raw)
    if v is None:
        errors.append("%s[%s].%s = %r 不是数字" % (table, rid, col, raw)); return
    if not rng: return
    lo, hi = rng[0], rng[1]
    if v < lo or v > hi:
        errors.append("%s[%s].%s = %s 超出范围 [%s, %s]" % (table, rid, col, raw, lo, hi))


# ---------------------------------------------------------------- build / play
def cmd_build(full=False):
    need(UNITY, " Unity 2022.3.54f1（设置 NEON_UNITY）")
    method = "BuildDemo.SetupAndBuild" if full else "BuildDemo.BuildWindows"
    print("[build] %s（Unity 编辑器必须关掉这个项目，否则命令行打不开）" % method)
    code, text = run([UNITY, "-batchmode", "-quit", "-projectPath", ROOT, "-executeMethod", method], log_path("build"), 3600 if full else 1500)
    errs = sorted(set(re.findall(r"^(Assets/.*?error CS\d+:.*)$", text, re.M)))
    for e in errs[:30]: print("  ✗ " + e)
    if "another Unity instance is running" in text or "It looks like another Unity instance" in text:
        print("  ✗ 项目正被 Unity 编辑器打开，请先关闭编辑器"); return 2
    ok = "NEON_BUILD_OK" in text and code == 0
    m = re.search(r"NEON_BUILD_OK .*", text)
    print("[build] %s %s" % ("通过" if ok else "未通过", m.group(0) if m else ""))
    if not ok and not errs:
        for line in [l for l in text.splitlines() if "Exception" in l or "Error" in l][:15]: print("  " + line)
    return 0 if ok else 1


def play(name, flags, timeout, graphics=False):
    need(EXE, "打包程序（先跑 build）")
    args = [EXE, "-batchmode"] + ([] if graphics else ["-nographics"]) + flags
    print("[%s] %s" % (name, " ".join(flags)))
    return run(args, log_path(name), timeout)


def cmd_test():
    code, text = play("test", ["-neonTests"], 900, graphics=True)  # M15 读材质颜色，-nographics 下 shader 不加载会误判
    fails = re.findall(r"NEON_TEST (\S+) FAIL (.*)", text)
    for tid, detail in fails: print("  ✗ %s %s" % (tid, detail[:300]))
    m = re.search(r"NEON_TESTS_DONE passed=(\d+) failed=(\d+)", text)
    if not m:
        print("[test] 没有跑完（崩溃或超时），看日志"); return 1
    print("[test] %s 项通过，%s 项未通过 → Build/Playtest/test_report.md" % m.groups())
    return 0 if m.group(2) == "0" else 1


def cmd_range():
    code, text = play("range", ["-neonRangeTest"], 600, graphics=True)
    for line in re.findall(r"NEON_RANGE_RESULT (.*)", text): print("  " + line)
    for line in re.findall(r"NEON_RANGE_WALL (.*)", text): print("  穿墙 " + line)
    ok = "NEON_RANGE_DONE" in text
    print("[range] %s → Build/range_report.md" % ("完成" if ok else "没有跑完，看日志"))
    return 0 if ok else 1


MATCH_FLAGS = {"bomb": "-neonMatchTest", "ffa": "-neonFFATest", "cook": "-neonCookTest", "grow": "-neonGrowTest"}


def cmd_match(mode="bomb"):
    code, text = play("match_" + mode, [MATCH_FLAGS[mode]], 1800)
    for pat in [r"NEON_MATCH_DONE .*", r"NEON_ROUND .*", r"NEON_FFA_DONE .*", r"NEON_COOK_DONE .*", r"NEON_TELEMETRY .*"]:
        for line in re.findall(pat, text): print("  " + line[:300])
    exc = [l for l in text.splitlines() if "Exception" in l]
    for l in exc[:10]: print("  ✗ " + l)
    done = re.search(r"NEON_(MATCH|FFA|COOK)_DONE", text) is not None
    ok = done and code == 0 and not exc
    print("[match %s] %s，异常 %d 条" % (mode, "通过" if ok else "未通过", len(exc)))
    return 0 if ok else 1


def cmd_shots():
    code, text = play("shots", ["-neonShots"], 900, graphics=True)
    ok = "NEON_SHOTS_DONE" in text
    print("[shots] %s → Build/Screenshots（用 Read 工具打开 png 检查画面）" % ("完成" if ok else "没有跑完"))
    return 0 if ok else 1


def main(argv):
    if not argv or argv[0] in ("-h", "--help"):
        print(__doc__); return 0
    cmd, rest = argv[0], argv[1:]
    if cmd == "data": return cmd_data("--build-data" in rest)
    if cmd == "build": return cmd_build("--full" in rest)
    if cmd == "test": return cmd_test()
    if cmd == "range": return cmd_range()
    if cmd == "match": return cmd_match(rest[0] if rest else "bomb")
    if cmd == "shots": return cmd_shots()
    if cmd == "verify":
        steps = [("data", cmd_data)] + ([] if "--no-build" in rest else [("build", cmd_build)]) + \
                [("test", cmd_test), ("range", cmd_range), ("match", cmd_match)]
        results = []
        for name, fn in steps:
            r = fn(); results.append((name, r))
            if name in ("data", "build") and r != 0: break
        print("\n==== verify 汇总 ====")
        for name, r in results: print("  %-6s %s" % (name, "通过" if r == 0 else "未通过"))
        return 0 if all(r == 0 for _, r in results) and len(results) == len(steps) else 1
    print("未知命令：%s" % cmd); print(__doc__); return 2


if __name__ == "__main__":
    sys.exit(main(sys.argv[1:]))
