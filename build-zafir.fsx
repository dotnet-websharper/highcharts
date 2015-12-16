#load "tools/includes.fsx"
open IntelliFactory.Build

open System.IO
let ( +/ ) a b = Path.Combine(a, b)

let bt = 
    BuildTool().VersionFrom("Zafir")
        .WithFSharpVersion(FSharpVersion.FSharp30)
        .WithFramework(fun fw -> fw.Net40)

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
        "http://api.highcharts.com/highstock/option/dump.json", 
        tempDir +/ "hsconfigs.json"
    )
    cl.DownloadFile(
        "http://api.highcharts.com/highstock/object/dump.json", 
        tempDir +/ "hsobjects.json"
    )
    cl.DownloadFile(
        "http://api.highcharts.com/highmaps/option/dump.json", 
        tempDir +/ "hmconfigs.json"
    )
    cl.DownloadFile(
        "http://api.highcharts.com/highmaps/object/dump.json", 
        tempDir +/ "hmobjects.json"
    )

let common =
    bt.Zafir.Library("HighchartsGeneratorCommon")
        .SourcesFromProject()
        .References(fun r -> [r.NuGet("FParsec").Reference()])

let hc =
    bt.Zafir.Extension("WebSharper.Highcharts")
        .SourcesFromProject()
        .References(fun r -> [r.Project common; r.NuGet("FParsec").Reference()])

let hs =
    bt.Zafir.Extension("WebSharper.Highstock")
        .SourcesFromProject()
        .References(fun r -> [r.Project common; r.NuGet("FParsec").Reference()])

let hm =
    bt.Zafir.Extension("WebSharper.Highmaps")
        .SourcesFromProject()
        .References(fun r -> [r.Project common; r.NuGet("FParsec").Reference(); r.Project hc; r.Project hs])

bt.Solution [
    common
    hc
    hs
    hm

    bt.PackageId("Zafir.Highcharts").NuGet.CreatePackage()
        .Description("WebSharper bindings to Highcharts")
        .Add(hc)
        .Configure(fun c ->
            { c with
                Authors = ["IntelliFactory"]
                Id = "Zafir.Highcharts"
                Title = Some ("Zafir.Highcharts")
                NuGetReferences =
                    c.NuGetReferences |> List.filter (fun dep -> 
                        dep.PackageId.Contains "FParsec" |> not
                    )
            })
    bt.PackageId("Zafir.Highstock").NuGet.CreatePackage()
        .Description("WebSharper bindings to Highstock")
        .Add(hs)
        .Configure(fun c ->
            { c with
                Authors = ["IntelliFactory"]
                Id = "Zafir.Highstock"
                Title = Some ("Zafir.Highstock")
                NuGetReferences =
                    c.NuGetReferences |> List.filter (fun dep -> 
                        dep.PackageId.Contains "FParsec" |> not
                    )
            })
    bt.PackageId("Zafir.Highmaps").NuGet.CreatePackage()
        .Description("WebSharper bindings to Highmaps")
        .Add(hm)
        .Configure(fun c ->
            { c with
                Authors = ["IntelliFactory"]
                Id = "Zafir.Highmaps"
                Title = Some ("Zafir.Highmaps")
                NuGetReferences =
                    c.NuGetReferences |> List.filter (fun dep -> 
                        dep.PackageId.Contains "FParsec" |> not
                    )
            })
]
|> bt.Dispatch
