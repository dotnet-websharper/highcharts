module HighchartsGeneratorCommon.Json

open FParsec

type Json =
    | JNull
    | JString of string
    | JNumber of float
    | JBool   of bool
    | JList   of Json list
    | JObject of Map<string, Json>

    override this.ToString() =
        match this with
        | JNull     -> "null"
        | JString s -> "'" + s + "'"
        | JNumber n -> string n
        | JBool   b -> string b
        | JList   l -> "[" + (l |> Seq.map string |> String.concat ", ") + "]"
        | JObject o -> "{" + (o |> Seq.map (fun (KeyValue (k, v)) -> k + ":" + string v) |> String.concat ", ") + "}"

let parse =
    let str s = pstring s
    let ws = spaces
    let listBetweenStrings sOpen sClose pElement f =
        between (str sOpen) (str sClose)
            (ws >>. sepBy (pElement .>> ws) (str "," >>. ws) |>> f)

    let jnull = stringReturn "null" JNull
    let jbool = stringReturn "true"  (JBool true) <|> stringReturn "false" (JBool false)
    let jnumber = pfloat |>> JNumber

    let stringLiteral =
        let escape =
            anyOf "\"\\/bfnrt"
            |>> function
            | 'b' -> "\b"
            | 'f' -> "\u000C"
            | 'n' -> "\n"
            | 'r' -> "\r"
            | 't' -> "\t"
            | c   -> string c

        let unicodeEscape =
            let hex2int c = (int c &&& 15) + (int c >>> 6)*9

            str "u" >>. pipe4 hex hex hex hex (fun h3 h2 h1 h0 ->
                (hex2int h3)*4096 + (hex2int h2)*256 + (hex2int h1)*16 + hex2int h0
                |> char |> string
            )

        let escapedCharSnippet = str "\\" >>. (escape <|> unicodeEscape)
        let normalCharSnippet  = manySatisfy (fun c -> c <> '"' && c <> '\\')

        between (str "\"") (str "\"")
            (stringsSepBy normalCharSnippet escapedCharSnippet)

    let jvalue, jvalueRef = createParserForwardedToRef<Json, unit>()

    let jstring = stringLiteral |>> JString

    let keyValue = stringLiteral .>>. (ws >>. str ":" >>. ws >>. jvalue)
    let jlist   = listBetweenStrings "[" "]" jvalue JList
    let jobject = listBetweenStrings "{" "}" keyValue (Map.ofList >> JObject)

    jvalueRef := choice [ jobject; jlist; jstring; jnumber; jbool; jnull ]

    let json = ws >>. jvalue .>> ws .>> eof

    run json
    >> function Success (res, _, _) -> res | Failure (msg, _, _) -> failwith msg