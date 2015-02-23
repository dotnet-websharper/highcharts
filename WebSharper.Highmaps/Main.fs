module Main

open System.IO

open WebSharper.InterfaceGenerator
open HighchartsGeneratorCommon

let ( +/ ) a b = Path.Combine(a, b)

let Assembly =
    try 
        let configs =
            File.ReadAllText(__SOURCE_DIRECTORY__ +/ "../.temp/hmconfigs.json")  
            |> Json.parse |> HcJson.getConfigs
        let objects =  
            File.ReadAllText(__SOURCE_DIRECTORY__ +/ "../.temp/hmobjects.json")  
            |> Json.parse 
            |> function                                                                 
                | Json.JList l -> 
                    Json.JList(
                        l |> List.filter (
                            function 
                            | Json.JObject o -> o |> Map.containsKey "title"
                            | _ -> false
                        )
                    )
                | _ -> failwith "json not a list" 
            |> HcJson.getObjects
        let def = 
            Definition.Highmaps {
                HighchartsRes = T<WebSharper.Highcharts.Resources.Highcharts>
                HighstockRes = T<WebSharper.Highstock.Resources.Highstock>
            }
        Definition.getAssembly def configs objects
    with exc ->
        printfn "%A" exc
        reraise()    

[<Sealed>]
type HighmapsExtension() =
    interface IExtension with
        member ext.Assembly = Assembly

[<assembly: Extension(typeof<HighmapsExtension>)>]
do ()
