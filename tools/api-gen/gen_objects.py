#!/usr/bin/env python3
"""Regenerate the `.temp/*objects.json` class dumps from the Highcharts docs.

`hcconfigs.json`/`hsconfigs.json`/`hmconfigs.json` describe the *options*
objects; the matching `*objects.json` describe the *runtime classes* that the
binding exposes (`Chart`, `Axis`, `Series`, ...). Highcharts publishes those at

    https://api.highcharts.com/class-reference/Highcharts.Chart

with one page per class; this script scrapes the member lists into the flat
shape that `HighchartsGeneratorCommon.HcJson.getObjects` expects.

Only the classes the binding has always exposed are fetched (see `CLASSES`); any
member whose documented type is not one of them is typed as `Object`, because a
WIG type has to be declared in the assembly to be usable.

Usage:
    python3 tools/api-gen/gen_objects.py [--cache dir] [--out .temp]
"""

import argparse
import html
import json
import os
import re
import urllib.parse
import urllib.request

BASE = "https://api.highcharts.com/class-reference/"

# docs page -> class name used in the binding
CLASSES = {
    "Highcharts": "Highcharts",
    "Highcharts.Axis": "Axis",
    "Highcharts.Chart": "Chart",
    "Highcharts.Legend": "Legend",
    "Highcharts.Point": "Point",
    "Highcharts.Series": "Series",
    "Highcharts.SVGRenderer": "Renderer",
    "Highcharts.SVGElement": "Element",
}

# documented type -> binding type
TYPE_MAP = {
    "Highcharts.Chart": "Chart",
    "Highcharts.Axis": "Axis",
    "Highcharts.Series": "Series",
    "Highcharts.Point": "Point",
    "Highcharts.Legend": "Legend",
    "Highcharts.SVGRenderer": "Renderer",
    "Highcharts.SVGElement": "Element",
}

PRIMITIVES = {
    "*": "Object",
    "object": "Object",
    "Object": "Object",
    "Mixed": "Mixed",
    "number": "Number",
    "string": "String",
    "boolean": "Boolean",
    "function": "Function",
    "void": "",
    "undefined": "",
    "null": "",
    "HTMLElement": "HTMLElement",
    "Highcharts.HTMLDOMElement": "HTMLElement",
    "Highcharts.CSSObject": "CSSObject",
    "Highcharts.ColorString": "Color",
    "Highcharts.ColorType": "Color",
    "Highcharts.Dictionary.<*>": "Object",
}

KNOWN = set(CLASSES.values()) | {
    "Object", "Mixed", "Number", "String", "Boolean", "Function",
    "Color", "CSSObject", "HTMLElement", "Array",
}

TAG = re.compile(r"<[^>]+>")
WS = re.compile(r"\s+")


def clean_name(raw):
    """Docs member ids are dotted, URL-encoded and may carry generics."""
    n = urllib.parse.unquote(raw).strip()
    n = re.sub(r"<.*>", "", n).strip()
    return n.lstrip(".-#")


def text(s):
    return WS.sub(" ", html.unescape(TAG.sub("", s))).strip()


def split_union(s):
    out, depth, cur = [], 0, ""
    for ch in s:
        if ch in "<([":
            depth += 1
        elif ch in ">)]":
            depth -= 1
        if ch == "|" and depth == 0:
            out.append(cur)
            cur = ""
        else:
            cur += ch
    if cur:
        out.append(cur)
    return out


def type_name(n):
    n = (n or "").strip()
    if not n:
        return ""
    if n in TYPE_MAP:
        return TYPE_MAP[n]
    if n in PRIMITIVES:
        return PRIMITIVES[n]
    if n.startswith("Array.<") and n.endswith(">"):
        inner = [type_name(p) for p in split_union(n[6:-1])]
        inner = [i for i in inner if i]
        if not inner:
            return "Array<Object>"
        return "Array<%s>" % inner[0]
    if n.startswith("Array<") and n.endswith(">"):
        return type_name("Array.<%s>" % n[6:-1])
    if n.endswith(("Value", "ValueType")):
        return "String"
    if "CallbackFunction" in n or n.endswith("Function"):
        return "Function"
    # A class that the binding does not declare: keep it opaque.
    return "Object"


def parse_members(page):
    """Yield the members of a class-reference page."""
    blocks = page.split('<div class="hc-member">')[1:]
    for block in blocks:
        m = re.search(r'<h4[^>]*id="([^"]*)"[^>]*>(.*?)</h4>', block, re.S)
        if not m:
            continue
        name = clean_name(m.group(1))
        sig = text(m.group(2))
        sig = clean_name(sig) if "(" not in sig else sig
        if not name or name == "constructor":
            continue

        desc_m = re.search(r'<div class="description">(.*?)</div>', block, re.S)
        desc = text(desc_m.group(1)) if desc_m else ""

        is_method = "(" in sig

        if is_method:
            # Types come from the parameters table, order and optionality from
            # the signature, e.g. "addAxis(options [, isX] [, redraw])".
            types = [text(t) for t in re.findall(
                r'<td class="type">(.*?)</td>', block, re.S)]
            params = parse_params(sig, types)
            ret = ""
            ret_m = re.search(r'<h5>Returns:</h5>(.*?)(?:<h5>|<div class="hc-member">|$)',
                              block, re.S)
            if ret_m:
                pt = re.search(r'<span class="param-type">(.*?)</span>', ret_m.group(1), re.S)
                if pt:
                    ret = type_name(text(pt.group(1)))
            yield {
                "name": name,
                "title": name,
                "parent": None,  # filled in by the caller
                "type": "method",
                "returnType": ret,
                "params": params,
                "description": desc,
            }
        else:
            # "axes :Array.<Highcharts.Axis>"
            typ = ""
            if " :" in sig:
                typ = type_name(sig.split(" :", 1)[1])
            yield {
                "name": name,
                "title": name,
                "parent": None,
                "type": "property",
                "returnType": typ or "Object",
                "params": "",
                "description": desc,
            }


def parse_params(sig, types):
    start = sig.index("(")
    end = sig.rindex(")") if ")" in sig[start:] else len(sig)
    body = sig[start + 1:end].strip()
    if not body:
        return ""

    # Handles both jsdoc optional forms: "options [, isX]" and "[renderTo]".
    parts = []
    cur = ""
    optional = False
    for ch in body:
        if ch == "[":
            if cur.strip():
                parts.append((cur.strip(), optional))
            cur = ""
            optional = True
        elif ch == "]":
            if cur.strip():
                parts.append((cur.strip(), optional))
            cur = ""
            optional = False
        elif ch == ",":
            if cur.strip():
                parts.append((cur.strip(), optional))
            cur = ""
        else:
            cur += ch
    if cur.strip():
        parts.append((cur.strip(), optional))

    # WIG expands a trailing run of optional parameters into overloads that
    # include a parameterless one, so two same-named all-optional methods end up
    # with identical .NET signatures. Promote the first parameter to required.
    if parts and all(opt for _, opt in parts):
        parts[0] = (parts[0][0], False)

    out = []
    for idx, (pname, opt) in enumerate(parts):
        ptype = type_name(types[idx]) if idx < len(types) else "Object"
        out.append(("[%s %s]" if opt else "%s %s") % (ptype, pname))
    return "(%s)" % ", ".join(out)


def fetch(page, cache):
    os.makedirs(cache, exist_ok=True)
    path = os.path.join(cache, page.replace(".", "_") + ".html")
    if os.path.exists(path):
        with open(path, encoding="utf-8") as f:
            return f.read()
    url = BASE + page
    print("fetching %s" % url)
    req = urllib.request.Request(url, headers={"User-Agent": "Mozilla/5.0"})
    with urllib.request.urlopen(req) as r:
        body = r.read().decode("utf-8")
    with open(path, "w", encoding="utf-8") as f:
        f.write(body)
    return body


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--cache", default="/tmp/highcharts-classes")
    ap.add_argument("--out", default=os.path.join(os.path.dirname(__file__), "..", "..", ".temp"))
    args = ap.parse_args()

    # All three products expose the same class reference; the docs pages cover
    # the union of the options, so one dump per product is enough (the binding
    # assemblies differ only in their option types).
    entries = []
    for page, cls in CLASSES.items():
        members = list(parse_members(fetch(page, args.cache)))
        if page == "Highcharts":
            # The namespace page also documents types (AlignValue, ...); only
            # the lowercase functions and properties are part of the runtime API.
            members = [m for m in members if m["title"][:1].islower()]
        else:
            # A member named after its class is the documented constructor; the
            # binding defines constructors explicitly where it wants them.
            members = [m for m in members if m["title"] != cls]
        entries.append({
            "name": cls,
            "title": cls,
            "parent": None,
            "type": None,
            "returnType": "",
            "params": "",
            "description": "",
        })
        for m in members:
            m["parent"] = cls
            m["name"] = "%s--%s" % (cls, m["name"])
            entries.append(m)
        print("%-24s %d members" % (cls, len(members)))

    seen = set()
    unique = []
    for e in entries:
        key = (e["name"], e["params"], e["returnType"], e["type"])
        if key in seen:
            continue
        seen.add(key)
        unique.append(e)
    entries = unique

    os.makedirs(args.out, exist_ok=True)
    for prefix in ("hc", "hs", "hm"):
        path = os.path.abspath(os.path.join(args.out, "%sobjects.json" % prefix))
        with open(path, "w", encoding="utf-8") as f:
            json.dump(entries, f, indent=1, ensure_ascii=False)
            f.write("\n")
        print("-> %s (%d entries)" % (path, len(entries)))


if __name__ == "__main__":
    main()
