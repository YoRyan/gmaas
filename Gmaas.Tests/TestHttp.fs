module Tests.Http

open System.Net
open System.Net.Http
open System.Text
open System.Threading.Tasks

open Giraffe
open Google.Apis.Gmail.v1
open Microsoft.AspNetCore.Builder
open Microsoft.AspNetCore.TestHost
open Microsoft.Extensions.Hosting
open Xunit

open Gmaas.Config
open Gmaas.Gmail
open Gmaas.Helpers
open Gmaas.Http

type private MockGmailFs() =
    member val CalledMessage: Message option = None with get, set

    interface IGmailFs with
        member this.Import(msg: Message) : Task<Data.Message> =
            this.CalledMessage <- Some msg
            let whatever = Data.Message()
            Task.FromResult whatever

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
    let mock = MockGmailFs()

    let config =
        { Htpasswd = None
          AuthGmailInsert = Set.empty
          AuthGmailSend = Set.empty
          AppriseMiddleware = []
          ShoutrrrMiddleware = []
          HttpAddress = ""
          Gmail = mock }

    config, mock

[<Fact>]
let ``Easy curl import works`` () =
    let config, mock = mockWithoutAuth ()

    use content =
        new ByteArrayContent(
            "Lorem ipsum dolor sit amet, consectetur adipiscing elit, sed do eiusmod tempor incididunt ut labore et dolore magna aliqua."
            |> Encoding.UTF8.GetBytes
        )

    let request = new HttpRequestMessage(HttpMethod.Post, "/api/messages/import/ez")
    request.Content <- content

    let response = testRequest config request
    Assert.Equal(HttpStatusCode.OK, response.StatusCode)

    Assert.Equal(
        "Lorem ipsum dolor sit amet, consectetur adipiscing elit, sed do eiusmod tempor incididunt ut labore et dolore magna aliqua.",
        match mock.CalledMessage.Value.Body with
        | SinglePart s -> s
        | MultiPart(_ct, c, _a) -> c
    )

    Assert.Equivalent([ "text/plain" ], getHeader "Content-Type" mock.CalledMessage.Value.Headers)

[<Fact>]
let ``Easy curl import passes through headers`` () =
    let config, mock = mockWithoutAuth ()
    let request = new HttpRequestMessage(HttpMethod.Post, "/api/messages/import/ez")
    request.Headers.Add("To", "bob@example.com")
    request.Headers.Add("Subject", "Hello, World!")

    let response = testRequest config request
    Assert.Equal(HttpStatusCode.OK, response.StatusCode)
    Assert.Equivalent([ "Hello, World!" ], getHeader "Subject" mock.CalledMessage.Value.Headers)

[<Fact>]
let ``Easy curl import passes through Content-Type`` () =
    let config, mock = mockWithoutAuth ()
    let request = new HttpRequestMessage(HttpMethod.Post, "/api/messages/import/ez")
    request.Headers.Add("To", "bob@example.com")

    use content =
        new ByteArrayContent("Now we have <strong>HTML</strong>!" |> Encoding.UTF8.GetBytes)

    content.Headers.ContentType <- Headers.MediaTypeHeaderValue "text/html"
    request.Content <- content

    let response = testRequest config request
    Assert.Equal(HttpStatusCode.OK, response.StatusCode)
    Assert.Equivalent([ "text/html" ], getHeader "Content-Type" mock.CalledMessage.Value.Headers)
