module Gmaas.Http

open Giraffe
open Google.Apis.Gmail.v1
open Gmaas.Config
open Gmaas.Gmail
open Gmaas.Helpers
open Microsoft.AspNetCore.Http
open Microsoft.AspNetCore.Builder
open Microsoft.Extensions.DependencyInjection
open Microsoft.Extensions.Primitives

let private expandHeaders (sv: (string * StringValues) seq) =
    sv
    |> Seq.map (fun (k, sv) -> sv |> Seq.cast<string> |> Seq.map (fun s -> k, s))
    |> Seq.concat

let private ezImportHandler: HttpHandler =
    fun (next: HttpFunc) (ctx: HttpContext) ->
        task {
            let! body = ctx.ReadBodyFromRequestAsync()

            let headers =
                seq { "From", "gmaas" }
                |> overrideHeaders (ctx.Request.Headers |> Seq.map (|KeyValue|) |> expandHeaders |> List.ofSeq)
                |> overrideHeaders [ "Content-Type", "text/plain" ]
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

let private webApp = choose [ route "/api/messages/import/ez" >=> ezImportHandler ]

let serveHttpAsync (config: ServeConfig) =
    task {
        let builder = WebApplication.CreateBuilder()
        builder.Services.AddGiraffe().AddSingleton<GmailService> config.Gmail |> ignore

        let app = builder.Build()
        app.UseGiraffe webApp

        return! app.RunAsync()
    }
