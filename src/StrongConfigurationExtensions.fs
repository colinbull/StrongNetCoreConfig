[<AutoOpen>]
module ConfigurationExtensions 

    open System
    open System.IO
    open System.Runtime.CompilerServices
    open System.Collections.Concurrent
    open Microsoft.Extensions.Primitives
    open Microsoft.Extensions.Configuration
    open Microsoft.AspNetCore.Hosting

    type IConfiguration with 

        [<Extension>]
        member x.RegisterConfigChange(onChange : IHostingEnvironment -> string -> unit, env:IHostingEnvironment) = 
            let fileHashDict = new ConcurrentDictionary<_,_>()

            let getFileHash (fileName:string) = 
                use file = File.OpenRead(fileName)
                let sha1 = System.Security.Cryptography.SHA1.Create()
                sha1.ComputeHash(file)

            let reloadConfigOnChange f = 
                fun (env:IHostingEnvironment) -> 
                    for (KeyValue(path,hash)) in fileHashDict do 
                        let newHash = getFileHash(path)
                        if hash = newHash
                        then ()
                        else 
                            fileHashDict.[path] <- newHash
                            f env path
            
            [
                Path.Combine(env.ContentRootPath, "appsettings.json")
                Path.Combine(env.ContentRootPath, sprintf "appsettings.%s.json" env.EnvironmentName)
            ] |> List.iter (fun x -> fileHashDict.[x] <- [||])
                
            ChangeToken.OnChange(
                new Func<_>(fun _ -> x.GetReloadToken()), 
                Action<_>(reloadConfigOnChange onChange),
                env
            )  |> ignore