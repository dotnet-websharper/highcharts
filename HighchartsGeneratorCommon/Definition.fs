// $begin{copyright}
//
// This file is part of WebSharper
//
// Copyright (c) 2008-2018 IntelliFactory
//
// Licensed under the Apache License, Version 2.0 (the "License"); you
// may not use this file except in compliance with the License.  You may
// obtain a copy of the License at
//
//     http://www.apache.org/licenses/LICENSE-2.0
//
// Unless required by applicable law or agreed to in writing, software
// distributed under the License is distributed on an "AS IS" BASIS,
// WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or
// implied.  See the License for the specific language governing
// permissions and limitations under the License.
//
// $end{copyright}
module HighchartsGeneratorCommon.Definition

open System.Collections.Generic

open WebSharper
open WebSharper.InterfaceGenerator

open HcJson

type HighmapsParams =
    {
        HighchartsRes : Type.Type
        HighstockRes : Type.Type
    }

type HcLib =
    | Highcharts
    | Highstock
    | Highmaps of HighmapsParams

let capitalize (s: string) = s.[0 .. 0].ToUpper() + s.[1 ..]

let classMap = Dictionary<string, CodeModel.Class>()
let extraTypes = ResizeArray<CodeModel.Class>()

let getClass name =
    match classMap.TryGetValue(name) with
    | true, c -> c
    | _ ->
        let c = Class name
        classMap[name] <- c
        c

let product = "highcharts"

let WithOptComment c e =
    match c with
    | None -> e
    | Some s -> e |> WithComment s

let knownTypes =
    dict [
        "boolean", T<bool>
        "number", T<float>
        "string", T<string>
        "function", T<JavaScript.Function>
    ]

let rec getSingleType (typ: string) =
    match knownTypes.TryGetValue(typ) with
    | true, t -> t
    | _ ->
        if typ.StartsWith("Array.<") && typ.EndsWith(">") then
            let innerType = typ.[7 .. typ.Length - 2]
            Type.ArrayOf (getSingleType innerType)
        else
            T<obj> // Fallback to obj for unknown types

let getType name (types: string list) =
    let isNull, notNull = types |> List.partition (fun t -> t = "undefined" || t = "null")
    let enumValues, rest = notNull |> List.partition (fun t -> t.StartsWith("\"")) 
    let e =         
        match enumValues with 
        | [] -> None
        | _ -> 
            let transformEnumValueName n =
                match n with
                | ">" -> "GreaterThan"
                | "<" -> "LessThan"
                | ">=" -> "GreaterThanOrEqual"
                | "<=" -> "LessThanOrEqual"
                | "==" -> "Equal"
                | "===" -> "StrictEqual"
                | "!=" -> "NotEqual"
                | "!==" -> "StrictNotEqual"
                | _ -> n
            let e = Pattern.EnumStrings (name + "Value") (enumValues |> List.map (fun t -> t.Trim('"') |> transformEnumValueName) |> List.distinct)
            extraTypes.Add e
            Some e.Type
    let t =
        match rest with
        | [] -> 
            match e with 
            | Some e -> e 
            | _ -> T<unit>
        | [ t ] -> 
            match e with
            | Some e -> e + getSingleType t
            | None -> getSingleType t
        | _ -> 
            (e |> Option.toList) @ (notNull |> List.map getSingleType) |> List.reduce (+)
    if isNull.Length > 0 then
        !? t
    else
        t

let isInProduct product (info: CfgInfo) =
    info.Products |> List.isEmpty || info.Products |> List.exists (fun p -> p = product)

let rec getConfig product parentName (info: CfgInfo) =
    let n = info.Name
    let name, seriesType =
        if parentName = "Series" then 
            capitalize n + "Series", Some n
        else
            parentName + capitalize n, None
    let cls = 
        getClass (name + "Cfg")
        |+> Static [ Constructor T<unit> |> WithInline (match seriesType with | Some s -> sprintf "{type: '%s'}" s | _ -> "{}") ] 
        |> WithOptComment info.Description
    match info.Extends with
    | Some extends ->
        let baseName = extends.Split('.') |> Array.map capitalize |> String.concat ""
        cls |=> Inherits (getClass (baseName + "Cfg")) |> ignore
    | _ ->
        match seriesType with
        | Some s ->
            cls |=> Inherits (getClass "SeriesCfg") |> ignore
        | _ ->
            ()
    for cc in info.Children do
        if cc |> isInProduct product then
            
            if List.isEmpty cc.Children && Option.isNone cc.Extends then
                cls |+> Instance [
                    cc.Name =@ getType (name + capitalize cc.Name) cc.Types |> WithOptComment cc.Description
                ] |> ignore
            else
                let isArray = cc.Types |> List.contains "Array.<*>"
                cls |+> Instance [
                    cc.Name =@ (getConfig product name cc |> if isArray then Type.ArrayOf else id) |> WithOptComment cc.Description
                ] |> ignore
    cls.Type

let getConfigs product =    

    let optConfigs, hcConfigs =
        tree |> List.partition (fun c -> c.Name = "global" || c.Name = "lang")

    let configCls = 
        Class (
            capitalize product + "Cfg"
        ) 
        |+> Instance (
            hcConfigs |> List.choose (fun cc -> 
                if cc |> isInProduct product && cc.Name <> "global" && cc.Name <> "lang" then
                    let isArray = cc.Types |> List.contains "Array.<*>"
                    Some (cc.Name =@ (getConfig product "" cc |> if isArray then Type.ArrayOf else id) |> WithOptComment cc.Description :> _)
                else
                    None
            )
        )
        |+> Static [ Constructor T<unit> |> WithInline "{}" ] 

    let optionsCls =
        Class "OptionsCfg"
        |+> Instance (
            optConfigs |> List.map (fun c -> 
                let cls = getConfig product "" c
                c.Name =@ cls :> _
            )
        )
        |+> Static [ Constructor T<unit> |> WithInline "{}" ] 

    let highcharts = 
        Class "Highcharts"
        |> Import "Highcharts" "highcharts"
        |+> Static [
            "chart" => !?(T<string> + T<WebSharper.JavaScript.Dom.Element>)?container * configCls?config * !?(T<WebSharper.JavaScript.Function>)?callback ^-> T<unit> //getClass "Chart" 
            "setOptions" => optionsCls?options ^-> optionsCls
        ]

    classMap.Values |> Seq.append extraTypes |> Seq.append [ configCls; optionsCls; highcharts ]

let getAssembly lib =

    match lib with
    | Highcharts ->
        let hcRes =
            Resource "Highcharts" "https://code.highcharts.com/highcharts.js"
    
        Assembly [
            Namespace "WebSharper.Highcharts" (
                getConfigs "highcharts" |> Seq.cast |> List.ofSeq
            ) 
            //Namespace "WebSharper.Highcharts.Resources" [
            //    hcRes

            //    Resource "ExportingModule" "https://code.highcharts.com/modules/exporting.js" 
            //    |> Requires [ hcRes ]
            
            //    Resource "MooToolsAdapter" "https://code.highcharts.com/adapters/mootools-adapter.js" 
            //    |> Requires [ hcRes ]

            //    Resource "PrototypeAdapter" "https://code.highcharts.com/adapters/prototype-adapter.js" 
            //    |> Requires [ hcRes ]
            //]
        ]
    | Highstock -> 
        let hsRes =
            Resource "Highstock" "https://code.highcharts.com/stock/highstock.js"

        Assembly [
            Namespace "WebSharper.Highstock" (
                getConfigs "highstock" |> Seq.cast |> List.ofSeq
            ) 
            //Namespace "WebSharper.Highstock.Resources" [
            //    hsRes

            //    Resource "ExportingModule" "https://code.highcharts.com/stock/modules/exporting.js" 
            //    |> Requires [ hsRes ]
            
            //    Resource "MooToolsAdapter" "https://code.highcharts.com/stock/adapters/mootools-adapter.js" 
            //    |> Requires [ hsRes ]

            //    Resource "PrototypeAdapter" "https://code.highcharts.com/stock/adapters/prototype-adapter.js" 
            //    |> Requires [ hsRes ]
            //]
        ]
    | Highmaps p ->
    let hmRes =
        Resource "Highmaps" "https://code.highcharts.com/maps/highmaps.js"

    Assembly [
        Namespace "WebSharper.Highmaps" (
            getConfigs "highmaps" |> Seq.cast |> List.ofSeq
        ) 
        //Namespace "WebSharper.Highmaps.Resources" [
        //    hmRes

        //    Resource "MapModuleForCharts" "https://code.highcharts.com/maps/modules/map.js" 
        //    |> RequiresExternal [ p.HighchartsRes ]
            
        //    Resource "MapModuleForStock" "https://code.highcharts.com/maps/modules/map.js" 
        //    |> RequiresExternal [ p.HighstockRes ]

        //    Resource "ExportingModule" "https://code.highcharts.com/maps/modules/exporting.js" 
        //    |> Requires [ hmRes ]
            
        //    Resource "MooToolsAdapter" "https://code.highcharts.com/maps/adapters/mootools-adapter.js" 
        //    |> Requires [ hmRes ]

        //    Resource "PrototypeAdapter" "https://code.highcharts.com/maps/adapters/prototype-adapter.js" 
        //    |> Requires [ hmRes ]
        //]
    ]
