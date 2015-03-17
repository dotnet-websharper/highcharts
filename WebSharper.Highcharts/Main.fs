module Main

open System.IO

open WebSharper.InterfaceGenerator
open HighchartsGeneratorCommon

let ( +/ ) a b = Path.Combine(a, b)

let Assembly =
    try 
        let configs =
            File.ReadAllText(__SOURCE_DIRECTORY__ +/ "../.temp/hcconfigs.json")  
            |> Json.parse |> HcJson.getConfigs
        let objects =  
            File.ReadAllText(__SOURCE_DIRECTORY__ +/ "../.temp/hcobjects.json")  
            |> Json.parse |> HcJson.getObjects
        Definition.getAssembly Definition.Highcharts configs objects
    with exc ->
        printfn "%A" exc
        reraise()    

[<Sealed>]
type HighchartsExtension() =
    interface IExtension with
        member ext.Assembly = Assembly

[<assembly: Extension(typeof<HighchartsExtension>)>]
do ()