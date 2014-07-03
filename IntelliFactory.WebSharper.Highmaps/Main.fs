module Main

open System.IO

open IntelliFactory.WebSharper.InterfaceGenerator

let ( +/ ) a b = Path.Combine(a, b)

let Assembly =
    let configs =
        File.ReadAllText(__SOURCE_DIRECTORY__ +/ "../.temp/hmconfigs.json")  
        |> Json.parse |> HcJson.getConfigs
    let objects =  
        File.ReadAllText(__SOURCE_DIRECTORY__ +/ "../.temp/hmobjects.json")  
        |> Json.parse |> HcJson.getObjects
    try 
        Definition.getAssembly configs objects
    with exc ->
        printfn "%A" exc
        reraise()    

[<Sealed>]
type HighmapsExtension() =
    interface IExtension with
        member ext.Assembly = Assembly

[<assembly: Extension(typeof<HighmapsExtension>)>]
do ()
