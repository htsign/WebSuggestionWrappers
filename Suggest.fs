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

    let client =
        let compressionMethods = DecompressionMethods.GZip ||| DecompressionMethods.Deflate
        let handler = new HttpClientHandler(AutomaticDecompression=compressionMethods)
        new HttpClient(handler)

    [<FunctionName("booklive")>]
    let booklive ([<HttpTrigger(AuthorizationLevel.Anonymous, "get")>] req : HttpRequest) (log : ILogger) =
        let host = Uri("https://booklive.jp/")

        async {
            match getKey req "q" with
            | Error result -> return result
            | Ok query ->
                let parser = AngleSharp.Html.Parser.HtmlParser()
                let url = Uri(host, sprintf "/json/suggest?keyword=%s" <| HttpUtility.UrlEncode query)
                log.LogDebug("url = {URL}", url)
                
                let! data = client.GetStringAsync url |> Async.AwaitTask
                let! doc =
                    let data = JsonConvert.DeserializeObject<JObject> data
                    parser.ParseDocumentAsync(sprintf "<ul>%O</ul>" data.["html"], CancellationToken.None)
                    |> Async.AwaitTask
                let listItems = doc.GetElementsByTagName "li"

                let titles = listItems |> Seq.map (fun li -> li.QuerySelector("p.title_name").TextContent)
                let descs = listItems |> Seq.map (fun li -> li.QuerySelector("p.category_name").TextContent)
                let urls = listItems |> Seq.map (fun li -> Uri(host, li.QuerySelector("a").GetAttribute("href")).AbsoluteUri)

                return
                    [box query; box titles; box descs; box urls]
                    |> JsonConvert.SerializeObject
                    |> OkObjectResult :> IActionResult
        } |> Async.StartAsTask

    [<FunctionName("niconico")>]
    let niconico ([<HttpTrigger(AuthorizationLevel.Anonymous, "get")>] req : HttpRequest) (log : ILogger) =
        async {
            match getKey req "q" with
            | Error result -> return result
            | Ok query ->
                let url =
                    HttpUtility.UrlEncode query
                    |> String.toUpper
                    |> String.replace ("+", "%20")
                    |> sprintf "https://sug.search.nicovideo.jp/suggestion/expand/%s"
                log.LogDebug("url = {URL}", url)

                let! data = client.GetStringAsync(Uri url) |> Async.AwaitTask
                let converted =
                    let data = JsonConvert.DeserializeObject<JObject> data
                    data.["candidates"] :?> JArray
                    |> Seq.map string
                    |> Seq.map (fun s -> [s; ""; sprintf "https://www.nicovideo.jp/search/%s" (HttpUtility.UrlEncode s)])
                    |> Seq.transpose
                    |> Seq.map box
                
                return
                    seq { yield box query; yield! converted }
                    |> JsonConvert.SerializeObject
                    |> OkObjectResult :> IActionResult
        } |> Async.StartAsTask