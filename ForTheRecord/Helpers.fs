module ForTheRecord.Helpers

open System.Text.Json

/// Convenience function to transform return values from C#-style TryGet...()
/// functions into F# options.
let tryGetOption<'T> =
    function
    | (true, v: 'T) -> Some v
    | false, _ -> None

/// Map a top-level JSON object into a dictionary suitable for use as a Liquid
/// model.
let rec jsonLiquidModel (js: JsonElement) : obj =
    match js.ValueKind with
    | JsonValueKind.Object ->
        let d =
            js.EnumerateObject()
            |> Seq.map (fun prop -> prop.Name, jsonLiquidModel prop.Value)
            |> dict

        d
    | JsonValueKind.Array ->
        let l = js.EnumerateArray() |> Seq.map jsonLiquidModel |> List.ofSeq
        l
    | JsonValueKind.String -> js.GetString()
    | JsonValueKind.Number -> js.GetDecimal()
    | JsonValueKind.True -> true
    | JsonValueKind.False -> false
    | JsonValueKind.Undefined
    | JsonValueKind.Null
    | _ -> null

/// Attempt to read a property from a JSON element, checking that it's actually
/// an object first. This is most useful for the top-level element, which is
/// usually (but need not necessarily be) an object.
let tryJsonProperty (name: string) (js: JsonElement) : JsonElement option =
    match js.ValueKind with
    | JsonValueKind.Object -> js.TryGetProperty name |> tryGetOption
    | _ -> None
