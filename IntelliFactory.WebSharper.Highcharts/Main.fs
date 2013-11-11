module Main

open System.IO

open IntelliFactory.WebSharper.InterfaceGenerator

let ( +/ ) a b = Path.Combine(a, b)

let Assembly =
    let configs =
        File.ReadAllText(__SOURCE_DIRECTORY__ +/ "../.temp/configs.json")  
        |> Json.parse |> HcJson.getConfigs
    let objects =  
        File.ReadAllText(__SOURCE_DIRECTORY__ +/ "../.temp/objects.json")  
        |> Json.parse |> HcJson.getObjects
    try 
        Definition.getAssembly configs objects
    with exc ->
        printfn "%A" exc
        reraise()    

[<Sealed>]
type HighchartsExtension() =
    interface IExtension with
        member ext.Assembly = Assembly

[<assembly: Extension(typeof<HighchartsExtension>)>]
do ()
