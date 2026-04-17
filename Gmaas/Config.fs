module Gmaas.Config

open System
open System.IO
open System.Threading
open System.Threading.Tasks

open Google.Apis.Auth.OAuth2
open Google.Apis.Auth.OAuth2.Flows
open Google.Apis.Auth.OAuth2.Responses
open Google.Apis.Gmail.v1
open Google.Apis.Services
open Google.Apis.Util.Store
open Meziantou.Framework.Http
open Tomlyn.Model

open Gmaas.Gmail

[<Literal>]
let private defaultHttp = "http://[::1]:8080"

[<Literal>]
let private applicationName = "gmaas"

[<Literal>]
let private userId = "user"

type GmailOutput =
    { LabelIds: string list option
      HeaderTemplates: Map<string, string>
      BodyTemplate: string option
      BodyMime: string option
      InternalDateSource: string option
      NeverMarkSpam: bool option
      ProcessForCalendar: bool option
      Deleted: bool option }

type AppriseMatch =
    { User: string option
      Type: string option
      Format: string option }

type AppriseMiddleware =
    { Match: AppriseMatch
      Output: GmailOutput }

type ShoutrrrMatch = { User: string option }

type ShoutrrrMiddleware =
    { Match: ShoutrrrMatch
      Output: GmailOutput }

type ServeConfig =
    { Htpasswd: HtpasswdFile option
      AuthGmailInsert: Set<string>
      AuthGmailSend: Set<string>
      AppriseMiddleware: AppriseMiddleware list
      ShoutrrrMiddleware: ShoutrrrMiddleware list
      HttpAddress: string
      Gmail: IGmailFs }

type private CowardlyCodeReceiver() =
    interface ICodeReceiver with
        member this.ReceiveCodeAsync
            (url: Requests.AuthorizationCodeRequestUrl, taskCancellationToken: CancellationToken)
            : Task<Responses.AuthorizationCodeResponseUrl> =
            failwith "We got asked for an OAuth authorization flow. Aborting!"

        member this.RedirectUri: string = ""

type private TerminalCodeReceiver() =
    interface ICodeReceiver with
        member this.ReceiveCodeAsync
            (url: Requests.AuthorizationCodeRequestUrl, taskCancellationToken: CancellationToken)
            : Task<Responses.AuthorizationCodeResponseUrl> =
            task {
                let prompt = url.Build().AbsoluteUri
                printfn "Navigate to the following URL in your browser:\n%s" prompt

                printfn
                    "\nOnce you've authorized the request, your browser will redirect to an http://localhost URL that will fail to load. Paste the entire URL here:"

                let redirect = Uri(Console.ReadLine())
                let response = AuthorizationCodeResponseUrl redirect.Query
                printfn "\nOAuth flow completed."
                return response
            }

        // We have no intention to actually spin up a web server here, but
        // Google considers any non-localhost URL "insecure."
        member this.RedirectUri: string = "http://localhost"

let private loadCredentials
    (codeReceiver: ICodeReceiver)
    (file: FileInfo)
    (store: DirectoryInfo)
    : Task<UserCredential> =
    task {
        let! secrets = GoogleClientSecrets.FromFileAsync file.FullName

        let initializer = GoogleAuthorizationCodeFlow.Initializer()
        initializer.ClientSecrets <- secrets.Secrets
        initializer.DataStore <- new FileDataStore(store.FullName, true)

        initializer.Scopes <-
            seq {
                GmailService.Scope.GmailInsert
                GmailService.Scope.GmailSend
            }

        return!
            AuthorizationCodeInstalledApp(new GoogleAuthorizationCodeFlow(initializer), codeReceiver)
                .AuthorizeAsync(userId, CancellationToken.None)
    }

let private inTable<'T> k (t: TomlTable) =
    if t.ContainsKey k then Some(t.[k] :?> 'T) else None

let private asList<'T> (a: TomlArray) : 'T list = Seq.cast<'T> a |> Seq.toList

let private asTableList (ta: TomlTableArray) : TomlTable list = ta |> Seq.toList

let private asMap<'V> (t: TomlTable) : Map<string, 'V> =
    t |> Seq.map (|KeyValue|) |> Seq.cast<string * 'V> |> Map.ofSeq

let private loadGmailOutput (t: TomlTable) =
    { LabelIds = t |> inTable "labelids" |> Option.map asList<string>
      HeaderTemplates =
        t
        |> inTable<TomlTable> "headers"
        |> Option.map asMap<string>
        |> Option.defaultValue Map.empty
      BodyTemplate = t |> inTable<string> "body"
      BodyMime = t |> inTable<string> "bodytype"
      InternalDateSource = t |> inTable<string> "internaldatesource"
      NeverMarkSpam = t |> inTable<bool> "nevermarkspam"
      ProcessForCalendar = t |> inTable<bool> "processforcalendar"
      Deleted = t |> inTable<bool> "deleted" }

let loadServeConfig (t: TomlTable) =
    task {
        let google = t |> inTable "google"
        let googleScopes = google |> Option.bind (inTable "scopes")
        let http = t |> inTable "http"

        let credentialsFile =
            match google |> Option.bind (inTable "credentials") with
            | Some path -> FileInfo path
            | None -> failwith "Missing path to Google credentials file."

        let credentialsStore =
            match google |> Option.bind (inTable "tokensstore") with
            | Some path -> DirectoryInfo path
            | None -> failwith "Missing path to Google tokens directory."

        let! credentials = loadCredentials (CowardlyCodeReceiver()) credentialsFile credentialsStore
        let initializer = BaseClientService.Initializer()
        initializer.HttpClientInitializer <- credentials
        initializer.ApplicationName <- applicationName

        return
            { Htpasswd = t |> inTable<string> "htpasswd" |> Option.map HtpasswdFile.Parse
              AuthGmailInsert =
                googleScopes
                |> Option.bind (inTable "gmail")
                |> Option.bind (inTable "insert")
                |> Option.map asList
                |> Option.defaultValue []
                |> Set.ofList
              AuthGmailSend =
                googleScopes
                |> Option.bind (inTable "gmail")
                |> Option.bind (inTable "send")
                |> Option.map asList
                |> Option.defaultValue []
                |> Set.ofList
              AppriseMiddleware =
                http
                |> Option.bind (inTable "apprise")
                |> Option.bind (inTable "middleware")
                |> Option.map asTableList
                |> Option.defaultValue []
                |> List.choose (fun t ->
                    let mtch = t |> inTable "match"
                    let output = t |> inTable "output" |> Option.map loadGmailOutput

                    match mtch, output with
                    | Some mtch, Some output ->
                        Some
                            { AppriseMiddleware.Match =
                                { User = mtch |> inTable "user"
                                  Type = mtch |> inTable "type"
                                  Format = mtch |> inTable "format" }
                              AppriseMiddleware.Output = output }
                    | _ -> None)
              ShoutrrrMiddleware =
                http
                |> Option.bind (inTable "shoutrrr")
                |> Option.bind (inTable "middleware")
                |> Option.map asTableList
                |> Option.defaultValue []
                |> List.choose (fun t ->
                    match t |> inTable "match", t |> inTable "output" |> Option.map loadGmailOutput with
                    | Some mtch, Some output ->
                        Some
                            { ShoutrrrMiddleware.Match = { User = mtch |> inTable "user" }
                              ShoutrrrMiddleware.Output = output }
                    | _ -> None)
              HttpAddress = http |> Option.bind (inTable "address") |> Option.defaultValue defaultHttp
              Gmail = GmailFs(new GmailService(initializer)) }
    }

let doAuthFlow (t: TomlTable) =
    task {
        let google = t |> inTable "google"

        let credentialsFile =
            match google |> Option.bind (inTable "credentials") with
            | Some path -> FileInfo path
            | None -> failwith "Missing path to Google credentials file."

        let credentialsStore =
            match google |> Option.bind (inTable "tokensstore") with
            | Some path -> DirectoryInfo path
            | None -> failwith "Missing path to Google tokens directory."

        let! credentials = loadCredentials (TerminalCodeReceiver()) credentialsFile credentialsStore
        let initializer = BaseClientService.Initializer()
        initializer.HttpClientInitializer <- credentials
        initializer.ApplicationName <- applicationName

        new GmailService(initializer) |> ignore

        printfn "Successfully logged into Gmail."
        return ()
    }
