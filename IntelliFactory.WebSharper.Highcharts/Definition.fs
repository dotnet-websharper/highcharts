module Definition

open IntelliFactory.WebSharper.InterfaceGenerator

open HcJson

let getAssembly (configs: HcConfig list) (objects : HcObject list) =
    
    /// Key: Type name as in documentation
    /// Value: Type.Type records
    let typeMap =
        ref <| Map [
            "Object"    , T<obj>
            "(Object"   , T<obj>
            "String"    , T<string>
            "Number"    , T<float>
            "Boolean"   , T<bool>
            "Array"     , T<obj[]>
            "Function"  , T<unit -> unit>
            "Color"     , T<string>
            "Colo"      , T<string>
            "CSSObject" , T<obj>
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
            getRawType (n.[6 ..].TrimEnd('>')) |> Type.ArrayOf
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
                    printfn "Inherits: %s -> %s" t c.RefName
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

    let configCls =
        Class "HighchartsCfg"
        |+> Protocol (
            configs |> List.map (fun c -> 
                let cls = getConfig "" c
                if isArray c.Type then
                    c.Name =@ Type.ArrayOf cls :> CodeModel.Member
                else
                    c.Name =@ cls :> CodeModel.Member
            )
        )
        |+> [ Constructor T<unit> |> WithInline "{}" ] 

    configsList := upcast configCls :: !configsList

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
                if o.Name = "Highcharts" 
                then
                    "create" => 
                        T<IntelliFactory.WebSharper.JQuery.JQuery>?container 
                        * configCls?config 
                        ^-> getRawType "Chart" 
                        |> WithInline "$container.highcharts($config)" :> _
                    :: (members |> Seq.cast |> List.ofSeq)
                else Protocol members
            )  
            |> WithOptComment o.Desc
        cls

    Assembly [
        Namespace "IntelliFactory.WebSharper.Highcharts" (
            !configsList @
            (objects |> Seq.map getClass |> Seq.cast |> List.ofSeq)
        ) 
        Namespace "IntelliFactory.WebSharper.Highcharts.Resources" [
            (Resource "Highcharts" "highcharts.js").AssemblyWide()
        ]
    ]