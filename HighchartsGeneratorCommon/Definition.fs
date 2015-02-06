﻿module HighchartsGeneratorCommon.Definition

open IntelliFactory.WebSharper
open IntelliFactory.WebSharper.InterfaceGenerator

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

let getAssembly lib (configs: HcConfig list) (objects : HcObject list) =
    
    /// Key: Type name as in documentation
    /// Value: Type.Type records
    let typeMap =
        ref <| Map [
            "Object"             , T<obj>
            "(Object"            , T<obj>
            "object"             , T<obj>
            "String"             , T<string>
            "Number"             , T<float>
            "Boolean"            , T<bool>
            "Function"           , T<JavaScript.Function>
            "Color"              , T<string>
            "Colo"               , T<string>
            "CSSObject"          , T<obj>
            "Array&lt;Mixed&gt;" , T<obj[]>
            "null"               , T<unit>
            "undefined"          , T<unit>
        ]

    let warnTypeCreate = ref false

    let getRawType (n: string) = 
        match !typeMap |> Map.tryFind n with
        | Some t -> t
        | None ->
            if !warnTypeCreate then printfn "Creating type: %s" n
            let t = Type.New()
            typeMap := !typeMap |> Map.add n t
            t

    let getType (n: string) =
        match n with
        | null | "" -> T<unit>
        | _ ->
        if n.StartsWith "Array<" then
            getRawType (n.[6 ..].TrimEnd(';', '>')) |> Type.ArrayOf
        else
            let ts = n.Split '|'
            ts |> Array.map getRawType |> Array.reduce (+)

    let getParams (pl: HcParam list) =
        {
            This  = None
            Arguments =
                pl |> List.map (fun p ->
                    {
                        Name     = Some p.Name
                        Optional = p.Optional
                        Type     = getType p.Type
                    } : Type.Parameter
                )
            Variable = None
        } : Type.Parameters

    let WithOptComment c cls =
        match c with
        | None -> cls
        | Some s -> cls |> WithComment s

    let configsList = ref ([] : CodeModel.NamespaceEntity list)

    let capitalize (s: string) = s.[0 .. 0].ToUpper() + s.[1 ..]

    let arrayConfigs = ref Set.empty
   
    let isArray (s: string) = s <> null && s.StartsWith "Array<"

    let rec addArrayConfigs (c: HcConfig) =
        if c.Members |> List.isEmpty |> not then
            if isArray c.Type then
                arrayConfigs := !arrayConfigs |> Set.add c.RefName
            c.Members |> List.iter addArrayConfigs

    configs |> List.iter addArrayConfigs

    let rec getConfig parentName (c: HcConfig) =
        let name = parentName + capitalize c.Name
        let pmem, cmem = c.Members |> List.partition (fun cc -> cc.Members = [] && cc.Extends = None)
        let cls =
            Class (name + "Cfg")
            |=> getRawType c.RefName
            |> fun cls ->
                match c.Extends with                  
                | Some t ->
                    cls |=> Inherits (getRawType t)
                | _ -> cls
            |+> Instance (
                    (
                        pmem |> List.map (fun cc ->
                            cc.Name =@ getType cc.Type |> WithOptComment cc.Desc :> _
                        )
                    ) @ (
                        cmem |> List.map (fun cc ->
                            let cls = getConfig name cc
                            if isArray cc.Type || (cc.Extends |> Option.exists (fun t -> !arrayConfigs |> Set.contains t)) then
                                cc.Name =@ Type.ArrayOf cls :> _
                            else
                                cc.Name =@ cls :> _
                        )
                    )
                )
            |+> Static [ Constructor T<unit> |> WithInline "{}" ] 
            |> WithOptComment c.Desc    
        configsList := upcast cls :: !configsList
        cls

    let optConfigs, hcConfigs =
        configs |> List.partition (fun c -> c.RefName = "global" || c.RefName = "lang")

    let configCls =
        Class (
            match lib with 
            | Highcharts -> "HighchartsCfg" 
            | Highstock -> "HighstockCfg" 
            | Highmaps _ -> "HighmapsCfg"
        ) 
        |+> Instance (
            hcConfigs |> List.map (fun c -> 
                let cls = getConfig "" c
                if isArray c.Type then
                    c.Name =@ Type.ArrayOf cls :> _
                else
                    c.Name =@ cls :> _
            )
        )
        |+> Static [ Constructor T<unit> |> WithInline "{}" ] 

    configsList := upcast configCls :: !configsList

    let optionsCls =
        Class "OptionsCfg"
        |+> Instance (
            optConfigs |> List.map (fun c -> 
                let cls = getConfig "" c
                c.Name =@ cls :> _
            )
        )
        |+> Static [ Constructor T<unit> |> WithInline "{}" ] 

    configsList := upcast optionsCls :: !configsList

    warnTypeCreate := true

    let getClass (o: HcObject) =
        let members =
            o.Members |> List.map (
                function
                | HcProperty p ->
                    p.Name =@ getType p.Type |> WithOptComment p.Desc :> CodeModel.Member  
                | HcMethod m ->
                    m.Name => getParams m.Params ^-> getType m.ReturnType :> CodeModel.Member
            )
        let cls = 
            Class o.Name
            |=> getRawType o.Name
            |+> (
                match o.Name with
                | "Highcharts" -> 
                    [
                        "create" => 
                            T<IntelliFactory.WebSharper.JQuery.JQuery>?container 
                            * configCls?config 
                            ^-> getRawType "Chart" 
                            |> WithInline (
                                match lib with
                                | Highcharts -> "$container.highcharts($config)" 
                                | Highstock -> "$container.highcharts('StockChart', $config)" 
                                | Highmaps _ -> "$container.highcharts('Map', $config)"
                            ) :> CodeModel.Member
                        "setOptions" => optionsCls?options ^-> optionsCls :> CodeModel.Member
                    ]
                    @ (members |> List.filter (fun m -> m.Name <> "setOptions"))
                    |> Seq.cast |> List.ofSeq |> Static
                | "Renderer" -> members |> Seq.cast |> List.ofSeq |> Static
                | _ -> members |> Seq.cast |> List.ofSeq |> Instance
            )  
            |> WithOptComment o.Desc
        cls

    match lib with
    | Highcharts ->
        let hcRes =
            Resource "Highcharts" "http://code.highcharts.com/highcharts.js"
            |> RequiresExternal [ T<IntelliFactory.WebSharper.JQuery.Resources.JQuery> ]
    
        Assembly [
            Namespace "IntelliFactory.WebSharper.Highcharts" (
                !configsList @
                (objects |> Seq.map getClass |> Seq.cast |> List.ofSeq)
            ) 
            Namespace "IntelliFactory.WebSharper.Highcharts.Resources" [
                hcRes

                Resource "ExportingModule" "http://code.highcharts.com/modules/exporting.js" 
                |> Requires [ hcRes ]
            
                Resource "MooToolsAdapter" "http://code.highcharts.com/adapters/mootools-adapter.js" 
                |> Requires [ hcRes ]

                Resource "PrototypeAdapter" "http://code.highcharts.com/adapters/prototype-adapter.js" 
                |> Requires [ hcRes ]
            ]
        ]
    | Highstock -> 
        let hsRes =
            Resource "Highstock" "http://code.highcharts.com/stock/highstock.js"
            |> RequiresExternal [ T<IntelliFactory.WebSharper.JQuery.Resources.JQuery> ]

        Assembly [
            Namespace "IntelliFactory.WebSharper.Highstock" (
                !configsList @
                (objects |> Seq.map getClass |> Seq.cast |> List.ofSeq)
            ) 
            Namespace "IntelliFactory.WebSharper.Highstock.Resources" [
                hsRes

                Resource "ExportingModule" "http://code.highcharts.com/stock/modules/exporting.js" 
                |> Requires [ hsRes ]
            
                Resource "MooToolsAdapter" "http://code.highcharts.com/stock/adapters/mootools-adapter.js" 
                |> Requires [ hsRes ]

                Resource "PrototypeAdapter" "http://code.highcharts.com/stock/adapters/prototype-adapter.js" 
                |> Requires [ hsRes ]
            ]
        ]
    | Highmaps p ->
    let hmRes =
        Resource "Highmaps" "http://code.highcharts.com/maps/highmaps.js"
        |> RequiresExternal [ T<IntelliFactory.WebSharper.JQuery.Resources.JQuery> ]

    Assembly [
        Namespace "IntelliFactory.WebSharper.Highmaps" (
            !configsList @
            (objects |> Seq.map getClass |> Seq.cast |> List.ofSeq)
        ) 
        Namespace "IntelliFactory.WebSharper.Highmaps.Resources" [
            hmRes

            Resource "MapModuleForCharts" "http://code.highcharts.com/maps/modules/map.js" 
            |> RequiresExternal [ p.HighchartsRes ]
            
            Resource "MapModuleForStock" "http://code.highcharts.com/maps/modules/map.js" 
            |> RequiresExternal [ p.HighstockRes ]

            Resource "ExportingModule" "http://code.highcharts.com/maps/modules/exporting.js" 
            |> Requires [ hmRes ]
            
            Resource "MooToolsAdapter" "http://code.highcharts.com/maps/adapters/mootools-adapter.js" 
            |> Requires [ hmRes ]

            Resource "PrototypeAdapter" "http://code.highcharts.com/maps/adapters/prototype-adapter.js" 
            |> Requires [ hmRes ]
        ]
    ]