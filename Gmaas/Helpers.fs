module Gmaas.Helpers

let overrideHeaders overrides (source: (string * string) seq) =
    let standardize = fun (k: string, _) -> k.Trim().ToLowerInvariant()
    let overrideKeys = overrides |> List.map standardize |> Set.ofList

    seq {
        yield! source |> Seq.filter (standardize >> fun k -> not (overrideKeys.Contains k))
        yield! overrides
    }
