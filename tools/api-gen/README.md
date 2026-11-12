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

## Caveat

The generated *option* model is used through object initializers and is fully
covered by the sample. The runtime *class* tier is emitted with the correct .NET
shape, but calling individual members from JavaScript compilation depends on WIG
property/method metadata and does not work for every member. Plain option-based
charts do not need it.
