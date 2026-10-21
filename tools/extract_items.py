# -*- coding: utf-8 -*-
"""
从台服 Script.pvf + DNF86物品代码表.xlsx 生成 items.json（含子类型维度字段）。

依赖：pip install zhconv openpyxl
用法：python extract_items.py --pvf "D:\\DNF\\Script.pvf" \
                              --xlsx "D:\\DNF\\items.xlsx" \
                              --out  "..\\items.json"

输出字段（键名刻意压缩，items.json 要跟着 exe 一起发）：
  i  物品代码
  n  物品名称（简体）
  c  物品类型（大类，界面「物品类型」下拉的取值）
  s  子类型码，语义随大类不同：
       装备类 = xlsx「子类型」列原值（10 短剑 / 211 布甲上衣 / 310 项链 / 1010 头部 …）
       材料/消耗品 = 用途码（见 USE_*，由 PVF [stackable type] 归纳）
  r  稀有度码 0 普通 / 1 高级 / 2 稀有 / 3 神器 / 4 史诗 / 5 传说（仅装备）
  l  等级（仅装备）
  d  来源码（仅材料/消耗品，见 SRC_*，由 PVF 目录归纳）

PVF 结构（86 版实测）：
  header : uuid_len(4) + uuid(36) + version(4) + dirTreeLength(4) + crc32(4) + numFiles(4)
  解密   : 每 32 位 word 先 XOR (crc32 ^ 0x81A79011)，再循环左移 6 位
  树叶   : fileSize(4) + pathLen(4) + path + fileSize(4) + crc32(4) + offset(4)
  文件   : 用各自的 crc32 再做一次同样的解密
  .stk   : 从 offset 2 起，每 5 字节一个单元 [type(1)][value(4)]
           type 9 = 引用某个 .str（value 是 n_string.lst 下标，下一个单元是键）
           type 5/6/7/8 = 直接 stringtable 下标
           物品名在 [name] 段之后
"""
import argparse
import json
import struct
import sys
from collections import Counter
from pathlib import Path

try:
    from zhconv import convert
except ImportError:
    sys.exit("缺少依赖：pip install zhconv")


def decrypt(buf: bytes, crc: int) -> bytes:
    key = 0x81A79011
    xor = crc ^ key
    n = len(buf) // 4
    if n == 0:
        return buf
    x = int.from_bytes(buf[: n * 4], "little") ^ int.from_bytes(
        xor.to_bytes(4, "little") * n, "little")
    b = x.to_bytes(n * 4, "little")
    out = bytearray(n * 4)
    m1 = 0b111111
    for i in range(0, n * 4, 4):
        v = int.from_bytes(b[i:i + 4], "little")
        out[i:i + 4] = ((((v & m1) << 26) | ((v & 0xFFFFFFC0) >> 6)) & 0xFFFFFFFF).to_bytes(4, "little")
    return bytes(out)


class Pvf:
    def __init__(self, path):
        self.fp = open(path, "rb")
        uuid_len = struct.unpack("i", self.fp.read(4))[0]
        self.uuid = self.fp.read(uuid_len)
        self.version = struct.unpack("i", self.fp.read(4))[0]
        tree_len = struct.unpack("i", self.fp.read(4))[0]
        tree_crc = struct.unpack("I", self.fp.read(4))[0]
        self.num_files = struct.unpack("I", self.fp.read(4))[0]
        blob = self.fp.read(tree_len)
        self.data_off = self.fp.tell()

        tree = decrypt(blob, tree_crc)
        self.leaves = {}
        i = 0
        while i + 8 <= len(tree):
            _fn = struct.unpack_from("<I", tree, i)[0]; i += 4
            plen = struct.unpack_from("<I", tree, i)[0]; i += 4
            if plen > 500 or i + plen + 12 > len(tree):
                break
            p = tree[i:i + plen].decode("utf-8", "replace").lower(); i += plen
            flen, fcrc, off = struct.unpack_from("<III", tree, i); i += 12
            self.leaves[p] = (flen, fcrc, off)

        st = self.read("stringtable.bin")
        self.st_count = struct.unpack("<I", st[:4])[0]
        self.st_idx = st[4:]

        ns = self.read("n_string.lst")
        self.ns_paths = []
        i = 2
        while i + 10 <= len(ns):
            a, aa, b, bb = struct.unpack("<bIbI", ns[i:i + 10]); i += 10
            sidx = aa if a == 7 else (bb if b == 7 else None)
            if sidx is not None:
                self.ns_paths.append(self.str(sidx).lower())

        self._str_cache = {}

    def read(self, path: str) -> bytes:
        flen, fcrc, off = self.leaves[path]
        self.fp.seek(self.data_off + off)
        return decrypt(self.fp.read(flen), fcrc)

    def str(self, n: int) -> str:
        a, b = struct.unpack("<II", self.st_idx[n * 4: n * 4 + 8])
        return self.st_idx[a:b].decode("big5", "ignore")

    def strdict(self, n: int) -> dict:
        if n in self._str_cache:
            return self._str_cache[n]
        d = {}
        if 0 <= n < len(self.ns_paths) and self.ns_paths[n] in self.leaves:
            for line in self.read(self.ns_paths[n]).decode("big5", "ignore").split("\n"):
                if ">" in line:
                    k, v = line.split(">", 1)
                    d[k.strip()] = v.strip()
        self._str_cache[n] = d
        return d

    @staticmethod
    def units(d: bytes):
        return [(d[2 + k * 5], struct.unpack("<i", d[3 + k * 5: 7 + k * 5])[0])
                for k in range((len(d) - 2) // 5)]

    def items_of(self, path: str):
        """一次读盘取出 [name] 与 [stackable type]，避免每个文件读两遍。"""
        us = self.units(self.read(path))
        name, stype = None, None
        for k, (t, v) in enumerate(us):
            if t != 5 or v >= self.st_count * 2:
                continue
            key = self.str(v)
            # [name] 后面紧跟 type 9（str 引用）+ 键
            if key == "[name]" and k + 2 < len(us) and us[k + 1][0] == 9:
                nm = self.strdict(us[k + 1][1]).get(self.str(us[k + 2][1]))
                if nm:
                    name = convert(nm, "zh-cn")
            elif key == "[stackable type]" and k + 1 < len(us) and us[k + 1][0] == 7:
                stype = self.str(us[k + 1][1])
        return name, stype

    def name_of(self, path: str):
        return self.items_of(path)[0]


# ----------------------------------------------------------------------
# 维度编码表（必须与 C# 侧 FacetTable 的顺序一致）
# ----------------------------------------------------------------------
USE_MATERIAL, USE_CONSUMABLE, USE_RECIPE, USE_QUEST = 1, 2, 3, 4
USE_PACKAGE, USE_EMBLEM, USE_THROW, USE_OTHER = 5, 6, 7, 8

# PVF [stackable type] → 用途码。未列出的（booster / dye / contract / etc …）一律算消耗品。
USE_MAP = {
    "[material]": USE_MATERIAL,
    "[material expert job]": USE_MATERIAL,
    "[waste]": USE_MATERIAL,
    "[unlimited waste]": USE_MATERIAL,
    "[upgrade limit cube]": USE_MATERIAL,

    "[recipe]": USE_RECIPE,

    "[quest]": USE_QUEST,
    "[quest receive]": USE_QUEST,

    "[cera package]": USE_PACKAGE,
    "[usable cera package]": USE_PACKAGE,
    "[legacy]": USE_PACKAGE,
    "[upgradable legacy]": USE_PACKAGE,
    "[multi upgradable legacy]": USE_PACKAGE,
    "[multi upgradable legacy bonus cera]": USE_PACKAGE,
    "[stackable legacy]": USE_PACKAGE,

    "[avatar emblem]": USE_EMBLEM,
    "[enchant waste]": USE_EMBLEM,

    "[throw]": USE_THROW,
    "[set]": USE_THROW,

    None: USE_OTHER,
}

SRC_COMMON, SRC_CASH, SRC_EVENT, SRC_QUEST = 1, 2, 3, 4
SRC_JOB, SRC_CARD, SRC_EMBLEM = 5, 6, 7

# PVF 目录 → 来源码。未列出的（material / ect / recipe / root …）一律算「通用/野外」。
SRC_MAP = {
    "cash": SRC_CASH,
    "event": SRC_EVENT,
    "twdf": SRC_EVENT,
    "quest": SRC_QUEST,
    "professional": SRC_JOB,
    "monsterCard": SRC_CARD,
    "pvpCard": SRC_CARD,
    "pvpmaterial": SRC_CARD,
    "emblem": SRC_EMBLEM,
}

# xlsx 稀有度 → 码
RARITY = {"普通": 0, "高级": 1, "稀有": 2, "神器": 3, "史诗": 4, "传说": 5}


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--pvf", required=True)
    ap.add_argument("--xlsx", default="")
    ap.add_argument("--out", default="../items.json")
    a = ap.parse_args()

    pvf = Pvf(a.pvf)
    print(f"PVF {pvf.uuid.decode()}  文件数 {len(pvf.leaves)}  字符串 {pvf.st_count}")

    # ---- 消耗品 / 材料：从 stackable.lst 反查 id → 相对路径 ----
    lst = pvf.read("stackable/stackable.lst")
    pairs, i = [], 2
    while i + 10 <= len(lst):
        x, xx, y, yy = struct.unpack("<bIbI", lst[i:i + 10]); i += 10
        iid = xx if x == 2 else (yy if y == 2 else None)
        sidx = xx if x == 7 else (yy if y == 7 else None)
        if iid is not None and sidx is not None:
            pairs.append((iid, pvf.str(sidx)))

    items, seen = [], set()
    use_cnt, src_cnt = Counter(), Counter()
    for iid, rel in pairs:
        p = "stackable/" + rel.lower()
        if p not in pvf.leaves:
            continue
        nm, stype = pvf.items_of(p)
        if not nm or iid in seen:
            continue
        seen.add(iid)
        use = USE_MAP.get(stype, USE_CONSUMABLE)
        src = SRC_MAP.get(rel.split("/")[0] if "/" in rel else "", SRC_COMMON)
        use_cnt[use] += 1
        src_cnt[src] += 1
        items.append({"i": iid, "n": nm, "c": "材料/消耗品", "s": use, "d": src})
    print(f"消耗品 {len(items)} 条  用途分布 {sorted(use_cnt.items())}  来源分布 {sorted(src_cnt.items())}")

    # ---- 装备：xlsx（子类型 / 等级 / 稀有度 一并取出） ----
    if a.xlsx and Path(a.xlsx).exists():
        import openpyxl
        wb = openpyxl.load_workbook(a.xlsx, read_only=True, data_only=True)
        n0 = len(items)
        sub_cnt, rar_cnt = Counter(), Counter()
        for row in wb["全部装备"].iter_rows(min_row=3, values_only=True):
            if not row or row[0] is None:
                continue
            try:
                iid = int(row[0])
            except (TypeError, ValueError):
                continue
            nm, cat = (row[1] or "").strip(), (row[3] or "装备").strip()
            if not nm or iid in seen:
                continue
            seen.add(iid)

            try:
                sub = int(row[4])
            except (TypeError, ValueError):
                sub = 0
            try:
                lv = int(row[5])
            except (TypeError, ValueError):
                lv = 0
            rar = RARITY.get((row[6] or "").strip(), -1)

            sub_cnt[sub] += 1
            rar_cnt[rar] += 1
            items.append({"i": iid, "n": nm, "c": cat, "s": sub, "r": rar, "l": lv})
        print(f"装备 {len(items) - n0} 条  子类型 {len(sub_cnt)} 种  稀有度 {sorted(rar_cnt.items())}")

    items.sort(key=lambda x: x["i"])
    Path(a.out).write_text(json.dumps(items, ensure_ascii=False, separators=(",", ":")), encoding="utf-8")
    print(f"已写入 {a.out}：{len(items)} 条")


if __name__ == "__main__":
    main()
