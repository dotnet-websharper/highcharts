#load "tools/includes.fsx"
open IntelliFactory.Build

open System.IO
let ( +/ ) a b = Path.Combine(a, b)

let bt = 
    let bt = BuildTool()
    bt.WithFramework(bt.Framework.Net40)

let tempDir = __SOURCE_DIRECTORY__ +/ ".temp"

Directory.CreateDirectory tempDir |> ignore

do  use cl = new System.Net.WebClient()
    cl.DownloadFile(
        "http://api.highcharts.com/highcharts/option/dump.json", 
        tempDir +/ "hcconfigs.json"
    )
    cl.DownloadFile(
        "http://api.highcharts.com/highcharts/object/dump.json", 
        tempDir +/ "hcobjects.json"
    )
    cl.DownloadFile(
        "http://code.highcharts.com/highcharts.js",
        __SOURCE_DIRECTORY__ +/ "IntelliFactory.WebSharper.Highcharts/highcharts.js"
    )
    cl.DownloadFile(
        "http://api.highcharts.com/highstock/option/dump.json", 
        tempDir +/ "hsconfigs.json"
    )
    cl.DownloadFile(
        "http://api.highcharts.com/highstock/object/dump.json", 
        tempDir +/ "hsobjects.json"
    )
    cl.DownloadFile(
        "http://code.highcharts.com/stock/highstock.js",
        __SOURCE_DIRECTORY__ +/ "IntelliFactory.WebSharper.Highstock/highstock.js"
    )
    cl.DownloadFile(
        "http://api.highcharts.com/highmaps/option/dump.json", 
        tempDir +/ "hmconfigs.json"
    )
    cl.DownloadFile(
        "http://api.highcharts.com/highmaps/object/dump.json", 
        tempDir +/ "hmobjects.json"
    )
    cl.DownloadFile(
        "http://code.highcharts.com/maps/highmaps.js",
        __SOURCE_DIRECTORY__ +/ "IntelliFactory.WebSharper.Highmaps/highmaps.js"
    )
    cl.DownloadFile(
        "http://code.highcharts.com/maps/modules/map.js",
        __SOURCE_DIRECTORY__ +/ "IntelliFactory.WebSharper.Highmaps/map.js"
    )

let hc =
    bt.WebSharper.Extension("IntelliFactory.WebSharper.Highcharts")
        .SourcesFromProject()
        .Embed(["highcharts.js"])
        .References(fun r -> [r.NuGet("FParsec").Reference()])

let hs =
    bt.WebSharper.Extension("IntelliFactory.WebSharper.Highstock")
        .SourcesFromProject()
        .Embed(["highstock.js"])
        .References(fun r -> [r.NuGet("FParsec").Reference()])

let hm =
    bt.WebSharper.Extension("IntelliFactory.WebSharper.Highmaps")
        .SourcesFromProject()
        .Embed(["highmaps.js"; "map.js"])
        .References(fun r -> [r.NuGet("FParsec").Reference()])

bt.Solution [
    hc
    hs
    hm

    bt.PackageId("WebSharper.Highcharts", "2.5").NuGet.CreatePackage()
        .Description("WebSharper bindings to Highcharts")
        .Add(hc)
        .Configure(fun c ->
            { c with
                Authors = ["IntelliFactory"]
                Id = "WebSharper.Highcharts"
                Title = Some ("WebSharper.Highcharts")
                NuGetReferences =
                    c.NuGetReferences |> List.filter (fun dep -> 
                        dep.PackageId.Contains "FParsec" |> not
                    )
            })
    bt.PackageId("WebSharper.Highstock", "2.5").NuGet.CreatePackage()
        .Description("WebSharper bindings to Highstock")
        .Add(hs)
        .Configure(fun c ->
            { c with
                Authors = ["IntelliFactory"]
                Id = "WebSharper.Highstock"
                Title = Some ("WebSharper.Highstock")
                NuGetReferences =
                    c.NuGetReferences |> List.filter (fun dep -> 
                        dep.PackageId.Contains "FParsec" |> not
                    )
            })
    bt.PackageId("WebSharper.Highmaps", "2.5").NuGet.CreatePackage()
        .Description("WebSharper bindings to Highmaps")
        .Add(hm)
        .Configure(fun c ->
            { c with
                Authors = ["IntelliFactory"]
                Id = "WebSharper.Highmaps"
                Title = Some ("WebSharper.Highmaps")
                NuGetReferences =
                    c.NuGetReferences |> List.filter (fun dep -> 
                        dep.PackageId.Contains "FParsec" |> not
                    )
            })
]
|> bt.Dispatch
