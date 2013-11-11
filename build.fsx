#load "tools/includes.fsx"
#r "System.IO.Compression.FileSystem"
open IntelliFactory.Build

open System.IO

let ( +/ ) a b = Path.Combine(a, b)

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
        "IntelliFactory.WebSharper.Highcharts/highcharts.js"
    )

#I "packages/WebSharper.2.5.73.1-alpha/tools/net45"

#r "IntelliFactory.WebSharper"
#r "IntelliFactory.WebSharper.JQuery"
#r "IntelliFactory.WebSharper.InterfaceGenerator"


//let configsJson, membersJson =
//    use cl = new System.Net.WebClient()
//    cl.DownloadString "http://api.highcharts.com/highcharts/option/dump.json",
//    cl.DownloadString "http://api.highcharts.com/highcharts/object/dump.json"
//
//#I "packages/FParsec.1.0.1/lib/net40-client"
//#r "FParsec"
//#r "FParsecCS"
//  
//#load "IntelliFactory.WebSharper.Highcharts/json.fs"
//
//let configs = Json.parse configsJson
//let members = Json.parse membersJson

let bt = 
    BuildTool().PackageId("WebSharper.Highcharts", "2.5")
        .References(fun r -> [r.Assembly "System.Web"])

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
