module Gmaas.Http

open Giraffe
open Google.Apis.Gmail.v1
open Gmaas.Config
open Gmaas.Gmail
open Microsoft.AspNetCore.Http
open Microsoft.AspNetCore.Builder
open Microsoft.Extensions.DependencyInjection
open Microsoft.Extensions.Primitives

let private flattenStrings (sv: StringValues) : string =
    match sv.Count with
    | 0 -> ""
    | 1 -> sv[0]
    | _ -> sv[sv.Count - 1]

let private ezImportHandler: HttpHandler =
    fun (next: HttpFunc) (ctx: HttpContext) ->
        task {
            let! body = ctx.ReadBodyFromRequestAsync()

            let headers =
                (seq {
                    "From", "gmaas"

                    yield!
                        ctx.Request.Headers
                        |> Seq.map (|KeyValue|)
                        |> Seq.map (fun (k, sv) -> k, flattenStrings sv)

                    "Content-Type", "text/plain"
                })
                |> List.ofSeq

            let! _ =
                importToGmail
                    (ctx.GetService<GmailService>())
                    { LabelIds = None
                      Headers = headers
                      Body = SinglePart body
                      InternalDateSource = None
                      NeverMarkSpam = None
                      ProcessForCalendar = None
                      Deleted = None }

            return! next ctx
        }

let private webApp =
    choose [ route "/api/messages/import/ez" >=> ezImportHandler ]

let serveHttpAsync (config: ServeConfig) =
    task {
        let builder = WebApplication.CreateBuilder()
        builder.Services.AddGiraffe().AddSingleton<GmailService> config.Gmail |> ignore

        let app = builder.Build()
        app.UseGiraffe webApp

        return! app.RunAsync()
    }
