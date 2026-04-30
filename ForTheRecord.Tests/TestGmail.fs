module Tests.Gmail

open System
open System.Collections.Generic
open System.IO
open System.Net
open System.Net.Http
open System.Net.Http.Headers
open System.Text

open Giraffe
open Meziantou.Framework.Http
open Microsoft.AspNetCore.Builder
open Microsoft.AspNetCore.TestHost
open Microsoft.Extensions.Hosting
open MimeKit
open MimeKit.Text
open Xunit

open ForTheRecord.Config
open ForTheRecord.Gmail
open ForTheRecord.Http

open Mocks

let private getTestApp (config: ServeConfig) =
    let builder = WebApplication.CreateBuilder()
    builder.WebHost.UseTestServer() |> ignore
    configureServices config builder.Services

    let app = builder.Build()
    app.UseGiraffe webApp
    app.Start()
    app

let private testRequest (config: ServeConfig) (request: HttpRequestMessage) =
    let resp =
        task {
            use server = getTestApp(config).GetTestServer()
            use client = server.CreateClient()
            let! response = request |> client.SendAsync
            return response
        }

    resp.Result

let private mockWithoutAuth () =
    let mock = MockGmailInbox()

    let config =
        { Htpasswd = None
          HttpAddress = ""
          AppriseTemplates = Map.empty
          Inbox = Gmail(Set.empty, Set.empty, mock) }

    config, mock

let private mockWithHunter2Auth (user: string) (hasInsert: bool) (hasSend: bool) =
    let mock = MockGmailInbox()

    let authSet yay =
        if yay then Set(Seq.singleton user) else Set.empty

    let config =
        { Htpasswd = Some(HtpasswdFile.Parse $"{user}:$apr1$nKTVHFsh$8gVerNz4iYOp211EbpBpJ0\n")
          HttpAddress = ""
          AppriseTemplates = Map.empty
          Inbox = Gmail(authSet hasInsert, authSet hasSend, mock) }

    config, mock

let private makeBasicAuth (user: string) (pass: string) =
    AuthenticationHeaderValue("Basic", $"{user}:{pass}" |> Encoding.UTF8.GetBytes |> Convert.ToBase64String)

let private makeContent (mime: string) (s: string) =
    let content = new ByteArrayContent(Encoding.UTF8.GetBytes s)
    content.Headers.ContentType <- Headers.MediaTypeHeaderValue mime
    content

let private makeTextContent = makeContent "text/plain"

let private readEntity (e: MimeEntity) =
    use stream = new MemoryStream()
    e.WriteTo stream
    stream.ToArray()

[<Fact>]
let ``Endpoints not available when Gmail is not configured`` () =
    let mock = MockImapInbox()

    let config =
        { Htpasswd = None
          HttpAddress = ""
          AppriseTemplates = Map.empty
          Inbox = Imap(Set.empty, mock) }

    let request =
        new HttpRequestMessage(HttpMethod.Post, "/api/gmail/messages/import/ez")

    let response = testRequest config request
    Assert.Equal(HttpStatusCode.Gone, response.StatusCode)

    use content = new FormUrlEncodedContent(seq { "body", "" } |> Seq.map KeyValuePair)
    let request = new HttpRequestMessage(HttpMethod.Post, "/api/gmail/messages/import")
    request.Content <- content
    let response = testRequest config request
    Assert.Equal(HttpStatusCode.Gone, response.StatusCode)

[<Fact>]
let ``Authenticated endpoints work when authentication is disabled`` () =
    let config, _ = mockWithoutAuth ()

    let request =
        new HttpRequestMessage(HttpMethod.Post, "/api/gmail/messages/import/ez")

    let response = testRequest config request
    Assert.Equal(HttpStatusCode.OK, response.StatusCode)

    use content = new FormUrlEncodedContent(seq { "body", "" } |> Seq.map KeyValuePair)
    let request = new HttpRequestMessage(HttpMethod.Post, "/api/gmail/messages/import")
    request.Content <- content
    let response = testRequest config request
    Assert.Equal(HttpStatusCode.OK, response.StatusCode)

[<Fact>]
let ``Authenticated endpoints require authentication`` () =
    let config, _ = mockWithHunter2Auth "AzureDiamond" true true

    let request =
        new HttpRequestMessage(HttpMethod.Post, "/api/gmail/messages/import/ez")

    let response = testRequest config request
    Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode)

    use content = new FormUrlEncodedContent(seq { "body", "" } |> Seq.map KeyValuePair)
    let request = new HttpRequestMessage(HttpMethod.Post, "/api/gmail/messages/import")
    request.Content <- content
    let response = testRequest config request
    Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode)

[<Fact>]
let ``Authenticated endpoints work with basic authentication`` () =
    let config, _ = mockWithHunter2Auth "AzureDiamond" true true
    let authHeader = makeBasicAuth "AzureDiamond" "hunter2"

    let request =
        new HttpRequestMessage(HttpMethod.Post, "/api/gmail/messages/import/ez")

    request.Headers.Authorization <- authHeader
    let response = testRequest config request
    Assert.Equal(HttpStatusCode.OK, response.StatusCode)

    use content = new FormUrlEncodedContent(seq { "body", "" } |> Seq.map KeyValuePair)
    let request = new HttpRequestMessage(HttpMethod.Post, "/api/gmail/messages/import")
    request.Headers.Authorization <- authHeader
    request.Content <- content
    let response = testRequest config request
    Assert.Equal(HttpStatusCode.OK, response.StatusCode)

[<Fact>]
let ``Authenticated endpoints fail with bad authentication`` () =
    let config, _ = mockWithHunter2Auth "AzureDiamond" true true
    let authHeader = makeBasicAuth "AzureDiamond" "whatever"

    let request =
        new HttpRequestMessage(HttpMethod.Post, "/api/gmail/messages/import/ez")

    request.Headers.Authorization <- authHeader
    let response = testRequest config request
    Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode)

    use content = new FormUrlEncodedContent(seq { "body", "" } |> Seq.map KeyValuePair)
    let request = new HttpRequestMessage(HttpMethod.Post, "/api/gmail/messages/import")
    request.Headers.Authorization <- authHeader
    request.Content <- content
    let response = testRequest config request
    Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode)

[<Fact>]
let ``Authenticated import endpoints require the import scope`` () =
    let config, _ = mockWithHunter2Auth "AzureDiamond" false true
    let authHeader = makeBasicAuth "AzureDiamond" "hunter2"

    let request =
        new HttpRequestMessage(HttpMethod.Post, "/api/gmail/messages/import/ez")

    request.Headers.Authorization <- authHeader
    let response = testRequest config request
    Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode)

    use content = new FormUrlEncodedContent(seq { "body", "" } |> Seq.map KeyValuePair)
    let request = new HttpRequestMessage(HttpMethod.Post, "/api/gmail/messages/import")
    request.Headers.Authorization <- authHeader
    request.Content <- content
    let response = testRequest config request
    Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode)

[<Fact>]
let ``Minimal import call produces a valid message`` () =
    let config, mock = mockWithoutAuth ()

    let request =
        new HttpRequestMessage(HttpMethod.Post, "/api/gmail/messages/import/ez")

    let response = testRequest config request
    Assert.Equal(HttpStatusCode.OK, response.StatusCode)

    let called = mock.CalledImport.Value
    Assert.NotEqual(0, called.Message.From.Count)

[<Fact>]
let ``Easy curl import works`` () =
    let config, mock = mockWithoutAuth ()

    use content =
        makeTextContent
            "Lorem ipsum dolor sit amet, consectetur adipiscing elit, sed do eiusmod tempor incididunt ut labore et dolore magna aliqua."

    let request =
        new HttpRequestMessage(HttpMethod.Post, "/api/gmail/messages/import/ez")

    request.Content <- content

    let response = testRequest config request
    Assert.Equal(HttpStatusCode.OK, response.StatusCode)

    let called = mock.CalledImport.Value

    Assert.Equal(
        "Lorem ipsum dolor sit amet, consectetur adipiscing elit, sed do eiusmod tempor incididunt ut labore et dolore magna aliqua.",
        TextFormat.Text |> called.Message.GetTextBody |> _.Trim()
    )

    Assert.Equal("text/plain", called.Message.Body.ContentType.MimeType)

[<Fact>]
let ``Easy curl import passes through headers`` () =
    let config, mock = mockWithoutAuth ()

    let request =
        new HttpRequestMessage(HttpMethod.Post, "/api/gmail/messages/import/ez")

    request.Headers.Add("To", "bob@example.com")
    request.Headers.Add("Subject", "Hello, World!")

    let response = testRequest config request
    Assert.Equal(HttpStatusCode.OK, response.StatusCode)

    let called = mock.CalledImport.Value
    Assert.Equal("bob@example.com", called.Message.To.ToString())
    Assert.Equal("Hello, World!", called.Message.Subject)

[<Fact>]
let ``Easy curl import passes through Content-Type`` () =
    let config, mock = mockWithoutAuth ()

    let request =
        new HttpRequestMessage(HttpMethod.Post, "/api/gmail/messages/import/ez")

    request.Headers.Add("To", "bob@example.com")

    use content = makeTextContent "Now we have <strong>HTML</strong>!"
    content.Headers.ContentType <- Headers.MediaTypeHeaderValue "text/html"
    request.Content <- content

    let response = testRequest config request
    Assert.Equal(HttpStatusCode.OK, response.StatusCode)

    let called = mock.CalledImport.Value
    Assert.Equal("text/html", called.Message.Body.ContentType.MimeType)

[<Fact>]
let ``Curl import with application/x-www-form-urlencoded works`` () =
    let config, mock = mockWithoutAuth ()

    use content =
        new FormUrlEncodedContent(
            seq {
                "body",
                "Lorem ipsum dolor sit amet, consectetur adipiscing elit, sed do eiusmod tempor incididunt ut labore et dolore magna aliqua."

                "labelid", "INBOX"
                "labelid", "STARRED"
                "internaldatesource", "dateheader"
                "nevermarkspam", "true"
                "processforcalendar", "false"
            }
            |> Seq.map KeyValuePair
        )

    let request = new HttpRequestMessage(HttpMethod.Post, "/api/gmail/messages/import")
    request.Content <- content

    let response = testRequest config request
    Assert.Equal(HttpStatusCode.OK, response.StatusCode)

    let called = mock.CalledImport.Value
    Assert.Equivalent(Some [ "INBOX"; "STARRED" ], called.LabelIds)
    Assert.Equivalent(Some InternalDateSourceEnum.DateHeader, called.InternalDateSource)
    Assert.Equivalent(Some true, called.NeverMarkSpam)
    Assert.Equivalent(Some false, called.ProcessForCalendar)
    Assert.Equivalent(None, called.Deleted)

    Assert.Equal(
        "Lorem ipsum dolor sit amet, consectetur adipiscing elit, sed do eiusmod tempor incididunt ut labore et dolore magna aliqua.",
        TextFormat.Plain |> called.Message.GetTextBody |> _.Trim()
    )

[<Fact>]
let ``Curl import with application/x-www-form-urlencoded works with non-plain body`` () =
    let config, mock = mockWithoutAuth ()

    use content =
        new FormUrlEncodedContent(
            seq {
                "body", "Hello, <em>World!</em>"
                "bodytype", "text/html"
            }
            |> Seq.map KeyValuePair
        )

    let request = new HttpRequestMessage(HttpMethod.Post, "/api/gmail/messages/import")
    request.Content <- content

    let response = testRequest config request
    Assert.Equal(HttpStatusCode.OK, response.StatusCode)

    let called = mock.CalledImport.Value
    Assert.Equal("Hello, <em>World!</em>", TextFormat.Html |> called.Message.GetTextBody |> _.Trim())

[<Fact>]
let ``Curl import with multipart/form-data works`` () =
    let config, mock = mockWithoutAuth ()

    use content = new MultipartFormDataContent()

    for k, v in
        seq {
            "body",
            "Lorem ipsum dolor sit amet, consectetur adipiscing elit, sed do eiusmod tempor incididunt ut labore et dolore magna aliqua."

            "labelid", "INBOX"
            "labelid", "STARRED"
            "internaldatesource", "dateheader"
            "nevermarkspam", "true"
            "processforcalendar", "false"
        } do
        content.Add(makeTextContent v, k)

    let request = new HttpRequestMessage(HttpMethod.Post, "/api/gmail/messages/import")
    request.Content <- content

    let response = testRequest config request
    Assert.Equal(HttpStatusCode.OK, response.StatusCode)

    let called = mock.CalledImport.Value
    Assert.Equivalent(Some [ "INBOX"; "STARRED" ], called.LabelIds)
    Assert.Equivalent(Some InternalDateSourceEnum.DateHeader, called.InternalDateSource)
    Assert.Equivalent(Some true, called.NeverMarkSpam)
    Assert.Equivalent(Some false, called.ProcessForCalendar)
    Assert.Equivalent(None, called.Deleted)

    Assert.Equal(
        "Lorem ipsum dolor sit amet, consectetur adipiscing elit, sed do eiusmod tempor incididunt ut labore et dolore magna aliqua.",
        TextFormat.Plain |> called.Message.GetTextBody |> _.Trim()
    )

[<Fact>]
let ``Curl import with multipart/form-data works with attachments`` () =
    let config, mock = mockWithoutAuth ()

    use content = new MultipartFormDataContent()
    content.Add(makeTextContent "Hello, World!", "body")
    content.Add(makeContent "application/json" "{\"hello\": \"world\"}", "upload", "hello.json")
    content.Add(makeContent "text/html" "<p>hello <strong>world</strong></p>", "upload2", "hello.html")

    let request = new HttpRequestMessage(HttpMethod.Post, "/api/gmail/messages/import")
    request.Content <- content

    let response = testRequest config request
    Assert.Equal(HttpStatusCode.OK, response.StatusCode)

    let called = mock.CalledImport.Value
    Assert.Equal("Hello, World!", TextFormat.Plain |> called.Message.GetTextBody |> _.Trim())

    let attachments = mock.CalledImport.Value.Message.Attachments |> Seq.toList
    Assert.Equal(attachments.Length, 2)
    Assert.Equal("application/json", attachments[0].ContentType.MimeType)
    Assert.Equal("hello.json", attachments[0].ContentType.Name)
    Assert.Equal("text/html", attachments[1].ContentType.MimeType)
    Assert.Equal("hello.html", attachments[1].ContentType.Name)

    Assert.Contains(
        "{\"hello\": \"world\"}" |> Encoding.UTF8.GetBytes |> Convert.ToBase64String,
        attachments[0] |> readEntity |> Encoding.UTF8.GetString
    )

    Assert.Contains(
        "<p>hello <strong>world</strong></p>"
        |> Encoding.UTF8.GetBytes
        |> Convert.ToBase64String,
        attachments[1] |> readEntity |> Encoding.UTF8.GetString
    )

[<Fact>]
let ``Curl import with multipart/form-data works (sort of) with non-plain body`` () =
    let config, mock = mockWithoutAuth ()

    use content = new MultipartFormDataContent()
    content.Add(makeContent "text/html" "Hello, <em>World!</em>", "body")

    let request = new HttpRequestMessage(HttpMethod.Post, "/api/gmail/messages/import")
    request.Content <- content

    let response = testRequest config request
    Assert.Equal(HttpStatusCode.OK, response.StatusCode)

    let called = mock.CalledImport.Value
    // This would ideally be text/html as originally submitted, but due to
    // limitations in ASP.NET's form-parsing API, that would require a custom
    // parser like https://andrewlock.net/reading-json-and-binary-data-from-multipart-form-data-sections-in-aspnetcore/.
    // Too much work--for now we're not concerned with this niche case.
    Assert.Equal("Hello, <em>World!</em>", TextFormat.Text |> called.Message.GetTextBody |> _.Trim())

[<Fact>]
let ``Curl import passes through headers`` () =
    let config, mock = mockWithoutAuth ()

    use content = new MultipartFormDataContent()
    content.Add(makeTextContent "Hello, World!", "body")

    let request = new HttpRequestMessage(HttpMethod.Post, "/api/gmail/messages/import")
    request.Content <- content
    request.Headers.Add("To", "bob@example.com")
    request.Headers.Add("Subject", "Hello, World!")

    let response = testRequest config request
    Assert.Equal(HttpStatusCode.OK, response.StatusCode)

    let called = mock.CalledImport.Value
    Assert.Equal("bob@example.com", called.Message.To.ToString())
    Assert.Equal("Hello, World!", called.Message.Subject)
