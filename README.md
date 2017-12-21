# Strongly type access for .NET Core for F#

True strongly type Net core configuration access. This simple library, provides a hook to use F# Type provides with .net core configuration system.

**Note** this project doesn't actually add anything that wasn't already there in the F# + netcore ecosystem. It just ties together a few concepts. 

Currently, no NuGet pacakage is available, but the actual extension is only a single file which can be referenced using paket, 

    github colinbull/StrongNetCoreConfig src/StrongConfigurationExtensions.fs

Firstly, create the type providers based on configuration samples, 
```fsharp
type CommonConfiguration = JsonProvider<"appsettings.json">
type EnvironmentConfiguration = JsonProvider<"appsettings.production.json">

type CommonConfigRoot = CommonConfiguration.Root
type EnvConfigRoot = EnvironmentConfiguration.Root
```
Optionally then we can define a wrapper object for this provided configuration. 
```fsharp
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
```
Also then we need to setup a global instance of our configuration. 
```fsharp
let configInstance = ref Configuration.Empty
```

We can register to listen for changes in the configuration, this is done as part of the ConfigureService call on the WebHostBuilder 
```fsharp
let configureServices (services : IServiceCollection) =
    let sp  = services.BuildServiceProvider()
    let env = sp.GetService<IHostingEnvironment>()

    //-------------------  Setup typed configuration
    let config = sp.GetService<IConfiguration>() 
    config.RegisterConfigChange(Configuration.OnChange configInstance,env)
```
Also at this point we can choose to transiently inject our global config instance to make it available to out Http handlers. 
```fsharp
services.AddTransient<Configuration>(fun _ -> !configInstance) |> ignore
```
As a nice touch, if you are using giraffe, you can add the following handler to resolve the configuration for you, for example, 
```fsharp
let withConfig<'a> (f : 'a -> HttpHandler) : HttpHandler = 
    (fun (next:HttpFunc) (ctx:HttpContext) -> 
        task { 
            let cfg = ctx.GetService<'a>()
            return! (f cfg) next ctx
        }
    )
```
and then you'll have access to the config in your routes. 
```fsharp
let webapp = 
    choose [ 
        route "/" >=> withConfig<Configuration> (fun a -> 
                        razorHtmlView "index" { 
                                            IntValue = a.Common.Test.OptionInt; 
                                            StringValue = a.Common.Test.OptionString; 
                                            EnableCaching = a.Environment.Caching.EnableCaching
                                        }) 
    ]
```
See the [samples](samples) for more info. 

