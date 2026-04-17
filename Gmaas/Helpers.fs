module Gmaas.Helpers

open Google.Apis.Gmail.v1

type InternalDateSourceEnum = UsersResource.MessagesResource.ImportRequest.InternalDateSourceEnum

let overrideHeaders overrides (source: (string * string) seq) =
    let standardize = fun (k: string, _) -> k.Trim().ToLowerInvariant()
    let overrideKeys = overrides |> List.map standardize |> Set.ofList

    seq {
        yield! source |> Seq.filter (standardize >> fun k -> not (overrideKeys.Contains k))
        yield! overrides
    }

let parseInternalDateSource (v: string) =
    match v.Trim().ToLowerInvariant() with
    | "receivedtime" -> Ok InternalDateSourceEnum.ReceivedTime
    | "dateheader" -> Ok InternalDateSourceEnum.DateHeader
    | _ -> Error(sprintf "unknown internal date source value: %s" v)
