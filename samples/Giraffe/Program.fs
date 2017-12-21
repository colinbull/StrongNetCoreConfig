// Learn more about F# at http://fsharp.org
module Program

open System
open System.IO
open System.Linq
open System.Collections.Generic
open System.Collections.Concurrent
open System.Runtime.CompilerServices
open Microsoft.Extensions.Primitives
open Microsoft.Extensions.FileProviders
open Microsoft.Extensions.Configuration
open Microsoft.AspNetCore
open Microsoft.AspNetCore.Builder
open Microsoft.AspNetCore.Cors.Infrastructure
open Microsoft.AspNetCore.Hosting
open Microsoft.AspNetCore.Http
open Microsoft.Extensions.Logging
open Microsoft.Extensions.DependencyInjection
open FSharp.Data
open Giraffe
open Giraffe.Razor

type CommonConfiguration = JsonProvider<"appsettings.json">
type EnvironmentConfiguration = JsonProvider<"appsettings.production.json">

type CommonConfigRoot = CommonConfiguration.Root
type EnvConfigRoot = EnvironmentConfiguration.Root


//Wrapper around our provided configuration objects  
type Configuration = 
    { Common : CommonConfigRoot
      Environment : EnvConfigRoot }
    static member Empty = 
        {
            Common = CommonConfiguration.GetSample()
            Environment = EnvironmentConfiguration.GetSample()
        }
    member x.Refresh(?common:CommonConfigRoot, ?env:EnvConfigRoot) = 
        {
           Common = defaultArg common x.Common
           Environment = defaultArg env x.Environment
        }

    static member OnChange (configRef:Configuration ref) (env:IHostingEnvironment) path  = 
        let envPath = sprintf "appsettings.%s.json" env.EnvironmentName 
        match Path.GetFileName(path) with 
        | "appsettings.json" -> 
            printfn "Updating Common config"
            configRef := ((!configRef).Refresh(common = CommonConfiguration.Load(path)))
        | a when a = envPath -> 
            printfn "Updating Environment config"
            configRef := ((!configRef).Refresh(env = EnvironmentConfiguration.Load(path)))
        | _ -> ()

let configInstance = ref Configuration.Empty

type ConfigModel = { 
    IntValue : int 
    StringValue : string 
    EnableCaching : bool
}

//Helper to inject config to handler... (optional)
let withConfig<'a> (f : 'a -> HttpHandler) : HttpHandler = 
    (fun (next:HttpFunc) (ctx:HttpContext) -> 
        task { 
            let cfg = ctx.GetService<'a>()
            return! (f cfg) next ctx
        }
    )

let webapp = 
    choose [ 
        route "/" >=> withConfig<Configuration> (fun a -> 
                        razorHtmlView "index" { 
                                              IntValue = a.Common.Test.OptionInt; 
                                              StringValue = a.Common.Test.OptionString; 
                                              EnableCaching = a.Environment.Caching.EnableCaching
                                          }) 
    ]

let errorHandler (ex : Exception) (logger : ILogger) =
    logger.LogError(EventId(), ex, "An unhandled exception has occurred while executing the request.")
    clearResponse >=> setStatusCode 500 >=> text ex.Message


let configureCors (builder : CorsPolicyBuilder) =
    builder.WithOrigins("http://localhost:8080").AllowAnyMethod().AllowAnyHeader() |> ignore

let configureApp (app : IApplicationBuilder) =
    app.UseCors(configureCors)
       .UseGiraffeErrorHandler(errorHandler)
       .UseStaticFiles()
       .UseGiraffe(webapp)

//Our global configuration instance this can obviosly be managed however

let configureServices (services : IServiceCollection) =
    let sp  = services.BuildServiceProvider()
    let env = sp.GetService<IHostingEnvironment>()

    //-------------------  Setup typed configuration
    let config = sp.GetService<IConfiguration>() 
    config.RegisterConfigChange(Configuration.OnChange configInstance,env)
    services.AddTransient<Configuration>(fun _ -> !configInstance) |> ignore

    let viewsFolderPath = Path.Combine(env.ContentRootPath, "Views")
    services.AddRazorEngine viewsFolderPath |> ignore
    services.AddCors() |> ignore

    
let configureLogging (builder : ILoggingBuilder) =
    let filter (l : LogLevel) = l.Equals LogLevel.Error
    builder.AddFilter(filter).AddConsole().AddDebug() |> ignore


[<EntryPoint>]
let main argv =

    let contentRoot = Directory.GetCurrentDirectory()
    let webRoot     = Path.Combine(contentRoot, "WebRoot")
    
    WebHost
        .CreateDefaultBuilder()
        .UseContentRoot(contentRoot)
        .UseWebRoot(webRoot)   
        .Configure(Action<IApplicationBuilder> configureApp)
        .ConfigureServices(configureServices)
        .ConfigureLogging(configureLogging)
        .Build()
        .Run()

    0 // return an integer exit code
