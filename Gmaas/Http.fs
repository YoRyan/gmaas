module Gmaas.Http

open System
open System.IO
open System.Security.Claims
open System.Threading.Tasks

open Giraffe
open idunno.Authentication.Basic
open Microsoft.AspNetCore.Http
open Microsoft.AspNetCore.Builder
open Microsoft.Extensions.DependencyInjection

open Gmaas.Config
open Gmaas.Gmail
open Gmaas.Helpers

module private Realms =
    [<Literal>]
    let configured = "Config"

module private Roles =
    [<Literal>]
    let gmailInsert = "GmailInsert"

    [<Literal>]
    let gmailSend = "GmailSend"

let private defaultHeaders (ctx: HttpContext) =
    seq {
        "From",
        (match ctx.User.FindFirst ClaimTypes.NameIdentifier with
         | null -> "gmaas"
         | c -> c.Value)
    }

let private requestHeaders (ctx: HttpContext) =
    ctx.Request.Headers
    |> Seq.map (|KeyValue|)
    |> Seq.map (fun (k, sv) -> sv |> Seq.cast<string> |> Seq.map (fun s -> k, s))
    |> Seq.concat
    |> List.ofSeq

let private ezImportHandler: HttpHandler =
    fun (next: HttpFunc) (ctx: HttpContext) ->
        task {
            let config = ctx.GetService<ServeConfig>()

            let headers =
                seq {
                    yield! defaultHeaders ctx
                    "Content-Type", "text/plain"
                }
                |> overrideHeaders (requestHeaders ctx)
                |> List.ofSeq

            let! body = ctx.ReadBodyFromRequestAsync()

            let! _ =
                config.Gmail.Import
                    { LabelIds = None
                      Headers = headers
                      Body = SinglePart body
                      InternalDateSource = None
                      NeverMarkSpam = None
                      ProcessForCalendar = None
                      Deleted = None }

            return! next ctx
        }

[<CLIMutable>]
type ImportForm =
    { LabelId: string list option
      Body: string option
      BodyType: string option
      InternalDateSource: string option
      NeverMarkSpam: bool option
      ProcessForCalendar: bool option
      Deleted: bool option }

let private readAttachment (file: IFormFile) =
    task {
        use stream = file.OpenReadStream()
        use memory = new MemoryStream()
        do! stream.CopyToAsync memory

        return
            { ContentType = file.ContentType
              Filename = file.FileName
              Base64 = Convert.ToBase64String(memory.ToArray()) }
    }

let private importHandler: HttpHandler =
    fun (next: HttpFunc) (ctx: HttpContext) ->
        task {
            let config = ctx.GetService<ServeConfig>()
            let! form = ctx.BindFormAsync<ImportForm>()

            let headers =
                defaultHeaders ctx |> overrideHeaders (requestHeaders ctx) |> List.ofSeq

            let! attachments = ctx.Request.Form.Files |> Seq.map readAttachment |> Task.WhenAll

            let! _ =
                config.Gmail.Import
                    { LabelIds = form.LabelId
                      Headers = headers
                      Body =
                        MultiPart(
                            form.BodyType |> Option.defaultValue "text/plain",
                            form.Body |> Option.defaultValue "",
                            attachments |> List.ofArray
                        )
                      InternalDateSource =
                        form.InternalDateSource
                        |> Option.bind (parseInternalDateSource >> Result.toOption)
                      NeverMarkSpam = form.NeverMarkSpam
                      ProcessForCalendar = form.ProcessForCalendar
                      Deleted = form.Deleted }

            return! next ctx
        }

let private requiresRole role =
    fun (next: HttpFunc) (ctx: HttpContext) ->
        let config = ctx.GetService<ServeConfig>()

        (if config.Htpasswd.IsSome then
             requiresAuthentication (RequestErrors.UNAUTHORIZED "Basic" Realms.configured "Authentication failed")
             >=> requiresRole role (RequestErrors.FORBIDDEN "Required scope not granted")
         else
             id)
            next
            ctx

let webApp =
    choose
        [ POST
          >=> choose
                  [ route "/api/messages/import/ez"
                    >=> requiresRole Roles.gmailInsert
                    >=> ezImportHandler
                    route "/api/messages/import"
                    >=> requiresRole Roles.gmailInsert
                    >=> importHandler ]
          RequestErrors.NOT_FOUND "404" ]

let private validateCredentials (context: ValidateCredentialsContext) =
    let config = context.HttpContext.RequestServices.GetService<ServeConfig>()

    let verified =
        match config.Htpasswd with
        | Some htpasswd -> htpasswd.VerifyCredentials(context.Username, context.Password)
        | None -> false

    if verified then
        let claims =
            seq {
                ClaimTypes.NameIdentifier, context.Username
                ClaimTypes.Name, context.Username

                if config.AuthGmailInsert.Contains context.Username then
                    ClaimTypes.Role, Roles.gmailInsert

                if config.AuthGmailSend.Contains context.Username then
                    ClaimTypes.Role, Roles.gmailSend
            }
            |> Seq.map (fun (``type``, value) ->
                Claim(``type``, value, ClaimValueTypes.String, context.Options.ClaimsIssuer))

        context.Principal <- ClaimsPrincipal(ClaimsIdentity(claims, context.Scheme.Name))
        context.Success()

    Task.CompletedTask

let configureServices (config: ServeConfig) (services: IServiceCollection) =
    let authEvents = BasicAuthenticationEvents()
    authEvents.OnValidateCredentials <- validateCredentials

    services
        .AddAuthentication(BasicAuthenticationDefaults.AuthenticationScheme)
        .AddBasic(fun options ->
            options.Realm <- Realms.configured
            options.Events <- authEvents
            ())
    |> ignore

    services.AddSingleton<ServeConfig>(config).AddGiraffe() |> ignore

let serveHttpAsync (config: ServeConfig) =
    task {
        let builder = WebApplication.CreateBuilder()
        configureServices config builder.Services

        let app = builder.Build()
        app.UseAuthentication() |> ignore
        app.UseGiraffe webApp

        return! app.RunAsync()
    }
