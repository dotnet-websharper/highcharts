module HcJson

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

#if INTERACTIVE
let mutable findErrorKey = null
let mutable findErrorTable = null 

module Map =
    let find key table =
        try Map.find key table
        with _ ->
            findErrorKey <- box key
            findErrorTable <- box table
            failwith "Map.find error: please check findErrorKey and findErrorTable"
#endif
   
let trimAndSplit (tr: string) (s: string) =
    s.TrimStart(tr.[0]).TrimEnd(tr.[1]).Split(tr.[2])

let getValues s =
    match s with
    | null | "" -> []
    | _ ->
        s |> trimAndSplit "[]," |> Seq.map (fun i ->
            i.Trim()
        ) |> List.ofSeq

let getConfig (j: Json) (members: HcConfig list) =
    let jo = j |> getObject
    try
        {
            Name    = jo |> Map.find "title" |> getString 
            RefName = jo |> Map.find "name" |> getString 
            Type    = jo |> Map.find "returnType" |> getString
            Members = members
            Desc    = jo |> Map.find "description" |> getStringOpt
            Values  = jo |> Map.find "values" |> getString |> getValues
            Extends = jo |> Map.find "extending" |> getStringOpt
        }
    with _ -> failwithf "getConfig error on: %A" (jo |> Map.find "values" |> getString)

let getConfigs (j: Json) =
    let l = j |> getObject |> Map.find "Options" |> getObject |> Map.find "options" |> getList
    let nestingMap =
        l |> Seq.filter (fun c -> c |> getObject |> hasTrue "deprecated" |> not)
        |> Seq.map (fun c -> c |> getObject |> Map.find "parent" |> getString, c)
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

let getMethod (j: Json) =
    let jo = j |> getObject
    {
        Name       = jo |> Map.find "title" |> getString
        ReturnType = jo |> Map.find "returnType" |> getString
        Params     = jo |> Map.find "params" |> getString |> getParam
        Desc       = jo |> Map.find "description" |> getStringOpt
    }

let getProperty (j: Json) =
    let jo = j |> getObject
    {
        Name = jo |> Map.find "title" |> getString
        Type = jo |> Map.find "returnType" |> getString
        Desc = jo |> Map.find "description" |> getStringOpt
    } : HcProperty

let getMember (j: Json) =
    let jo = j |> getObject
    match jo |> Map.find "type" |> getString with
    | "method" -> getMethod j |> HcMethod 
    | "property" -> getProperty j |> HcProperty
    | "Array<Object>" -> { getProperty j with Type = "Array<Object>" } |> HcProperty
    | "Object" -> { getProperty j with Type = "Object" } |> HcProperty
    | t -> failwithf "getMember error: type not found: %s" t

let getObjects (j: Json) =
    let l = j |> getObject |> Map.find "HObjects" |> getObject |> Map.find "objects" |> getList
    let memberMap =
        l |> Seq.map (fun c -> c |> getObject |> Map.find "parent" |> getString, c)
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