module ForTheRecord.Http

open System.IO
open System.Security.Claims
open System.Text
open System.Threading.Tasks

open Giraffe
open idunno.Authentication.Basic
open Microsoft.AspNetCore.Http
open Microsoft.AspNetCore.Builder
open Microsoft.Extensions.DependencyInjection
open MimeKit

open ForTheRecord.Config
open ForTheRecord.Gmail

module private Realms =
    [<Literal>]
    let configured = "Config"

module private Roles =
    [<Literal>]
    let gmailInsert = "GmailInsert"

    [<Literal>]
    let gmailSend = "GmailSend"

    [<Literal>]
    let imapAppend = "ImapAppend"

let private parseContentType (s: string) =
    let (ok, ct) = ContentType.TryParse(s)

    if ok then
        Ok ct
    else
        Error(sprintf "Invalid MIME content type: %s" s)

let private mapHeaders (ctx: HttpContext) =
    let headers, contentTypeHeaders =
        ctx.Request.Headers
        |> Seq.map (|KeyValue|)
        |> Seq.map (fun (k, sv) -> sv |> Seq.cast<string> |> Seq.map (fun s -> k, s))
        |> Seq.concat
        |> List.ofSeq
        |> List.partition (fun (k, v) -> k.ToLowerInvariant() <> "content-type")

    // All emails must at least have a valid From: field.
    let headersWithFrom =
        [ if not (List.exists (fun (k: string, _) -> k.ToLowerInvariant() = "from") headers) then
              "From",
              (match ctx.User.FindFirst ClaimTypes.NameIdentifier with
               | null -> "ForTheRecord"
               | c -> c.Value)
          yield! headers ]

    let headerList = HeaderList()

    for k, v in headersWithFrom do
        headerList.Add(k, v)

    let contentType =
        contentTypeHeaders
        |> Seq.tryExactlyOne
        |> Option.map (fun (_, v) -> v)
        |> Option.bind (parseContentType >> Result.toOption)

    headerList, contentType

let private ezImportHandler: HttpHandler =
    fun (next: HttpFunc) (ctx: HttpContext) ->
        task {
            let config = ctx.GetService<ServeConfig>()
            let messageHeaders, contentType = mapHeaders ctx
            use message = new MimeMessage(messageHeaders)

            use body =
                new MimePart(contentType |> Option.defaultWith (fun () -> ContentType("text", "plain")))

            let! bodyText = ctx.ReadBodyFromRequestAsync()
            use bodyStream = new MemoryStream(Encoding.UTF8.GetBytes bodyText)
            body.Content <- new MimeContent(bodyStream)
            message.Body <- body

            use stream = new MemoryStream()
            do! message.WriteToAsync stream

            let! _ = (getGmailInbox config).Import(stream.ToArray())

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
    let attach =
        new MimePart(
            file.ContentType
            |> parseContentType
            |> Result.defaultWith (fun _ -> ContentType("application", "octet-stream"))
        )

    attach.Content <- new MimeContent(file.OpenReadStream())
    attach.ContentDisposition <- ContentDisposition(ContentDisposition.Attachment)
    attach.ContentTransferEncoding <- ContentEncoding.Base64
    attach.FileName <- file.FileName
    attach

let private importHandler: HttpHandler =
    fun (next: HttpFunc) (ctx: HttpContext) ->
        task {
            let config = ctx.GetService<ServeConfig>()
            let! form = ctx.BindFormAsync<ImportForm>()
            use multipart = new Multipart "mixed"

            use body =
                new MimePart(
                    form.BodyType
                    |> Option.bind (parseContentType >> Result.toOption)
                    |> Option.defaultWith (fun () -> ContentType("text", "plain"))
                )

            use bodyStream =
                new MemoryStream(form.Body |> Option.defaultValue "" |> Encoding.UTF8.GetBytes)

            body.Content <- new MimeContent(bodyStream)

            for entity in
                seq {
                    body
                    yield! ctx.Request.Form.Files |> Seq.map readAttachment
                } do
                multipart.Add(entity)

            let messageHeaders, _ = mapHeaders ctx
            use message = new MimeMessage(messageHeaders)
            message.Body <- multipart

            use stream = new MemoryStream()
            do! message.WriteToAsync stream

            let! _ =
                (getGmailInbox config)
                    .Import(
                        stream.ToArray(),
                        ?labelIds = form.LabelId,
                        ?internalDateSource =
                            (form.InternalDateSource
                             |> Option.bind (parseInternalDateSource >> Result.toOption)),
                        ?neverMarkSpam = form.NeverMarkSpam,
                        ?processForCalendar = form.ProcessForCalendar,
                        ?deleted = form.Deleted
                    )

            return! next ctx
        }

let private requiresGmail =
    fun (next: HttpFunc) (ctx: HttpContext) ->
        let config = ctx.GetService<ServeConfig>()

        (if config.Inbox.IsGmail then
             id
         else
             RequestErrors.GONE "Gmail not configured")
            next
            ctx

let private requiresImap =
    fun (next: HttpFunc) (ctx: HttpContext) ->
        let config = ctx.GetService<ServeConfig>()

        (if config.Inbox.IsImap then
             id
         else
             RequestErrors.GONE "IMAP not configured")
            next
            ctx

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
                  [ route "/api/gmail/messages/import/ez"
                    >=> requiresRole Roles.gmailInsert
                    >=> requiresGmail
                    >=> ezImportHandler
                    route "/api/gmail/messages/import"
                    >=> requiresRole Roles.gmailInsert
                    >=> requiresGmail
                    >=> importHandler ]
          RequestErrors.NOT_FOUND "404" ]

let private validateCredentials (context: ValidateCredentialsContext) =
    let config = context.HttpContext.RequestServices.GetService<ServeConfig>()

    let verified =
        match config.Htpasswd with
        | Some htpasswd -> htpasswd.VerifyCredentials(context.Username, context.Password)
        | None -> false

    if verified then
        let checkedRole (auth: Set<string>) (role: string) =
            seq {
                if auth.Contains context.Username then
                    ClaimTypes.Role, role
            }

        let claims =
            seq {
                ClaimTypes.NameIdentifier, context.Username
                ClaimTypes.Name, context.Username

                match config.Inbox with
                | Gmail(authInsert, authSend, _inbox) ->
                    yield! checkedRole authInsert Roles.gmailInsert
                    yield! checkedRole authSend Roles.gmailSend
                | Imap(authAppend, _inbox) -> yield! checkedRole authAppend Roles.imapAppend
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
