#time "on"
#r "nuget: Akka.FSharp" 
#r "nuget: Akka.Remote"
open System
open Akka.Actor
open Akka.Configuration
open Akka.FSharp
open System.Security.Cryptography

open System.Net.NetworkInformation
open System.Net
open System.Net.Sockets

// get the ip address of remote machine
let localIpAddress =
    let networkInterfaces =
        NetworkInterface.GetAllNetworkInterfaces()
        |> Array.filter (fun iface -> iface.OperationalStatus.Equals(OperationalStatus.Up))

    let addresses =
        seq {
            for iface in networkInterfaces do
                for unicastAddr in iface.GetIPProperties().UnicastAddresses do
                    yield unicastAddr.Address
        }

    addresses
    |> Seq.filter (fun addr -> addr.AddressFamily.Equals(AddressFamily.InterNetwork))
    |> Seq.filter (IPAddress.IsLoopback >> not)
    |> Seq.head

let localhost = localIpAddress.ToString()

let configuration = 
    ConfigurationFactory.ParseString(
        @"akka {
            actor {
                provider = ""Akka.Remote.RemoteActorRefProvider, Akka.Remote""
                debug : {
                    receive : on
                    autoreceive : on
                    lifecycle : on
                    event-stream : on
                    unhandled : on
                }
            }
            loglevel = ""OFF""
            remote {
                helios.tcp {
                    port = 9001
                    hostname = "+localhost+"
                }
            }            
        }")


// read the command line argument for number of zeros in the hash of a string
let k = fsi.CommandLineArgs.[1] |> int
// no. of workers under a supervisor
let workers = System.Environment.ProcessorCount |> int64
// Random string length
let strLength = 15
let workLoad = 1000
let mutable ref = null

// random string generator
let ranStr() = 
    let r = Random()
    let chars = Array.concat([[|'a' .. 'z'|];[|'A' .. 'Z'|];[|'0' .. '9'|]])
    let sz = Array.length chars in
    String(Array.init strLength (fun _ -> chars.[r.Next sz]))

// calculate SHA-256 hash of strings
let getSHA256Encoding(str: string) = 
    System.Text.Encoding.ASCII.GetBytes(str) 
    |> (SHA256.Create()).ComputeHash
    |> Array.map (fun (x : byte) -> System.String.Format("{0:x2}", x))
    |> String.concat String.Empty

// validate if the hash of the string contains the required pattern
let validateHash(hash: string) =
    let index = hash |> Seq.tryFindIndex (fun x -> x <> '0')
    index.IsSome && index.Value = k

let miner = "shashankk.hayyal;"

// generates strings for coordinator to pass on to workers
let getStrings(num: int) = 
    List.init num (fun _ -> miner+ranStr())


// Messages that Worker and Coordinator exchanges between themself
type Messages =
    | InitiateWorker
    | RequestJob
    | Job of int
    | Success of string*string
    | InitiateCoordinator
    | RemoteJobRequest of IActorRef
    | RemoteSuccess of string*string*IActorRef
    | Completed

//creating an actor system
let system = ActorSystem.Create("RemoteFSharp", configuration)

// template for worker actor 
let worker (mailbox:Actor<_>)=
    let rec loop() = 
        actor{
            let! msg = mailbox.Receive()
            match msg with
            | InitiateWorker -> 
                                mailbox.Sender()<! RequestJob
            | Job(num) -> 
                            let mutable flag = false
                            let mutable i=0
                            while (i<num && not flag) do
                                let str = miner+ranStr()
                                let hash = getSHA256Encoding(str)
                                if validateHash(hash) then
                                    flag <- true
                                    mailbox.Sender() <! Success(str, hash)
                                i<-i+1
                            if not flag then
                                mailbox.Sender() <! RequestJob
            | _ -> printfn "Worker Received Wrong message"
            return! loop()
        }
    loop()

// list of workers working remotly
let mutable listOfRemoteWorkers : IActorRef list = []

// template for Coordinator, it's responsible for distributing the jobs to workers
let Coordinator (mailbox:Actor<_>)=
    let mutable stringFound: bool = false;
    let rec loop() = actor{        
        let! msg = mailbox.Receive()
        match msg with
        | InitiateCoordinator -> 
                                    let workersList = [for i in 1L .. workers do yield(spawn system ("Worker_"+(string i)) worker)]
                                    for i in 0L .. (workers-1L) do
                                        workersList.Item(i|>int) <! InitiateWorker
        | RequestJob -> 
                            if stringFound then
                                mailbox.Context.System.Terminate() |> ignore// terminate the coordinator
                            else
                                mailbox.Sender() <! Job(workLoad) // send the worker the new string to process
        | Success(str, hash) ->
                        stringFound <- true
                        printfn "%A\n%A %A" k str hash
                        listOfRemoteWorkers |> List.iter (fun remoteWorker ->
                            remoteWorker <! "Completed"
                        )
                        mailbox.Context.System.Terminate() |> ignore
        | RemoteJobRequest(remoteWorker) ->
                                remoteWorker <! "Process,"+(string k)+",shashankk.hayyal;"
        | RemoteSuccess(str, hash, remoteWorker) ->
                                                    stringFound <- true
                                                    printfn "%A\n%A %A" k str hash
                                                    listOfRemoteWorkers |> List.iter (fun remoteWorker ->
                                                        remoteWorker <! "Completed"
                                                    )
                                                    mailbox.Context.System.Terminate() |> ignore


        | _ -> printfn "Coordinator Received Wrong message"  
        return! loop()     
    }
    loop()

let CoordinatorRef = spawn system "Coordinator" Coordinator

let commlink = 
    spawn system "server"
    <| fun mailbox ->
        let rec loop() =
            actor {
                let! msg = mailbox.Receive()
                ref <- mailbox.Sender()
                printfn "%s" msg 
                let message = msg.Split ','
                if msg.CompareTo("RemoteRequestJob")=0 then
                    listOfRemoteWorkers <- mailbox.Sender() :: listOfRemoteWorkers
                    printfn "Received remote job request from %A" (mailbox.Sender().ToString())                 
                    CoordinatorRef <! RemoteJobRequest(mailbox.Sender())                  
                else
                    printfn "Success from remote server:"
                    CoordinatorRef <! RemoteSuccess(message.[0], message.[1], mailbox.Sender())             
                return! loop() 
            }
        loop()

CoordinatorRef <! InitiateCoordinator
system.WhenTerminated.Wait()

//system.Terminate()
