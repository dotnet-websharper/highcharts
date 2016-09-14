module HighchartsGeneratorCommon.Definition

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

let getAssembly lib (configs: HcConfig list) (objects : HcObject list) =
    
    /// Key: Type name as in documentation
    /// Value: Type.Type records
    let typeMap =
        ref <| Map [
            "Object"             , T<obj>
            "(Object"            , T<obj>
            "Mixed"              , T<obj>
            "object"             , T<obj>
            "String"             , T<string>
            "Number"             , T<float>
            "Boolean"            , T<bool>
            "Function"           , T<JavaScript.Function>
            "Color"              , T<string>
            "Colo"               , T<string>
            "CSSObject"          , T<JavaScript.Object<string>>
            "Array"              , T<obj[]>
            "Array&lt;Mixed&gt;" , T<obj[]>
            "Array<Mixed"        , T<obj[]>
            "Array<String>"      , T<string[]>
            "null"               , T<unit>
            "undefined"          , T<unit>
            "HTMLElement"        , T<JavaScript.Dom.Element>
            "Text"               , T<string>
            "#CCC"               , T<string>
            "middle"             , T<string>
            "CSS"                , T<JavaScript.Object<string>>
        ]

    let warnTypeCreate = ref false

    let getRawType (n: string) = 
        match !typeMap |> Map.tryFind n with
        | Some t -> t
        | None ->
            if !warnTypeCreate then printfn "Creating type: %s" n
            let t = (Class n).Type
            typeMap := !typeMap |> Map.add n t
            t

    let rec getType (n: string) =
        match n with
        | null | "" -> T<unit>
        | _ ->
        if n.StartsWith "Array<" then
            getType (n.[6 ..].TrimEnd(';', '>')) |> Type.ArrayOf
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
        let seriesType =
            if c.Name.StartsWith "series<" then
                Some c.Name.[7 .. c.Name.Length - 2]
            else None
        let name = 
            match seriesType with
            | Some s -> capitalize s + "Series" 
            | _ -> parentName + capitalize c.Name
        let pmem, cmem = 
            c.Members |> Seq.distinctBy (fun m -> m.Name) |> List.ofSeq
            |> List.partition (fun cc -> cc.Members = [] && cc.Extends = None)
        let cls =
            Class (name + "Cfg")
            |=> getRawType c.RefName
            |> fun cls ->
                match c.Extends with                  
                | Some t ->
                    cls |=> Inherits (getRawType t)
                | _ -> 
                match seriesType with
                | Some s ->
                    cls |=> Inherits (getRawType "series")
                | _ ->
                    cls
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
            |+> Static [ Constructor T<unit> |> WithInline (match seriesType with | Some s -> sprintf "{type: '%s'}" s | _ -> "{}") ] 
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
                    let meth = m.Name => getParams m.Params ^-> getType m.ReturnType 
                    if m.Name = "chart" || m.Name = "stockChart" then meth |> WithSourceName m.Name else meth
                    :> CodeModel.Member
            )
        let cls = 
            Class o.Name
            |=> getRawType o.Name
            |+> (
                match o.Name with
                | "Highcharts" -> 
                    [
                        "create" => 
                            T<WebSharper.JQuery.JQuery>?container 
                            * configCls?config 
                            ^-> getRawType "Chart" 
                            |> WithInline (
                                match lib with
                                | Highcharts -> "$container.highcharts($config)" 
                                | Highstock -> "$container.highcharts('StockChart', $config)" 
                                | Highmaps _ -> "$container.highcharts('Map', $config)"
                            ) :> CodeModel.Member
                        "setOptions" => optionsCls?options ^-> optionsCls :> _
                        "renderer" =? getRawType "Renderer" :> _
                    ]
                    @ (members |> List.filter (fun m -> m.Name <> "setOptions"))
                    |> Seq.cast |> List.ofSeq |> Static
                | "Renderer" -> 
                    Constructor (T<JavaScript.Dom.Element>?parentNode * T<int>?width * T<int>?height) :> CodeModel.Member
                    :: members |> Seq.cast |> List.ofSeq |> Instance
                | _ -> members |> Seq.cast |> List.ofSeq |> Instance
            )  
            |> WithOptComment o.Desc
        cls

    match lib with
    | Highcharts ->
        let hcRes =
            Resource "Highcharts" "http://code.highcharts.com/highcharts.js"
            |> RequiresExternal [ T<WebSharper.JQuery.Resources.JQuery> ]
    
        Assembly [
            Namespace "WebSharper.Highcharts" (
                !configsList @
                (objects |> Seq.map getClass |> Seq.cast |> List.ofSeq)
            ) 
            Namespace "WebSharper.Highcharts.Resources" [
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
            |> RequiresExternal [ T<WebSharper.JQuery.Resources.JQuery> ]

        Assembly [
            Namespace "WebSharper.Highstock" (
                !configsList @
                (objects |> Seq.map getClass |> Seq.cast |> List.ofSeq)
            ) 
            Namespace "WebSharper.Highstock.Resources" [
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
        |> RequiresExternal [ T<WebSharper.JQuery.Resources.JQuery> ]

    Assembly [
        Namespace "WebSharper.Highmaps" (
            !configsList @
            (objects |> Seq.map getClass |> Seq.cast |> List.ofSeq)
        ) 
        Namespace "WebSharper.Highmaps.Resources" [
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
