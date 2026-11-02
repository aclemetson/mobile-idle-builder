"""Surgical edits to Assets/Data/game_data.json that keep its hand-formatted layout (used by rebalance scripts).

Objects inside a top-level array are re-serialised in the file's own style:
4-space object indent, 6-space keys, scalar arrays inline, arrays of objects one object per line.
"""
import json

from pathlib import Path
PATH = str(Path(__file__).resolve().parent.parent / "Assets" / "Data" / "game_data.json")


def _scan_close(text, i):
    """i points at '[' or '{'; return index of the matching closer."""
    depth, in_str, esc = 0, False, False
    for j in range(i, len(text)):
        c = text[j]
        if in_str:
            if esc: esc = False
            elif c == "\\": esc = True
            elif c == '"': in_str = False
            continue
        if c == '"': in_str = True
        elif c in "[{": depth += 1
        elif c in "]}":
            depth -= 1
            if depth == 0:
                return j
    raise ValueError("unbalanced")


ASCII = [True]   # current object's escaping style (the file mixes raw and \u-escaped text)


def _val(v):
    return json.dumps(v, ensure_ascii=ASCII[0])


def style_of(span):
    """True = escape non-ASCII as \\uXXXX (matches spans that already do, or are pure ASCII)."""
    return "\\u" in span or all(ord(c) < 128 for c in span)


def fmt_obj(o, ind=4, ascii_=True):
    ASCII[0] = ascii_
    pad, kpad = " " * ind, " " * (ind + 2)
    lines = []
    for k, v in o.items():
        if isinstance(v, list) and v and all(isinstance(x, dict) for x in v):
            inner = ",\n".join(kpad + "  { " + ", ".join(f"{_val(a)}: {_val(b)}" for a, b in x.items()) + " }" for x in v)
            s = f"{kpad}{_val(k)}: [\n{inner}\n{kpad}]"
        elif isinstance(v, list):
            s = f"{kpad}{_val(k)}: [" + ", ".join(_val(x) for x in v) + "]"
        else:
            s = f"{kpad}{_val(k)}: {_val(v)}"
        lines.append(s)
    return pad + "{\n" + ",\n".join(lines) + "\n" + pad + "}"


class GameData:
    def __init__(self, path=PATH):
        self.path = path
        self.text = open(path, encoding="utf-8").read()

    def save(self):
        json.loads(self.text)  # must still parse
        open(self.path, "w", encoding="utf-8", newline="\n").write(self.text)

    def _section(self, name):
        key = f'\n  "{name}": ['
        s = self.text.index(key) + len(key) - 1
        return s, _scan_close(self.text, s)

    def _spans(self, section):
        s, e = self._section(section)
        i = s + 1
        while True:
            j = self.text.find("{", i, e)
            if j < 0: return
            k = _scan_close(self.text, j)
            yield j, k
            i = k + 1

    def _find(self, section, oid):
        for j, k in self._spans(section):
            o = json.loads(self.text[j:k + 1])
            if o.get("id") == oid:
                return j, k, o
        raise KeyError(f"{section}/{oid}")

    def get(self, section, oid):
        return self._find(section, oid)[2]

    def has(self, section, oid):
        try: self._find(section, oid); return True
        except KeyError: return False

    def put(self, section, o):
        j, k, _ = self._find(section, o["id"])
        start = self.text.rfind("\n", 0, j) + 1
        self.text = self.text[:start] + fmt_obj(o, ascii_=style_of(self.text[j:k + 1])) + self.text[k + 1:]

    def update(self, section, oid, **changes):
        o = self.get(section, oid)
        o.update(changes)
        self.put(section, o)
        return o

    def insert_after(self, section, after_id, o):
        _, k, _ = self._find(section, after_id)
        self.text = self.text[:k + 1] + ",\n" + fmt_obj(o) + self.text[k + 1:]

    def delete(self, section, oid):
        j, k, _ = self._find(section, oid)
        start = self.text.rfind("\n", 0, j) + 1
        # swallow the separating comma: the one after the object if present, else the one before
        after = self.text[k + 1:]
        if after.lstrip().startswith(","):
            end = k + 1 + after.index(",") + 1
            self.text = self.text[:start] + self.text[end:].lstrip("\n")
        else:
            comma = self.text.rfind(",", 0, start)
            self.text = self.text[:comma] + "\n" + self.text[k + 1:].lstrip("\n")

    def set_line(self, section, oid, key, value):
        """Replace a single-line `"key": value` inside one object, leaving the rest byte-identical."""
        j, k, _ = self._find(section, oid)
        span = self.text[j:k + 1]
        tag = f'"{key}": '
        a = span.index(tag) + len(tag)
        b = span.index("\n", a)
        line_end = span[a:b]
        trail = "," if line_end.rstrip().endswith(",") else ""
        new = span[:a] + json.dumps(value, ensure_ascii=style_of(span)) + trail + span[b:]
        json.loads(new)
        self.text = self.text[:j] + new + self.text[k + 1:]
