module ForTheRecord.Gmail

open System
open System.Buffers.Text
open System.Collections.Generic
open System.IO
open System.Threading
open System.Threading.Tasks

open Google.Apis.Auth.OAuth2
open Google.Apis.Auth.OAuth2.Flows
open Google.Apis.Auth.OAuth2.Responses
open Google.Apis.Gmail.v1
open Google.Apis.Util.Store

[<Literal>]
let private userId = "user"

type InternalDateSourceEnum = UsersResource.MessagesResource.ImportRequest.InternalDateSourceEnum

let parseInternalDateSource (s: string) =
    match s.ToLowerInvariant() with
    | "receivedtime" -> Ok InternalDateSourceEnum.ReceivedTime
    | "dateheader" -> Ok InternalDateSourceEnum.DateHeader
    | _ -> Error(sprintf "unknown internal date source value: %s" s)

type CowardlyCodeReceiver() =
    interface ICodeReceiver with
        member this.ReceiveCodeAsync
            (url: Requests.AuthorizationCodeRequestUrl, taskCancellationToken: CancellationToken)
            : Task<Responses.AuthorizationCodeResponseUrl> =
            failwith "We got asked for an OAuth authorization flow. Aborting!"

        member this.RedirectUri: string = ""

type TerminalCodeReceiver() =
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

let loadCredentials (codeReceiver: ICodeReceiver) (file: FileInfo) (store: DirectoryInfo) : Task<UserCredential> =
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

type IGmailInbox =
    abstract member Import:
        message: ReadOnlySpan<byte> *
        ?labelIds: string list *
        ?internalDateSource: InternalDateSourceEnum *
        ?neverMarkSpam: bool *
        ?processForCalendar: bool *
        ?deleted: bool ->
            Task<unit>

    abstract member Send: message: ReadOnlySpan<byte> -> Task<Data.Message>

type GmailInbox(service: GmailService) =
    interface IGmailInbox with
        member _.Import
            (
                message: ReadOnlySpan<byte>,
                ?labelIds: string list,
                ?internalDateSource: InternalDateSourceEnum,
                ?neverMarkSpam: bool,
                ?processForCalendar: bool,
                ?deleted: bool
            ) : Task<unit> =
            let data = Data.Message()
            data.Raw <- Base64Url.EncodeToString message
            data.LabelIds <- labelIds |> Option.defaultValue [ "INBOX" ] |> List.insertAt 0 "UNREAD" |> List

            let request = service.Users.Messages.Import(data, "me")
            request.NeverMarkSpam <- neverMarkSpam |> Option.defaultValue true
            request.ProcessForCalendar <- processForCalendar |> Option.defaultValue false
            request.Deleted <- deleted |> Option.defaultValue false
            request.InternalDateSource <- internalDateSource |> Option.defaultValue InternalDateSourceEnum.ReceivedTime

            task {
                let! _ = request.ExecuteAsync()
                return ()
            }

        member _.Send(message: ReadOnlySpan<byte>) : Task<Data.Message> = failwith "Not Implemented"
