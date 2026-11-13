// $begin{copyright}
//
// This file is part of WebSharper
//
// Copyright (c) 2008-2026 IntelliFactory
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

// `|=>` is the WIG way to associate a type with a declaration; it is marked
// obsolete in the interface generator, but there is no replacement for it.
#nowarn "44"

open WebSharper
open WebSharper.InterfaceGenerator

open HcJson

type HcLib =
    | Highcharts
    | Highstock
    | Highmaps

let getAssembly lib (configs: HcConfig list) (objects : HcObject list) =

    /// Highcharts ships a single npm package with three UMD entry points plus an
    /// ES module build. The bindings use the ESM build: its modules have to be
    /// importable for their side effect (`Modules.*`), which the UMD files
    /// cannot do inside a bundle.
    let moduleName =
        match lib with
        | Highcharts -> "highcharts/esm/highcharts.js"
        | Highstock -> "highcharts/esm/highstock.js"
        | Highmaps -> "highcharts/esm/highmaps.js"

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
        match typeMap.Value |> Map.tryFind n with
        | Some t -> t
        | None ->
            if warnTypeCreate.Value then printfn "Creating type: %s" n
            let t = (Class n).Type
            typeMap.Value <- typeMap.Value |> Map.add n t
            t

    /// Split on `sep`, ignoring separators nested inside `<>`, `()` or `[]`.
    let splitTop (sep: char) (s: string) =
        let parts = ResizeArray<string>()
        let sb = System.Text.StringBuilder()
        let mutable depth = 0
        for ch in s do
            match ch with
            | '<' | '(' | '[' -> depth <- depth + 1; sb.Append ch |> ignore
            | '>' | ')' | ']' -> depth <- depth - 1; sb.Append ch |> ignore
            | c when c = sep && depth = 0 -> parts.Add(sb.ToString()); sb.Clear() |> ignore
            | c -> sb.Append c |> ignore
        parts.Add(sb.ToString())
        List.ofSeq parts

    let rec getType (n: string) =
        match n with
        | null | "" -> T<unit>
        | _ ->
        let n = n.Trim().TrimEnd(';')
        match splitTop '|' n with
        | [ single ] ->
            if single.StartsWith "Array<" && single.EndsWith ">" then
                getType (single.Substring(6, single.Length - 7)) |> Type.ArrayOf
            else
                getRawType single
        | parts -> parts |> List.map getType |> List.reduce (+)

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

    // ------------------------------------------------------------------
    // npm modules
    //
    // Most series types (and some option groups) live in Highcharts modules
    // that have to be imported for their side effect before a chart uses them.
    // The docs name them per option node; `Modules.<Name>()` below wraps each
    // one as an npm subpath import.
    // ------------------------------------------------------------------
    let requiredModules =
        let all = System.Collections.Generic.HashSet<string>()
        let rec walk (c: HcConfig) =
            c.Requires |> List.iter (all.Add >> ignore)
            c.Members |> List.iter walk
        configs |> List.iter walk
        List.ofSeq all |> List.sort

    let pascalCase (s: string) =
        s.Split('-', '_') |> Array.map capitalize |> String.concat ""

    /// `highcharts/esm/modules/export-data.js` -> `ExportData`.
    let moduleBaseName (spec: string) =
        spec.Split('/') |> Array.last |> fun s -> s.Replace(".js", "") |> pascalCase

    /// Module specifier -> the name of its `Modules` member.
    let moduleMembers =
        let used = System.Collections.Generic.Dictionary<string, int>()
        requiredModules
        |> List.map (fun spec ->
            let baseName = moduleBaseName spec
            let name =
                match used.TryGetValue baseName with
                | true, n ->
                    used.[baseName] <- n + 1
                    baseName + string (n + 1)
                | _ ->
                    used.[baseName] <- 1
                    baseName
            spec, name)

    let moduleMemberOf (spec: string) =
        moduleMembers |> List.pick (fun (s, n) -> if s = spec then Some n else None)

    /// Notes that a config needs a module, and how to load it.
    let requiresNote (requires: string list) =
        match requires with
        | [] -> None
        | mods ->
            Some (
                "\n\nRequires " + String.concat ", " mods + ": call " +
                (mods |> List.map (fun m -> "`Modules." + moduleMemberOf m + "()`") |> String.concat " and ") +
                " before creating the chart."
            )


    let arrayConfigs = ref Set.empty
   
    let isArray (s: string) = s <> null && s.Contains "Array<"

    let rec addArrayConfigs (c: HcConfig) =
        if c.Members |> List.isEmpty |> not then
            if isArray c.Type then
                arrayConfigs.Value <- arrayConfigs.Value |> Set.add c.RefName
            c.Members |> List.iter addArrayConfigs

    configs |> List.iter addArrayConfigs

    // Class name and node for every config, so that `Extends` targets can be
    // resolved regardless of the order in which the dumps are walked.
    let cfgNodes = System.Collections.Generic.Dictionary<string, HcConfig>()
    let cfgNames = System.Collections.Generic.Dictionary<string, string>()

    let seriesTypeOf (c: HcConfig) =
        if c.Name.StartsWith "series<" then
            Some c.Name.[7 .. c.Name.Length - 2]
        else None

    let rec plan parentName (c: HcConfig) =
        let name =
            match seriesTypeOf c with
            | Some s -> capitalize s + "Series"
            | _ -> parentName + capitalize c.Name
        if not (cfgNodes.ContainsKey c.RefName) then
            cfgNodes.[c.RefName] <- c
            cfgNames.[c.RefName] <- name
            c.Members |> List.iter (plan name)

    configs |> List.iter (plan "")

    // Members of each generated config, so that a config with `Extends` can
    // repeat its base's members. WIG inheritance would be the obvious choice,
    // but it drops the .NET constructor of the derived proxy class, which the
    // config builders need.
    let cfgProps = System.Collections.Generic.Dictionary<string, (string * Type.Type) list>()
    let cfgClasses = System.Collections.Generic.Dictionary<string, CodeModel.Class>()
    let inProgress = System.Collections.Generic.HashSet<string>()

    let rec getConfigProps (c: HcConfig) : (string * Type.Type) list =
        match cfgProps.TryGetValue c.RefName with
        | true, props -> props
        | _ when inProgress.Contains c.RefName -> []
        | _ ->
            inProgress.Add c.RefName |> ignore
            let name = cfgNames.[c.RefName]
            let pmem, cmem =
                c.Members |> Seq.distinctBy (fun m -> m.Name) |> List.ofSeq
                |> List.partition (fun cc -> cc.Members = [] && cc.Extends = None)
            let inherited =
                match c.Extends with
                | Some t when cfgNodes.ContainsKey t -> getConfigProps cfgNodes.[t]
                | _ -> []
            let props =
                inherited @
                (pmem |> List.map (fun cc -> cc.Name, getType cc.Type)) @
                (cmem |> List.map (fun cc ->
                    let cls = getConfig cc
                    let ty =
                        if isArray cc.Type || (cc.Extends |> Option.exists (fun t -> arrayConfigs.Value |> Set.contains t)) then
                            Type.ArrayOf cls
                        else
                            cls.Type
                    cc.Name, ty
                ))
                |> List.distinctBy fst
            inProgress.Remove c.RefName |> ignore
            cfgProps.[c.RefName] <- props
            props

    and getConfig (c: HcConfig) : CodeModel.Class =
        match cfgClasses.TryGetValue c.RefName with
        | true, cls -> cls
        | _ ->
            let name = cfgNames.[c.RefName]
            let props = getConfigProps c
            let cls =
                Class (name + "Cfg")
                |=> getRawType c.RefName
                |+> Instance (props |> List.map (fun (n, t) -> n =@ t :> _))
                |+> Static [
                    Constructor T<unit>
                    |> WithInline (match seriesTypeOf c with Some s -> sprintf "{type: '%s'}" s | _ -> "{}")
                ]
                |> WithOptComment (
                    match c.Desc, requiresNote c.Requires with
                    | Some d, Some r -> Some (d + r)
                    | d, Some r -> Some (r.TrimStart())
                    | d, None -> d
                )
            cfgClasses.[c.RefName] <- cls
            configsList.Value <- upcast cls :: configsList.Value
            cls

    /// Loads the Highcharts modules the option model needs. Highcharts modules
    /// patch the core library, so call these before creating a chart.
    let modulesCls =
        Class "Modules"
        |+> Static (
            moduleMembers |> List.map (fun (spec, memberName) ->
                memberName => T<unit> ^-> T<unit>
                |> WithInline "$importFile"
                |> ImportDefault spec
                |> WithComment (
                    sprintf "Imports `%s`, which registers the options that need it." spec
                ) :> CodeModel.IClassMember
            )
        )

    configsList.Value <- upcast modulesCls :: configsList.Value

    let optConfigs, hcConfigs =
        configs |> List.partition (fun c -> c.RefName = "global" || c.RefName = "lang")

    let configCls =
        Class (
            match lib with 
            | Highcharts -> "HighchartsCfg" 
            | Highstock -> "HighstockCfg" 
            | Highmaps -> "HighmapsCfg"
        ) 
        |+> Instance (
            hcConfigs |> List.map (fun c -> 
                let cls = getConfig c
                if isArray c.Type then
                    c.Name =@ Type.ArrayOf cls :> _
                else
                    c.Name =@ cls :> _
            )
        )
        |+> Static [ Constructor T<unit> |> WithInline "{}" ] 

    configsList.Value <- upcast configCls :: configsList.Value

    let optionsCls =
        Class "OptionsCfg"
        |+> Instance (
            optConfigs |> List.map (fun c -> 
                let cls = getConfig c
                c.Name =@ cls :> _
            )
        )
        |+> Static [ Constructor T<unit> |> WithInline "{}" ] 

    configsList.Value <- upcast optionsCls :: configsList.Value

    warnTypeCreate.Value <- true

    let getClass (o: HcObject) =
        let members =
            o.Members |> List.map (
                function
                | HcProperty p ->
                    (p.Name =@ getType p.Type |> WithOptComment p.Desc) :> CodeModel.Member
                | HcMethod m ->
                    let meth = m.Name => getParams m.Params ^-> getType m.ReturnType
                    if m.Name = "chart" || m.Name = "stockChart" then meth |> WithSourceName m.Name else meth
                    :> CodeModel.Member
            )
        let cls = 
            let baseClass =
                Class o.Name
                |=> getRawType o.Name
            let cls =
                match o.Name with
                | "Highcharts" -> baseClass |> ImportDefault moduleName
                | _ -> baseClass
            cls
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
                                | Highcharts -> "$import.chart($container[0], $config)"
                                | Highstock -> "$import.stockChart($container[0], $config)"
                                | Highmaps -> "$import.mapChart($container[0], $config)"
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
        Assembly [
            Namespace "WebSharper.Highcharts" (
                configsList.Value @
                (objects |> Seq.map getClass |> Seq.cast |> List.ofSeq)
            ) 
        ]
    | Highstock -> 
        Assembly [
            Namespace "WebSharper.Highstock" (
                configsList.Value @
                (objects |> Seq.map getClass |> Seq.cast |> List.ofSeq)
            ) 
        ]
    | Highmaps ->
        Assembly [
            Namespace "WebSharper.Highmaps" (
                configsList.Value @
                (objects |> Seq.map getClass |> Seq.cast |> List.ofSeq)
            ) 
        ]
