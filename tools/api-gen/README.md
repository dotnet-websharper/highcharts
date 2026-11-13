# API model generation

`WebSharper.Highcharts/Main.fs` (and the Highstock/Highmaps equivalents) do not
hand-write the binding surface: they read a flat JSON model from `.temp/` and
build the assembly with `HighchartsGeneratorCommon`.

That model used to be scraped once and then frozen — the committed dumps dated
from 2017 and described Highcharts 5.x. The scripts here rebuild it from the
Highcharts documentation for the release the bindings target (currently 13.x,
matching the pinned `highcharts` npm package).

```
.temp/hcconfigs.json   Highcharts options       <- gen_configs.py
.temp/hsconfigs.json   Highstock options        <- gen_configs.py
.temp/hmconfigs.json   Highmaps options         <- gen_configs.py
.temp/hcobjects.json   Highcharts runtime classes <- gen_objects.py
.temp/hsobjects.json   Highstock runtime classes  <- gen_objects.py
.temp/hmobjects.json   Highmaps runtime classes   <- gen_objects.py
```

## Usage

```bash
python3 tools/api-gen/gen_configs.py        # downloads the docs tree itself
python3 tools/api-gen/gen_configs.py --tree /tmp/tree.json   # offline
python3 tools/api-gen/gen_objects.py --cache /tmp/hc-classes # downloads the class pages
```

Both scripts are dependency-free Python 3 and write into `.temp/`.

After regenerating, **force the bindings to rebuild**: MSBuild has no dependency
on `.temp/`, so an incremental build happily reuses the old generated assembly.

```bash
touch WebSharper.Highcharts/Main.fs WebSharper.Highstock/Main.fs WebSharper.Highmaps/Main.fs
dotnet build WebSharper.Highcharts.sln -c Release
```

## Options (`gen_configs.py`)

Source: `https://api.highcharts.com/highcharts/tree.json` — one tree covering
all products, with per-node `doclet.products`, descriptions, types, `extends`
and `exclude`.

What the script does:

- **Products.** A node is emitted for Highcharts/Highstock/Highmaps when it (or
  one of its descendants) is documented for that product.
- **Series types.** `series.<type>` becomes a top-level `series<type>` entry,
  the convention `Definition.getConfig` turns into `<Type>SeriesCfg`. Only
  nodes that also exist under `plotOptions` are series types — `series` also
  documents the plain array-element options (`id`, `name`, `xAxis`, ...).
- **Inheritance.** `doclet.extends` becomes the dump's `extending` field, which
  `Definition` turns into copied members (a series type lists its own options and
  inherits the rest from `plotOptions.series`). Option sets inherited by many
  configs stay *shared* through `extending`; small ones with few inheritors
  (`yAxis extends xAxis`) are inlined so their nested classes keep their own
  names. `--flatten` expands everything like the legacy dumps did, which is
  enormous (~100k entries).
- **Types.** The docs vocabulary (`number`, `Array.<*>`, `Highcharts.Point`, ...)
  is mapped onto what `Definition.getType` understands (`Number`,
  `Array<Object>`, ...). Unions are collapsed onto the widest member: WIG unions
  do not survive the round trip between the generated .NET proxy and the JS
  metadata.
- **Deprecated options** are dropped together with their subtree.
- **Modules.** `doclet.requires` becomes the dump's `requires` field, rewritten
  to the npm subpath that actually ships the module: the docs say
  `stock/indicators/acceleration-bands`, npm has
  `highcharts/esm/indicators/acceleration-bands.js`. `Definition` turns those
  into the binding's `Modules` class, where each member is an
  `Import` + `$importFile` side-effect import, and appends the "call
  `Modules.X()`" note to the docs of every config that needs it.

## Runtime classes (`gen_objects.py`)

Source: `https://api.highcharts.com/class-reference/<Class>` — one HTML page per
runtime class. The script scrapes members, parameters (including jsdoc optional
markers) and return types for the classes the bindings expose: `Highcharts`,
`Axis`, `Chart`, `Legend`, `Point`, `Series`, `Renderer` (`SVGRenderer`) and
`Element` (`SVGElement`).

Only those classes are fetched because a WIG type has to be declared in the
assembly: any other documented type is emitted as `Object`.

Methods whose documented parameters are all optional get their first parameter
promoted to required: WIG expands trailing optional parameters into overloads
including a parameterless one, so two same-named all-optional methods would
otherwise produce identical .NET signatures.

## Version match

The scripts read `https://api.highcharts.com/...`, which currently serves the
**13.1.0** docs, while the bindings pin `highcharts` **13.1.1** on npm. Diffing
the two npm releases shows the public API is identical: no file was added or
removed, no `.d.ts` changed at all, and the 17 changed files are the bundles
(+13 bytes each: the version string) plus one fix in
`es-modules/Core/Renderer/HTML/AST.js` (`xlink:href` added to the attributes
validated against `allowedReferences`). So the dumps are accurate for 13.1.1;
re-check on the next minor.

## Regenerating while developing

The WebSharper compiler server (`wsfscservice`) caches the WebSharper metadata of
referenced assemblies. After regenerating `.temp/` and rebuilding a binding, a
sample that references it can keep compiling against the *old* metadata — the
symptoms look like missing members on the runtime classes. Kill the server
before rebuilding:

```bash
pkill -f wsfscservice
```
