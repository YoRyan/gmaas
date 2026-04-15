module Gmaas.Gmail

open Gmaas.Helpers
open Google.Apis.Gmail.v1
open System
open System.Buffers.Text
open System.Collections.Generic
open System.Text
open System.Web

type InternalDateSourceEnum = UsersResource.MessagesResource.ImportRequest.InternalDateSourceEnum

type Attachment =
    { ContentType: string
      Filename: string
      Base64: string }

type Body =
    | SinglePart of string
    | MultiPart of contentType: string * content: string * attachments: Attachment list

type Message =
    { LabelIds: string list option
      Headers: (string * string) list
      Body: Body
      InternalDateSource: InternalDateSourceEnum option
      NeverMarkSpam: bool option
      ProcessForCalendar: bool option
      Deleted: bool option }

let private makeEnvelope (headers: (string * string) list) (body: Body) : string =
    let body, bodyHeaders =
        match body with
        | SinglePart s -> s, List.empty
        | MultiPart(contentType, content, []) -> content, [ "Content-Type", contentType ]
        | MultiPart(contentType, content, attachments) ->
            let boundary = $"boundary_{Guid.NewGuid()}"
            let content = $"--{boundary}\nContent-Type: {contentType}\n\n{content}"

            let attachments =
                attachments
                |> List.map (fun a ->
                    let contentType = $"{contentType}; name=\"{HttpUtility.UrlEncode a.Filename}\""
                    $"--{boundary}\nContent-Type: {contentType}\nContent-Transfer-Encoding: base64\n\n{a.Base64}")
                |> String.concat "\n\n"

            $"{content}\n\n{attachments}\n\n--{boundary}--",
            [ "Content-Type", "multipart/mixed; boundary=\"{boundary}\"" ]

    let headers =
        headers
        |> overrideHeaders bodyHeaders
        |> Seq.map (fun (k, v) -> $"{k}: {v}")
        |> String.concat "\n"

    $"{headers}\n\n{body}\n"

let importToGmail (gmail: GmailService) (msg: Message) =
    task {
        let envelope = makeEnvelope msg.Headers msg.Body
        let data = Data.Message()
        data.Raw <- Encoding.UTF8.GetBytes envelope |> Base64Url.EncodeToString

        data.LabelIds <-
            msg.LabelIds
            |> Option.defaultValue [ "INBOX" ]
            |> List.insertAt 0 "UNREAD"
            |> List

        let request = gmail.Users.Messages.Import(data, "me")
        request.NeverMarkSpam <- msg.NeverMarkSpam |> Option.defaultValue true
        request.ProcessForCalendar <- msg.ProcessForCalendar |> Option.defaultValue false
        request.Deleted <- msg.Deleted |> Option.defaultValue false

        request.InternalDateSource <-
            msg.InternalDateSource
            |> Option.defaultValue InternalDateSourceEnum.ReceivedTime

        return! request.ExecuteAsync()
    }
