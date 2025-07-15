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

let axis = Class "Axis"
let chart = Class "Chart"
let legend = Class "Legend"
let point = Class "Point"   
let series = Class "Series"

do
    axis
    |+> Instance [
        "categories" =? T<string []>
        "chart" =@ chart
        "coll" =@ T<string>
        "crosshair" =@ (T<bool> + T<obj>)
        "horiz" =@ !? T<bool>
        "isXAxis" =@ !? T<bool>
        "len" =@ T<float>
        "max" =@ !? T<float>
        "min" =@ !? T<float>
        "minorTicks" =@ T<obj>
        "options" =@ T<obj>
        "pos" =@ T<float>
        "reversed" =@ T<bool>
        "series" =@ !| series
        "side" =@ T<float>
        "tickPositions" =@ !? T<float []>
        "ticks" =@ T<obj>
        "userOptions" =@ T<obj>
        "addPlotBand" => T<obj> ^-> !? T<obj>
        "addPlotLine" => T<obj> ^-> !? T<obj>
        "addTitle" => !? T<bool> ^-> T<unit>
        "defaultLabelFormatter" => !? T<obj> ^-> T<string>
        "drawCrosshair" => !? T<obj> * !? point ^-> T<unit>
        "drilldownCategory" => T<float> * !? T<obj> ^-> T<unit>
        "getExtremes" => T<unit> ^-> T<obj>
        "getLinePath" => T<float> ^-> T<obj>
        "getLinearTickPositions" => T<float> * T<float> * T<float> ^-> T<float []>
        "getMinorTickInterval" => T<unit> ^-> (T<float> + T<string> + T<unit>)
        "getMinorTickPositions" => T<unit> ^-> T<float []>
        "getPlotBandPath" => T<float> * T<float> * T<obj> ^-> T<obj>
        "getPlotLinePath" => T<obj> ^-> !? T<obj>
        "getThreshold" => T<float> ^-> T<float>
        "hasData" => T<unit> ^-> T<bool>
        "hideCrosshair" => T<unit> ^-> T<unit>
        "init" => chart * T<obj> ^-> T<unit>
        "remove" => !? T<bool> ^-> T<unit>
        "removePlotBand" => T<string> ^-> T<unit>
        "removePlotLine" => T<string> ^-> T<unit>
        "renderLine" => T<unit> ^-> T<unit>
        "renderMinorTick" => T<float> * T<bool> ^-> T<unit>
        "renderTick" => T<float> * T<float> * T<bool> ^-> T<unit>
        "setCategories" => T<string []> * !? T<bool> ^-> T<unit>
        "setExtremes" => !? (T<float> + T<string>) * !? (T<float> + T<string>) * !? T<bool> * !? (T<bool> + T<obj>) * !? T<obj> ^-> T<unit>
        "setTitle" => T<obj> * !? T<bool> ^-> T<unit>
        "toPixels" => (T<float> + T<string>) * !? T<bool> ^-> T<float>
        "toValue" => T<float> * !? T<bool> ^-> T<float>
        "update" => T<obj> * !? T<bool> ^-> T<unit>
    ] |> ignore

    chart
    |+> Instance [
        "axes" =@ !| axis
        "chartHeight" =@ T<float>
        "chartWidth" =@ T<float>
        "container" =@ T<obj>
        "credits" =@ T<obj>
        "data" =@ !? T<obj>
        "exporting" =@ T<obj>
        "hoverPoint" =@ (point + T<obj>)
        "hoverPoints" =@ (!| point + T<obj>)
        "hoverSeries" =@ (series + T<obj>)
        "index" =? T<float>
        "inverted" =@ !? T<bool>
        "legend" =@ legend
        "numberFormatter" =@ T<obj>
        "options" =@ T<obj>
        "plotHeight" =@ T<float>
        "plotLeft" =@ T<float>
        "plotTop" =@ T<float>
        "plotWidth" =@ T<float>
        "pointer" =@ T<obj>
        "renderer" =@ T<obj>
        "series" =@ !| series
        "sonification" =@ !? T<obj>
        "styledMode" =@ T<bool>
        "subtitle" =@ T<obj>
        "time" =@ T<obj>
        "title" =@ T<obj>
        "tooltip" =@ T<obj>
        "userOptions" =@ T<obj>
        "xAxis" =@ !| axis
        "yAxis" =@ !| axis
        "addAnnotation" => T<obj> * !? T<bool> ^-> T<obj>
        "addAxis" => T<obj> * !? T<bool> * !? T<bool> * !? (T<bool> + T<obj>) ^-> axis
        "addColorAxis" => T<obj> * !? T<bool> * !? (T<bool> + T<obj>) ^-> axis
        "addCredits" => !? T<obj> ^-> T<unit>
        "addSeries" => T<obj> * !? T<bool> * !? (T<bool> + T<obj>) ^-> series
        "addSeriesAsDrilldown" => point * T<obj> ^-> T<unit>
        "destroy" => T<unit> ^-> T<unit>
        "drillUp" => T<unit> ^-> T<unit>
        "fromLatLonToPoint" => T<obj> ^-> T<obj>
        "fromPointToLatLon" => (point + T<obj>) ^-> !? T<obj>
        "get" => T<string> ^-> !? (axis + series + point)
        "getOptions" => T<unit> ^-> T<obj>
        "getSelectedPoints" => T<unit> ^-> !| point
        "getSelectedSeries" => T<unit> ^-> !| series
        "init" => T<obj> * !? T<obj> ^-> T<unit>
        "isInsidePlot" => T<float> * T<float> * !? T<obj> ^-> T<bool>
        "langFormat" => T<string> * T<obj> ^-> T<string>
        "mapZoom" => !? T<float> * !? T<float> * !? T<float> * !? T<float> * !? T<float> ^-> T<unit>
        "redraw" => !? (T<bool> + T<obj>) ^-> T<unit>
        "reflow" => !? T<obj> ^-> T<unit>
        "removeAnnotation" => (T<float> + T<string> + T<obj>) ^-> T<unit>
        "setCaption" => T<obj> ^-> T<unit>
        "setClassName" => !? T<string> ^-> T<unit>
        "setSize" => !? T<float> * !? T<float> * !? (T<bool> + T<obj>) ^-> T<unit>
    ] |> ignore

    legend
    |+> Instance [
        "allItems" =? !|(point + series)
        "box" =? T<obj>
        "chart" =? chart
        "group" =? T<obj>
        "options" =? T<obj>
        "title" =? T<obj>
        "setText" => (point + series) ^-> T<unit>
        "update" => T<obj> * !? T<bool> ^-> T<unit>
    ] |> ignore

    point
    |+> Instance [
        "category" =@ (T<float> + T<string>)
        "colorIndex" =@ !? T<float>
        "dataGroup" =@ !? T<obj>
        "graphic" =@ !? T<obj>
        "graphics" =@ !? (T<obj []>)
        "high" =@ !? T<float>
        "index" =? T<float>
        "key" =@ (T<float> + T<string>)
        "low" =@ !? T<float>
        "name" =@ T<string>
        "options" =@ T<obj>
        "percentage" =@ !? T<float>
        "plotX" =@ !? T<float>
        "plotY" =@ !? T<float>
        "points" =@ !? (!|TSelf)
        "properties" =@ T<obj>
        "selected" =@ T<bool>
        "series" =@ series
        "total" =@ !? T<float>
        "tooltipPoints" =@ !? (!|TSelf)
        "visible" =@ T<bool>
        "x" =@ T<float>
        "y" =@ !? T<float>
        "firePointEvent" => T<string> * !? T<obj> * !? T<obj> ^-> T<unit>
        "haloPath" => T<float> ^-> T<obj>
        "remove" => !? T<bool> * !? (T<bool> + T<obj>) ^-> T<unit>
        "select" => !? T<bool> * !? T<bool> ^-> T<unit>
        "update" => (T<float> + T<string> + T<float []> + T<obj>) * !? T<bool> * !? (T<bool> + T<obj>) ^-> T<unit>
    ] |> ignore

    series 
    |+> Static [
        "registerType" => T<string> * T<obj> ^-> T<unit>
        "types" =? T<obj>
    ]
    |+> Instance [
        "center" =? T<float []>
        "chart" =? chart
        "color" =@ !? T<obj>
        "data" =? !| point
        "dataMax" =? !? T<float>
        "dataMin" =? !? T<float>
        "index" =? T<float>
        "legendItem" =@ !? T<obj>
        "linkedParent" =? TSelf
        "linkedSeries" =? !|TSelf
        "name" =@ T<string>
        "options" =? T<obj>
        "points" =? !| point
        "selected" =? T<bool>
        "type" =? T<string>
        "userOptions" =@ T<obj>
        "visible" =? T<bool>
        "xAxis" =? axis
        "yAxis" =? axis
        "addPoint" => T<obj> * !? T<bool> * !? T<bool> * !? (T<bool> + T<obj>) * !? T<bool> ^-> T<unit>
        "animate" => !? T<bool> ^-> T<unit>
        "drawPoints" => T<unit> ^-> T<unit>
        "getName" => T<unit> ^-> T<string>
        "getPlotBox" => T<unit> ^-> T<obj>
        "getValidPoints" => !? !| point * !? T<bool> * !? T<bool> ^-> !| point
        "groupData" => T<obj> * T<float []> * !? (T<string> + T<obj>) ^-> T<unit>
        "hide" => T<unit> ^-> T<unit>
        "is" => T<string> ^-> T<bool>
        "markerAttribs" => point * !? T<string> ^-> T<obj>
        "onMouseOut" => T<unit> ^-> T<unit>
        "onMouseOver" => T<unit> ^-> T<unit>
        "remove" => !? T<bool> * !? (T<bool> + T<obj>) * !? T<bool> ^-> T<unit>
        "removePoint" => T<float> * !? T<bool> * !? (T<bool> + T<obj>) ^-> T<unit>
        "render" => T<unit> ^-> T<unit>
        "searchPoint" => T<obj> * !? T<bool> ^-> !? point
        "select" => !? T<bool> ^-> T<unit>
        "setData" => T<obj []> * !? T<bool> * !? (T<bool> + T<obj>) * !? T<bool> ^-> T<unit>
        "setState" => !? T<string> * !? T<bool> ^-> T<unit>
        "setVisible" => !? T<bool> * !? T<bool> ^-> T<unit>
        "show" => T<unit> ^-> T<unit>
        "sonify" => !? T<obj> ^-> T<unit>
        "translate" => T<unit> ^-> T<unit>
        "update" => T<obj> * !? T<bool> ^-> T<unit>
    ] |> ignore

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
    match seriesType with
    | Some _ ->
        cls |=> Inherits series |> ignore
    | _ ->
        match info.Extends with
        | Some extends ->
            let baseName = extends.Split('.') |> Array.map capitalize |> String.concat ""
            cls |=> Inherits (getClass (baseName + "Cfg")) |> ignore
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
    if name = "Series" then
        cls |+> Instance [
            "Data" =@ T<obj[]>
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

    classMap.Values 
    |> Seq.append extraTypes 
    |> Seq.append [
        axis
        chart 
        legend
        point 
        series
        configCls
        optionsCls
        highcharts
    ]

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
