#load "tools/includes.fsx"
open IntelliFactory.Build

open System.IO
let ( +/ ) a b = Path.Combine(a, b)

let bt = 
    let bt = BuildTool().PackageId("WebSharper.Highcharts", "2.5")
    bt.WithFramework(bt.Framework.Net40)

let tempDir = __SOURCE_DIRECTORY__ +/ ".temp"

Directory.CreateDirectory tempDir |> ignore

do  use cl = new System.Net.WebClient()
    cl.DownloadFile(
        "http://api.highcharts.com/highcharts/option/dump.json", 
        tempDir +/ "configs.json"
    )
    cl.DownloadFile(
        "http://api.highcharts.com/highcharts/object/dump.json", 
        tempDir +/ "objects.json"
    )
    cl.DownloadFile(
        "http://code.highcharts.com/highcharts.js",
        __SOURCE_DIRECTORY__ +/ "IntelliFactory.WebSharper.Highcharts\highcharts.js"
    )

let main =
    bt.WebSharper.Extension("IntelliFactory.WebSharper.Highcharts")
        .SourcesFromProject()
        .Embed(["highcharts.js"])
        .References(fun r -> [r.NuGet("FParsec").Reference()])

let test =
    bt.WebSharper.HtmlWebsite("IntelliFactory.WebSharper.Highcharts.Tests")
        .SourcesFromProject()
        .References(fun r -> [r.Project main])

bt.Solution [
    main
    test

    bt.NuGet.CreatePackage()
        .Description("WebSharper bindings to Highcharts")
        .Add(main)
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
]
|> bt.Dispatch
