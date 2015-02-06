module Main

open System.IO

open IntelliFactory.WebSharper.InterfaceGenerator
open HighchartsGeneratorCommon

let ( +/ ) a b = Path.Combine(a, b)

let Assembly =
    try 
        let configs =
            File.ReadAllText(__SOURCE_DIRECTORY__ +/ "../.temp/hsconfigs.json")  
            |> Json.parse |> HcJson.getConfigs
        let objects =  
            File.ReadAllText(__SOURCE_DIRECTORY__ +/ "../.temp/hsobjects.json")  
            |> Json.parse |> HcJson.getObjects
        Definition.getAssembly Definition.Highstock configs objects
    with exc ->
        printfn "%A" exc
        reraise()    

[<Sealed>]
type HighstockExtension() =
    interface IExtension with
        member ext.Assembly = Assembly

[<assembly: Extension(typeof<HighstockExtension>)>]
do ()
