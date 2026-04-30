module ForTheRecord.Config

open System
open System.IO
open System.Threading.Tasks

open Google.Apis.Gmail.v1
open Google.Apis.Services
open Meziantou.Framework.Http
open Tomlyn.Model

open ForTheRecord.Gmail
open ForTheRecord.Imap

[<Literal>]
let private defaultHttp = "http://[::1]:8080"

[<Literal>]
let private applicationName = "ForTheRecord"

type ConfiguredInbox =
    | Gmail of authInsert: Set<string> * authSend: Set<string> * inbox: IGmailInbox
    | Imap of authAppend: Set<string> * inbox: IImapInbox

type ServeConfig =
    { Htpasswd: HtpasswdFile option
      HttpAddress: string
      Inbox: ConfiguredInbox }

let private inTable<'T> k (t: TomlTable) =
    match t.TryGetValue k with
    | true, v -> Some v
    | false, _ -> None
    |> Option.bind (function
        | :? 'T as v -> Some v
        | _ -> None)

let private asList<'T> (a: TomlArray) : 'T list = Seq.cast<'T> a |> Seq.toList

let private asTableList (ta: TomlTableArray) : TomlTable list = ta |> Seq.toList

let private asMap<'V> (t: TomlTable) : Map<string, 'V> =
    t |> Seq.map (|KeyValue|) |> Seq.cast<string * 'V> |> Map.ofSeq

let loadServeConfig (loadInboxConfig: TomlTable -> Task<ConfiguredInbox>) (t: TomlTable) =
    task {
        let http = t |> inTable "http"
        let! inbox = loadInboxConfig t

        return
            { Htpasswd = t |> inTable<string> "htpasswd" |> Option.map HtpasswdFile.Parse
              HttpAddress = http |> Option.bind (inTable "address") |> Option.defaultValue defaultHttp
              Inbox = inbox }
    }

let loadGmailConfig (t: TomlTable) =
    task {
        let google = t |> inTable "google"
        let googleScopes = google |> Option.bind (inTable "scopes")

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
        let service = new GmailService(initializer)

        let authInsert =
            googleScopes
            |> Option.bind (inTable "gmail")
            |> Option.bind (inTable "insert")
            |> Option.map asList
            |> Option.defaultValue []
            |> Set.ofList

        let authSend =
            googleScopes
            |> Option.bind (inTable "gmail")
            |> Option.bind (inTable "send")
            |> Option.map asList
            |> Option.defaultValue []
            |> Set.ofList

        return Gmail(authInsert, authSend, GmailInbox service)
    }

let getGmailInbox (config: ServeConfig) =
    match config.Inbox with
    | Gmail(_authInsert, _authSend, inbox) -> inbox
    | _ -> raise (InvalidOperationException())

let getImapInbox (config: ServeConfig) =
    match config.Inbox with
    | Imap(_authAppend, inbox) -> inbox
    | _ -> raise (InvalidOperationException())

let testGmail (t: TomlTable) =
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
