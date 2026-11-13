#!/usr/bin/env python3
"""Regenerate the `.temp/*configs.json` option dumps from the Highcharts docs tree.

The WebSharper bindings are generated at compile time from the flat JSON dumps in
`.temp/` (see `HighchartsGeneratorCommon/HcJson.fs`). Those dumps used to be
scraped once and then frozen (Highcharts 5.x). This script rebuilds them from the
API documentation tree published for the current release:

    https://api.highcharts.com/highcharts/tree.json

The tree describes the option namespaces of all products at once and annotates
every node with `doclet.products`, so one fetch is enough to produce the
Highcharts, Highstock and Highmaps dumps.

`doclet.extends` is translated into the dump's `extending` field, which
`Definition.getConfig` turns into WIG inheritance (`XxxCfg : YyyCfg`), so a
series type only lists its *own* options and inherits the rest. The legacy dumps
instead duplicated every inherited option into every series type; `--flatten`
reproduces that (much larger) old behaviour.

Usage:
    python3 tools/api-gen/gen_configs.py [--tree tree.json] [--out .temp]

Without `--tree` the script downloads the tree itself.
"""

import argparse
import json
import os
import urllib.request

DEFAULT_TREE_URL = "https://api.highcharts.com/highcharts/tree.json"

# product key in the docs tree -> dump prefix (and binding name, for logging)
PRODUCTS = {
    "highcharts": ("Highcharts", "hc"),
    "highstock": ("Highstock", "hs"),
    "highmaps": ("Highmaps", "hm"),
}

# ---------------------------------------------------------------------------
# Type mapping: docs tree vocabulary -> vocabulary understood by
# HighchartsGeneratorCommon.Definition.getType.
# ---------------------------------------------------------------------------
SIMPLE_TYPES = {
    "number": "Number",
    "string": "String",
    "boolean": "Boolean",
    "function": "Function",
    "*": "Object",
    "object": "Object",
    "Object": "Object",
    "Mixed": "Mixed",
    "Highcharts.ColorType": "Color",
    "Highcharts.ColorString": "Color",
    "Highcharts.Color": "Color",
    "Highcharts.GradientColorObject": "Color",
    "Highcharts.CSSObject": "CSSObject",
    "Highcharts.SVGAttributes": "CSSObject",
    "Highcharts.HTMLDOMElement": "HTMLElement",
    "Highcharts.SVGElement": "HTMLElement",
}

SKIP_TYPES = {"null", "undefined"}

# `extends` targets inherited by more than this many configs are shared through
# the `extending` field instead of being inlined into each inheriting config.
INLINE_MAX = 8


def split_union(s):
    """Split a type string on top-level `|` (ignoring `|` inside brackets)."""
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


def norm_type(name):
    """Map a single documentation type name onto a WIG-friendly type name."""
    name = (name or "").strip()
    if not name or name in SKIP_TYPES:
        return None
    # Quoted literal union members ("y", "circle", ...) are plain strings.
    if name[0] in "\"'":
        return "String"
    if name in SIMPLE_TYPES:
        return SIMPLE_TYPES[name]
    if name.startswith("Array.<") and name.endswith(">"):
        parts = [norm_type(p) for p in split_union(name[len("Array.<"):-1])]
        parts = [p for p in parts if p]
        return "Array<%s>" % ("|".join(dict.fromkeys(parts)) or "Object")
    if "(" in name or ")" in name:
        # Tuples / function signatures: no sensible .NET shape, keep it opaque.
        return "Object"
    if ".<" in name:
        # Generic instantiations: Partial.<X>, Dictionary.<*>, CallbackFunction.<X>.
        return "Function" if "Function" in name else "Object"
    if name.startswith("Highcharts."):
        if "CallbackFunction" in name or name.endswith("Function"):
            return "Function"
        if name.endswith(("Value", "ValueType", "Type")):
            return "String"
        if "Color" in name:
            return "Color"
        return "Object"
    # Unknown plain name: keep it opaque rather than inventing a class per name.
    return "Object"


# Wider types that win over the rest of a union.
UNION_PRECEDENCE = ("Function", "Mixed", "Object")


def norm_type_list(names):
    """Map a documentation type-union onto a single WIG type.

    WIG's union types (`Number|String`) do not survive the round trip between
    the generated .NET proxy and the JS metadata, so unions are collapsed onto
    the most permissive member instead. `Array<...>` keeps its element union.
    """
    parts = list(dict.fromkeys(p for p in (norm_type(n) for n in (names or [])) if p))
    if not parts:
        return ""
    if len(parts) == 1:
        return parts[0]
    # An option that accepts "a single object or an array" is exposed as an
    # array, matching how the docs describe it.
    arrays = [p for p in parts if p.startswith("Array<")]
    if arrays:
        return arrays[0]
    for wide in UNION_PRECEDENCE:
        if wide in parts:
            return wide
    return parts[0]


# ---------------------------------------------------------------------------
# Tree helpers
# ---------------------------------------------------------------------------
def doclet(node):
    return (node or {}).get("doclet") or {}


def children(node):
    return (node or {}).get("children") or {}


def type_names(node):
    typ = doclet(node).get("type")
    if isinstance(typ, dict):
        return typ.get("names") or []
    if isinstance(typ, list):
        return typ
    return []


def own_products(node):
    p = doclet(node).get("products")
    return set(p) if isinstance(p, list) and p else None


def covers(node, inherited, wanted):
    """True when this node (or any descendant) is documented for `wanted`."""
    mine = own_products(node) or inherited
    if wanted in mine:
        return True
    return any(covers(c, mine, wanted) for c in children(node).values())


def extends_of(node):
    ex = doclet(node).get("extends")
    if not ex:
        return []
    return [p.strip() for p in str(ex).split(",") if p.strip()]


# The docs name the modules of a product slightly differently from the npm
# package layout: indicators and stock-only modules live at the package root.
MODULE_ALIASES = (
    ("stock/indicators/", "indicators/"),
    ("stock/modules/", "modules/"),
)


def npm_modules(node):
    """The npm subpath imports a config needs, e.g. `highcharts/esm/modules/sankey`.

    The bindings use the ES module build (`esm/`): its modules can be imported
    for their side effect from within a bundle, which the UMD files cannot.
    Unbound products (Gantt) are left out.
    """
    out = []
    for req in doclet(node).get("requires") or []:
        if not req.startswith("module:"):
            continue
        spec = req[len("module:"):]
        if spec.startswith("highcharts-gantt"):
            continue
        for old, new in MODULE_ALIASES:
            if spec.startswith(old):
                spec = new + spec[len(old):]
                break
        full = "highcharts/esm/%s.js" % spec
        if full not in out:
            out.append(full)
    return out


# ---------------------------------------------------------------------------
# Inheritance flattening (only used with --flatten)
# ---------------------------------------------------------------------------
class Flattener:
    def __init__(self, tree):
        self.index = {}
        for name, node in tree.items():
            if name == "_meta":
                continue
            key = (node.get("meta") or {}).get("fullname") or name
            self.index[key] = node
            self._index(node, key)

    def _index(self, node, path):
        for name, child in children(node).items():
            key = (child.get("meta") or {}).get("fullname") or (path + "." + name)
            self.index.setdefault(key, child)
            self._index(child, key)

    def resolve(self, node, stack=()):
        """Return the effective {child name: node} map, inheritance included."""
        if node is None:
            return {}
        key = (node.get("meta") or {}).get("fullname") or id(node)
        if key in stack:
            return dict(children(node))  # docs inheritance cycle: stop
        merged = {}
        for target in extends_of(node):
            base = self.index.get(target)
            if base is not None:
                merged.update(self.resolve(base, stack + (key,)))
        for name in doclet(node).get("exclude") or []:
            merged.pop(name, None)
        for name, child in children(node).items():
            merged[name] = (self.merge(merged[name], child, stack + (key,))
                            if name in merged else child)
        return merged

    def merge(self, base, own, stack):
        out = dict(own)
        out["doclet"] = doclet(own) or doclet(base)
        kids = dict(self.resolve(base, stack))
        for name, child in children(own).items():
            kids[name] = self.merge(kids[name], child, stack) if name in kids else child
        out["children"] = kids
        return out


# ---------------------------------------------------------------------------
# Dump emission
# ---------------------------------------------------------------------------
class Builder:
    def __init__(self, tree, wanted, flat=None):
        self.tree = tree
        self.wanted = wanted
        self.flat = flat
        self.all_products = set(PRODUCTS)
        self.entries = []
        self.inliner = Flattener(tree)
        self._size = {}
        self.extend_users = {}
        for name, node in tree.items():
            if name != "_meta":
                self._count_extends(node)

    def row(self, refname, title, fullname, parent, node, kids):
        return {
            "name": refname,
            "title": title,
            "fullname": fullname,
            "parent": parent,
            "isParent": bool(kids),
            "returnType": norm_type_list(type_names(node)),
            "description": doclet(node).get("description") or "",
            "deprecated": bool(doclet(node).get("deprecated")),
            "since": doclet(node).get("since") or "",
            "values": "",
            "extending": "",
            "products": sorted(own_products(node) or []),
            "requires": npm_modules(node),
        }

    def inlines(self, node):
        """Whether `extends` is expanded here instead of shared via `extending`.

        Series/plotOptions option sets are always shared: a series type reuses
        the plotOptions config classes. Other inherited option sets are small
        (yAxis extending xAxis, say) and are inlined, so that `YAxisCfg.Title`
        stays a `YAxisTitleCfg`. Large ones (the navigation bindings' annotation
        options, inherited by 40+ bindings) would duplicate the whole subtree, so
        those are shared through `extending` instead.
        """
        if self.flat:
            return True
        shared = [p for p in extends_of(node)
                  if not p.startswith("plotOptions") and p != "series"]
        if not shared:
            return False
        # Inline only option sets that few configs inherit; ones shared by many
        # (the navigation bindings' annotation options) stay shared so the dump
        # does not grow by a full copy per inheriting config.
        return max(self.extend_users.get(p, 1) for p in shared) <= INLINE_MAX

    def _count_extends(self, node):
        for part in extends_of(node):
            self.extend_users[part] = self.extend_users.get(part, 0) + 1
        for child in children(node).values():
            self._count_extends(child)

    def resolved_size(self, node):
        """Number of entries a fully flattened node would produce."""
        key = id(node)
        if key in self._size:
            return self._size[key]
        self._size[key] = 1  # cycle guard
        total = 1 + sum(self.resolved_size(c)
                        for c in self.inliner.resolve(node).values())
        self._size[key] = total
        return total

    def effective(self, node):
        if self.flat or self.inlines(node):
            return self.inliner.resolve(node)
        return children(node)

    def emit(self, refname, title, fullname, parent, node, inherited, kids):
        if doclet(node).get("deprecated"):
            # HcJson drops deprecated configs anyway; skipping the whole subtree
            # also avoids dangling `extending` references.
            return
        self.entries.append(self.row(refname, title, fullname, parent, node, kids))
        mine = own_products(node) or inherited
        for kid, kidnode in kids.items():
            if not covers(kidnode, mine, self.wanted):
                continue
            self.emit(refname + "-" + kid, kid, fullname + "." + kid, refname,
                      kidnode, mine, self.effective(kidnode))

    def series_base(self):
        """The common series element options (`plotOptions.series` + `series`)."""
        po = None
        plot_options_node = None
        series_node = None
        for top, topnode in self.tree.items():
            if top == "plotOptions":
                plot_options_node = topnode
                po = (topnode.get("children") or {}).get("series")
            elif top == "series":
                series_node = topnode
        kids = dict(self.effective(po))
        # `series` itself documents the plain array-element options (id, name,
        # type, xAxis, data, ...) next to the per-type namespaces.
        plot_options = set(children(plot_options_node))
        for kid, kidnode in children(series_node).items():
            if kid in plot_options:
                continue
            kids[kid] = kidnode
        # `data` is documented per series type nowadays, but the aggregate
        # element still accepts it (each point is an array, tuple or object).
        kids.setdefault("data", {
            "meta": {"name": "data", "fullname": "series.data"},
            "doclet": {
                "description": "The series data, as an array of values, tuples or objects.",
                "type": {"names": ["Array.<*>"]},
            },
        })
        return kids

    def build(self):
        for name, node in self.tree.items():
            if name == "_meta" or not covers(node, self.all_products, self.wanted):
                continue
            if name != "series":
                self.emit(name, name, name, None, node, self.all_products,
                          self.effective(node))
                continue

            # `series` is an aggregate array element plus one top-level entry per
            # series type: the `series<type>` convention that Definition's
            # getConfig turns into `<Type>SeriesCfg`.
            base_kids = self.series_base()
            self.entries.append(self.row("series", "series", "series", None,
                                         node, base_kids))
            # Give the aggregate element class the same common options.
            for sub, subnode in base_kids.items():
                if covers(subnode, self.all_products, self.wanted):
                    self.emit("series-" + sub, sub, "series." + sub, "series",
                              subnode, self.all_products, self.effective(subnode))
            # `series` also carries the plain element options (id, name, xAxis,
            # ...) next to the type namespaces, so only keep the types that also
            # appear under plotOptions.
            plot_options = {}
            for top, topnode in self.tree.items():
                if top == "plotOptions":
                    plot_options = children(topnode)
            for kid, kidnode in children(node).items():
                if kid not in plot_options:
                    continue
                if not covers(kidnode, self.all_products, self.wanted):
                    continue
                ref = "series<%s>" % kid
                if self.flat:
                    kids = dict(base_kids)
                    po = self.flat.index.get("plotOptions." + kid)
                    if po is not None:
                        kids.update(self.flat.resolve(po))
                    kids.update(children(kidnode))
                else:
                    kids = children(kidnode)
                self.entries.append(self.row(ref, ref, ref, None, kidnode, kids))
                for sub, subnode in kids.items():
                    if not covers(subnode, self.all_products, self.wanted):
                        continue
                    self.emit(ref + "-" + sub, sub, ref + "." + sub, ref, subnode,
                              self.all_products, self.effective(subnode))
        return self.finish()

    def finish(self):
        # `extending` names another entry's RefName so that Definition's
        # `Inherits (getRawType extending)` lands on the generated Cfg class.
        refs = {e["name"]: e for e in self.entries}

        def as_ref(dotted):
            if dotted.startswith("series."):
                return "series<%s>" % dotted[len("series."):].split(".")[0]
            return dotted.replace(".", "-")

        for e in self.entries:
            target = ""
            node = self.node_of(e)
            # Configs whose `extends` was inlined in `effective` must not also
            # declare `extending`, or the members would be added twice.
            inlined = not self.flat and self.inlines(node)
            if not inlined:
                for part in extends_of(node):
                    # A bare `series` target means "the array element type"; for
                    # `series<type>` entries Definition already inherits `series`.
                    if part == "series":
                        continue
                    cand = as_ref(part)
                    if cand in refs and cand != e["name"]:
                        target = cand
                        break
            if not target and e["isParent"] and e["name"].startswith("series<") \
                    and "-" not in e["name"]:
                # Series types inherit the matching plotOptions entry, which in
                # turn inherits plotOptions.series.
                kind = e["name"][len("series<"):-1]
                for cand in ("plotOptions-" + kind, "plotOptions-series"):
                    if cand in refs:
                        target = cand
                        break
            e["extending"] = target

        self.drop_cycles(refs)
        return self.entries

    def node_of(self, row):
        return self.nodes.get(row["name"])

    def drop_cycles(self, refs):
        for row in self.entries:
            seen = []
            cur = row
            while cur and cur.get("extending"):
                if cur["extending"] in seen or cur["extending"] == row["name"]:
                    cur["extending"] = ""
                    break
                seen.append(cur["extending"])
                cur = refs.get(cur["extending"])


def build(tree, wanted, flat=None):
    b = Builder(tree, wanted, flat)
    b.nodes = {}

    # Track the docs node behind each row so `extends` can be resolved later.
    orig_row = b.row

    def row(refname, title, fullname, parent, node, kids):
        b.nodes[refname] = node
        return orig_row(refname, title, fullname, parent, node, kids)

    b.row = row
    return b.build()


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--tree", help="path to a local tree.json (skips download)")
    ap.add_argument("--out", default=os.path.join(os.path.dirname(__file__), "..", "..", ".temp"))
    ap.add_argument("--url", default=DEFAULT_TREE_URL)
    ap.add_argument("--flatten", action="store_true",
                    help="expand `extends` into every config (legacy behaviour, huge)")
    args = ap.parse_args()

    if args.tree:
        with open(args.tree, encoding="utf-8") as f:
            tree = json.load(f)
    else:
        print("fetching %s" % args.url)
        with urllib.request.urlopen(args.url) as r:
            tree = json.load(r)

    meta = tree.get("_meta") or {}
    print("docs version: %s (%s)" % (meta.get("version"), meta.get("date")))

    flat = Flattener(tree) if args.flatten else None
    os.makedirs(args.out, exist_ok=True)
    for product, (binding, prefix) in PRODUCTS.items():
        entries = build(tree, product, flat)
        path = os.path.abspath(os.path.join(args.out, "%sconfigs.json" % prefix))
        with open(path, "w", encoding="utf-8") as f:
            json.dump(entries, f, indent=1, ensure_ascii=False)
            f.write("\n")
        print("%-10s -> %s (%d entries)" % (binding, path, len(entries)))


if __name__ == "__main__":
    main()
