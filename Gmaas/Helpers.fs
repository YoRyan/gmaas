module Gmaas.Helpers

open Google.Apis.Gmail.v1

type InternalDateSourceEnum = UsersResource.MessagesResource.ImportRequest.InternalDateSourceEnum

let private standardize (s: string) = s.Trim().ToLowerInvariant()

let overrideHeaders overrides (source: (string * string) seq) =
    let overrideKeys = overrides |> List.map (fun (k, _v) -> standardize k) |> Set.ofList

    seq {
        yield! source |> Seq.filter (fun (k, _v) -> not (k |> standardize |> overrideKeys.Contains))
        yield! overrides
    }

let getHeader key (headers: (string * string) seq) =
    headers
    |> Seq.choose (fun (k, v) -> if k = key then Some v else None)
    |> List.ofSeq

let parseInternalDateSource (v: string) =
    match standardize v with
    | "receivedtime" -> Ok InternalDateSourceEnum.ReceivedTime
    | "dateheader" -> Ok InternalDateSourceEnum.DateHeader
    | _ -> Error(sprintf "unknown internal date source value: %s" v)
