namespace Htsign.WebSuggestionWrappers

open System
open System.Net
open System.Net.Http
open System.Threading
open System.Web

open Microsoft.AspNetCore.Http
open Microsoft.AspNetCore.Mvc
open Microsoft.Azure.WebJobs
open Microsoft.Azure.WebJobs.Extensions.Http
open Microsoft.Extensions.Logging

open Newtonsoft.Json
open Newtonsoft.Json.Linq

module Suggest =
    let private getKey (req : HttpRequest) name =
        if req.Query.ContainsKey name then
            Ok (string req.Query.[name])
        else
            Error (BadRequestObjectResult(sprintf "param '%s' is requred" name) :> IActionResult)

    let private ok x = JsonConvert.SerializeObject x |> OkObjectResult :> IActionResult
    let private encode : string -> _ = HttpUtility.UrlEncode

    let private client =
        let compressionMethods = DecompressionMethods.GZip ||| DecompressionMethods.Deflate
        let handler = new HttpClientHandler(AutomaticDecompression=compressionMethods)
        new HttpClient(handler)

    let private fetchAsync (url : Uri) = client.GetStringAsync url |> Async.AwaitTask

    [<FunctionName("booklive")>]
    let booklive ([<HttpTrigger(AuthorizationLevel.Anonymous, "get")>] req : HttpRequest) (log : ILogger) =
        let host = Uri("https://booklive.jp/")
        let parseDocument (html : string) =
            let parser = AngleSharp.Html.Parser.HtmlParser()
            parser.ParseDocumentAsync(html, CancellationToken.None) |> Async.AwaitTask

        async {
            match getKey req "q" with
            | Error result -> return result
            | Ok query ->
                let url = Uri(host, sprintf "/json/suggest?keyword=%s" <| encode query)
                log.LogDebug("url = {URL}", url)
                
                let! data = fetchAsync url
                let! doc =
                    JsonConvert.DeserializeObject<JObject> data
                    |> Seq.find (string >> (=) "html")
                    |> sprintf "<ul>%O</ul>"
                    |> parseDocument
                let listItems = doc.GetElementsByTagName "li"

                let titles = listItems |> Seq.map (fun li -> li.QuerySelector("p.title_name").TextContent)
                let descs = listItems |> Seq.map (fun li -> li.QuerySelector("p.category_name").TextContent)
                let urls = listItems |> Seq.map (fun li -> Uri(host, li.QuerySelector("a").GetAttribute("href")).AbsoluteUri)

                return ok [box query; box titles; box descs; box urls]
        } |> Async.StartAsTask

    [<FunctionName("niconico")>]
    let niconico ([<HttpTrigger(AuthorizationLevel.Anonymous, "get")>] req : HttpRequest) (log : ILogger) =
        let createRequestUrl =
            encode >> String.toUpper >> String.replace ("+", "%20")
            >> sprintf "https://sug.search.nicovideo.jp/suggestion/expand/%s"
        let buildLinkWithQuery = encode >> sprintf "https://www.nicovideo.jp/search/%s"

        async {
            match getKey req "q" with
            | Error result -> return result
            | Ok query ->
                let url = createRequestUrl query
                log.LogDebug("url = {URL}", url)

                let! data = fetchAsync (Uri url)
                let converted =
                    let data = JsonConvert.DeserializeObject<JObject> data

                    data.["candidates"] :?> JArray
                    |> Seq.map (string >> flip List.apply [id; const_ ""; buildLinkWithQuery])
                    |> Seq.transpose
                
                return ok <| seq { yield box query; yield! converted |> Seq.map box }
        } |> Async.StartAsTask