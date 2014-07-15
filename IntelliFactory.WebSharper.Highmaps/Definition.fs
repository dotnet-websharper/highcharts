module Definition

open IntelliFactory.WebSharper.InterfaceGenerator

open HcJson

let getAssembly (configs: HcConfig list) (objects : HcObject list) =
    
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
            "Function"           , T<IntelliFactory.WebSharper.EcmaScript.Function>
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
            |+> Protocol (
                    (
                        pmem |> List.map (fun cc ->
                            cc.Name =@ getType cc.Type |> WithOptComment cc.Desc :> CodeModel.Member
                        )
                    ) @ (
                        cmem |> List.map (fun cc ->
                            let cls = getConfig name cc
                            if isArray cc.Type || (cc.Extends |> Option.exists (fun t -> !arrayConfigs |> Set.contains t)) then
                                cc.Name =@ Type.ArrayOf cls :> CodeModel.Member
                            else
                                cc.Name =@ cls :> CodeModel.Member
                        )
                    )
                )
            |+> [ Constructor T<unit> |> WithInline "{}" ] 
            |> WithOptComment c.Desc    
        configsList := upcast cls :: !configsList
        cls

    let optConfigs, hcConfigs =
        configs |> List.partition (fun c -> c.RefName = "global" || c.RefName = "lang")

    let configCls =
        Class "HighmapsCfg"
        |+> Protocol (
            hcConfigs |> List.map (fun c -> 
                let cls = getConfig "" c
                if isArray c.Type then
                    c.Name =@ Type.ArrayOf cls :> CodeModel.Member
                else
                    c.Name =@ cls :> CodeModel.Member
            )
        )
        |+> [ Constructor T<unit> |> WithInline "{}" ] 

    configsList := upcast configCls :: !configsList

    let optionsCls =
        Class "OptionsCfg"
        |+> Protocol (
            optConfigs |> List.map (fun c -> 
                let cls = getConfig "" c
                c.Name =@ cls :> CodeModel.Member
            )
        )
        |+> [ Constructor T<unit> |> WithInline "{}" ] 

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
                            |> WithInline "$container.highcharts('Map', $config)" :> CodeModel.Member
                        "setOptions" => optionsCls?options ^-> optionsCls :> _
                    ]
                    @ (members |> List.filter (fun m -> m.Name <> "setOptions"))
                    |> Seq.cast |> List.ofSeq
                | "Renderer" -> members |> Seq.cast |> List.ofSeq
                | _ -> Protocol members
            )  
            |> WithOptComment o.Desc
        cls

    Assembly [
        Namespace "IntelliFactory.WebSharper.Highmaps" (
            !configsList @
            (objects |> Seq.map getClass |> Seq.cast |> List.ofSeq)
        ) 
        Namespace "IntelliFactory.WebSharper.Highmaps.Resources" [
            Resource "Highmaps" "highmaps.js"
            Resource "MapModule" "map.js"
        ]
    ]