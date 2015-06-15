module HighchartsGeneratorCommon.HcJson

type HcConfig =
    {
        Name    : string
        RefName : string
        Type    : string
        Members : HcConfig list   
        Desc    : string option
        Values  : string list
        Extends : string option
    }

type HcProperty =
    {
        Name : string
        Type : string
        Desc : string option
    }

type HcParam =
    {
        Name     : string
        Type     : string 
        Optional : bool
    }

type HcMethod =
    {
        Name       : string
        ReturnType : string
        Params     : HcParam list
        Desc       : string option
    }

type HcMember =
    | HcProperty of HcProperty
    | HcMethod of HcMethod

type HcObject =
    {
        Name    : string
        Desc    : string option
        Members : HcMember list
    }

open Json

let getString j    = match j with JString s -> s | JNull -> null | o -> failwithf "not a string: %A" o
let getStringOpt j    = match j with JString "" | JNull -> None | JString s -> Some s | o -> failwithf "not a string: %A" o
let getList j      = match j with JList   l -> l | o -> failwithf "not a list: %A" o
let getObject j    = match j with JObject s -> s | o -> failwithf "not an object: %A" o
    
let hasTrue p j = j |> Map.tryFind p = Some (JBool true)

module Map =
    let find key table =
        try Map.find key table
        with _ ->
            failwithf "Key not found:\nItem: %A\nMap:%A" key table
   
let trimAndSplit (tr: string) (s: string) =
    s.TrimStart(tr.[0]).TrimEnd(tr.[1]).Split(tr.[2])

let getValues o =
    match o |> Map.tryFind "values" with
    | None -> []
    | Some values ->
    match values |> getString with
    | null | "" -> []
    | s ->
        s |> trimAndSplit "[]," |> Seq.map (fun i ->
            i.Trim()
        ) |> List.ofSeq

let getReturnType o =
    o |> Map.tryFind "returnType" |> Option.map getString |> function Some t -> t | _ -> null

let getConfig (j: Json) (members: HcConfig list) =
    let jo = j |> getObject
    try
        {
            Name    = jo |> Map.find "title" |> getString 
            RefName = jo |> Map.find "name" |> getString 
            Type    = jo |> getReturnType
            Members = members
            Desc    = jo |> Map.tryFind "description" |> Option.bind getStringOpt
            Values  = jo |> getValues
            Extends = jo |> Map.tryFind "extending" |> Option.bind getStringOpt
        }
    with e -> failwithf "getConfig error on: %A. Error: %A" (jo |> Map.find "fullname" |> getString) e

let getParent c =
    match c |> getObject |> Map.tryFind "parent" with
    | Some p -> getString p
    | _ -> null

let getConfigs (j: Json) =
    let l = j |> getList
    let nestingMap =
        l |> Seq.filter (fun c -> c |> getObject |> hasTrue "deprecated" |> not)
        |> Seq.map (fun c -> c |> getParent, c)
        |> Seq.groupBy fst |> Seq.map (fun (k, vl) -> k, vl |> Seq.map snd |> List.ofSeq)
        |> Map.ofSeq
    let rec tr c = 
        let co = c |> getObject
        match nestingMap |> Map.tryFind (co |> Map.find "name" |> getString) with
        | Some mem -> List.map tr mem
        | _ -> []
        |> getConfig c
    
    nestingMap |> Map.find null |> List.map tr

let getParam (s: string) =
    try
        match s with
        | null | "" | "()" -> []
        | _ ->
        s |> trimAndSplit "()," |> Seq.map (fun p ->
            let p = p.Trim()
            let opt, pa =
                if p.StartsWith("[") then
                    true, p |> trimAndSplit "[] "
                else
                    false, p.Split ' '
            {                              
                Name     = pa.[1]
                Type     = pa.[0]
                Optional = opt
            }                              
        )
        |> List.ofSeq
    with _ ->
        failwithf "getParam error on \"%s\"" s

let getParams o =
    match o |> Map.tryFind "params" with
    | Some ps -> ps |> getString |> getParam
    | None -> []

let getMethod (j: Json) =
    let jo = j |> getObject
    {
        Name       = jo |> Map.find "title" |> getString
        ReturnType = jo |> getReturnType
        Params     = jo |> getParams
        Desc       = jo |> Map.find "description" |> getStringOpt
    }

let getProperty (j: Json) =
    let jo = j |> getObject
    {
        Name = jo |> Map.find "title" |> getString
        Type = jo |> getReturnType
        Desc = jo |> Map.find "description" |> getStringOpt
    } : HcProperty

let getMember (j: Json) =
    let jo = j |> getObject
    match jo |> Map.find "type" |> getString with
    | "method" | "" -> getMethod j |> HcMethod 
    | "property" | "Number" -> getProperty j |> HcProperty
    | "Array<Object>" -> { getProperty j with Type = "Array<Object>" } |> HcProperty
    | "Object" -> { getProperty j with Type = "Object" } |> HcProperty
    | t -> failwithf "getMember error: type not found: %s" t

let getObjects (j: Json) =
    let l = j |> getList
    let memberMap =
        l |> Seq.map (fun c -> c |> getParent, c)
        |> Seq.groupBy fst |> Seq.map (fun (k, vl) -> k, vl |> Seq.map snd |> List.ofSeq)
        |> Map.ofSeq
    memberMap |> Map.find null |> List.choose (fun o ->
        let oo = o |> getObject
        let name = oo |> Map.find "title" |> getString
        if name = null then None else
        Some {
            Name    = name
            Desc    = oo |> Map.find "description" |> getStringOpt
            Members = memberMap |> Map.find name |> List.map getMember
        }
    )