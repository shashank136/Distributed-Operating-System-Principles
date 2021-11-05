#if INTERACTIVE
#r "nuget: Akka.FSharp" 
#r "nuget: Akka.TestKit" 
#endif

open System
open Akka.Actor
open Akka.Configuration
open Akka.FSharp
open System.Collections.Generic

type Gossip =
    |Initialize of IActorRef[]
    |BeginGossip of String
    |GossipResult of String
    |BeginPushSum of Double
    |EvaluatePushSum of Double * Double * Double
    |PushSumResult of Double * Double
    |StartTimer of int
    |NumParticipants of int
    |RemoveNode of IActorRef

let cubeRoot (base_ : bigint) n =
    if base_ < bigint.Zero || n <= 0 then
        raise (ArgumentException "Bad parameter")
 
    let n1 = n - 1
    let n2 = bigint n
    let n3 = bigint n1
    let mutable c = bigint.One
    let mutable d = (n3 + base_) / n2
    let mutable e = ((n3 * d) + (base_ / bigint.Pow(d, n1))) / n2
    while c <> d && c <> e do
        c <- d
        d <- e
        e <- (n3 * e + base_ / bigint.Pow(e, n1)) / n2
 
    if d < e then
        d
    else
        e

let rand = System.Random()

// let mutable unactiveNodes = Set.empty<IActorRef>
// let nodeStatus = new Dictionary<IActorRef, bool>()
// let cuberoot = cubeRoot (bigint N) k

let isValid(x: int, y: int, z: int, size: int, edge: int) =
    let tempKey = (x + edge*(y + edge*z))
    // printfn "validating: %A for %A" tempKey size
    tempKey < size && tempKey >=0

// isValid(2,2,3,27,3)


type Handler() =
    inherit Actor()
    let mutable rmessages = 0
    let mutable numParticipants = 0
    let stopwatch = System.Diagnostics.Stopwatch()

    override x.OnReceive(msgReceived) = 
        match msgReceived :?>Gossip with 
        | GossipResult message ->
            rmessages <- rmessages + 1
            // printfn "message received by: %A total: %A" x.Sender rmessages
            if rmessages = numParticipants then
                stopwatch.Stop()
                printfn "Convergence Time: %f ms" stopwatch.Elapsed.TotalMilliseconds
                Environment.Exit(0)  
        
        | PushSumResult (sum,weight) ->
            stopwatch.Stop()
            printfn "Convergence Time: %f ms" stopwatch.Elapsed.TotalMilliseconds
            Environment.Exit(0)

        | StartTimer x ->
            stopwatch.Start()

        | NumParticipants n ->
            numParticipants <- n
            // printfn "no. of nodes: %A\n" numParticipants
        | _->()
 
type Node(handler: IActorRef, nodeID: int) =
    inherit Actor()
    let mutable heardMsgs = 0 
    let mutable  neighbours:IActorRef[]=[||]

    //used for push sum
    let mutable sum = nodeID |> float
    let mutable weight = 1.0
    let mutable temp = 1    
 
    override x.OnReceive(number)=
         match number :?>Gossip with 
         | Initialize i ->
                neighbours <- i

         | BeginGossip message ->
                
                heardMsgs <- heardMsgs + 1
                // printfn "%A received %A msgs %A" x.Self heardMsgs neighbours.Length
                if heardMsgs < 10 then
                    if neighbours.Length > 0 then
                        let key = rand.Next(0, neighbours.Length)
                        neighbours.[key] <! BeginGossip(message)
                
                else if heardMsgs = 10 then
                    // printfn "%A has %A neighbours" x.Self neighbours.Length
                    for neighbour in neighbours do
                        neighbour <! RemoveNode(x.Self)

                    if neighbours.Length > 0 then
                        let key = rand.Next(0, neighbours.Length)
                        neighbours.[key] <! BeginGossip(message)

                    handler <! GossipResult(message)

                x.Self <! BeginGossip(message)
                
         
         | RemoveNode deadNode ->
                neighbours <- neighbours|>Array.filter( fun node -> node <> deadNode)
                // printfn "\n%A: neighbours: %A\n" x.Self neighbours


         | BeginPushSum n -> 
                        sum<- sum/2.0
                        weight <-weight/2.0
                        neighbours.[(rand.Next(0,neighbours.Length))] <! EvaluatePushSum(sum,weight,n)

         | EvaluatePushSum (s:float,w,n) -> 
                          let  latestSum = sum+s
                          let latestWeight = weight + w
                          let ans = sum/weight - latestSum/latestWeight |> abs
                          if(ans >n) then
                            temp<- 0
                            sum <- sum+s
                            weight <- weight + w
                            sum <- sum/2.0
                            weight <- weight/2.0
                            neighbours.[(rand.Next(0,neighbours.Length))] <! EvaluatePushSum(sum,weight,n)
                           elif (temp>=3) then
                             handler<! PushSumResult(sum,weight)
                            else
                               sum<- sum/2.0
                               weight <- weight/2.0
                               temp<- temp+1
                               neighbours.[(rand.Next(0,neighbours.Length))] <! EvaluatePushSum(sum,weight,n)


         | _-> ()


let mutable numNodes = int(string (fsi.CommandLineArgs.GetValue 1))
let topology = string (fsi.CommandLineArgs.GetValue 2)
let protocol= string (fsi.CommandLineArgs.GetValue 3)

let system = ActorSystem.Create("System")

let mutable totalNodes=float(numNodes)

let handler=system.ActorOf(Props.Create(typeof<Handler>),"handler")

match topology  with 
      | "full"->
          let arr= Array.zeroCreate (numNodes+1)
          for i in [0..numNodes] do
              arr.[i]<-system.ActorOf(Props.Create(typeof<Node>,handler,i+1),""+string(i))
            //   nodeStatus.Add(arr.[i], false)
          for i in [0..numNodes] do
              arr.[i]<!Initialize(arr)
              
          let rand = rand.Next(0,numNodes)
          if protocol="gossip" then
            handler<!NumParticipants(numNodes)
            handler<!StartTimer(1)
            arr.[rand]<!BeginGossip("full")
          else if protocol="push-sum" then
            handler<!StartTimer(1)
            arr.[rand]<!BeginPushSum(10.0 ** -10.0)
        
      |"line"->
          let arr= Array.zeroCreate (numNodes)
          for i in [0..numNodes-1] do
              arr.[i]<-system.ActorOf(Props.Create(typeof<Node>,handler,i+1),""+string(i))
            //   nodeStatus.Add(arr.[i], false)
          for i in [0..numNodes-1] do
              if(i = 0) then
                let neighbourArray=[|arr.[1]|]
                arr.[i]<!Initialize(neighbourArray)
              else if(i = numNodes - 1) then
                let neighbourArray=[|arr.[numNodes-2]|]
                arr.[i]<!Initialize(neighbourArray)
              else
                let neighbourArray=[|arr.[(i-1)];arr.[(i+1)]|]
                arr.[i]<!Initialize(neighbourArray)
          
          let rand = rand.Next(0,numNodes)
          
          if protocol="gossip" then
            handler<!NumParticipants(numNodes)
            handler<!StartTimer(1)
            arr.[rand]<!BeginGossip("line")
          
          else if protocol="push-sum" then
            handler<!StartTimer(1)
            arr.[rand]<!BeginPushSum(10.0 ** -10.0)

      |"3D"->
           let gridSize=cubeRoot (bigint totalNodes) 3 |> int//int(ceil(sqrt totalNodes)) // side of cube
           let totalGrid=gridSize*gridSize*gridSize |> int// total no. of node in cube
           // update number of nodes to match the number as per 3D arrangement
           handler<!NumParticipants(totalGrid)
        //    printfn "size: %A, total node in grid: %A" gridSize totalGrid
           let arr= Array.zeroCreate ((int totalGrid))
           for i in [0..totalGrid-1] do
              arr.[i]<-system.ActorOf(Props.Create(typeof<Node>,handler,i+1),""+string(i))
            //   nodeStatus.Add(arr.[i], false)
           
        //    printfn "%A" arr
           for x in [0..gridSize-1] do
                for y in [0..gridSize-1] do
                    for z in [0..gridSize-1] do
                        let k = (x + gridSize*(y + gridSize*z))
                        // printfn "Searching for %A %A %A --> k: %A" x y z k
                        if(k < totalGrid) then
                            let dx = [|0; 0; 1; -1; 0; 0|]
                            let dy = [|1; -1; 0; 0; 0; 0|]
                            let dz = [|0; 0; 0; 0; 1; -1|]
                            let mutable neighbours:IActorRef[]=[||]
                            for i in 0..dx.Length-1 do
                                let x1 = x+(Array.get dx i)
                                let y1 = y+(Array.get dy i)
                                let z1 = z+(Array.get dz i)
                                if(x1>=0 && x1<gridSize && y1>=0 && y1<gridSize && z1>=0 && z1<gridSize && isValid(x1, y1, z1, totalGrid, gridSize)) then
                                    neighbours<-(Array.append neighbours [|arr.[(x1 + gridSize*(y1 + gridSize*z1))]|])
                            // printfn "%A: has %A" k neighbours.Length
                            arr.[k]<!Initialize(neighbours)
           let rand = rand.Next(0,totalGrid)  
        //    printfn "Message received by: %A\n" arr.[rand]
           if protocol="gossip" then
            handler<!NumParticipants(totalGrid)
            handler<!StartTimer(1)
            arr.[rand]<!BeginGossip("3D")
           else if protocol="push-sum" then
            handler<!StartTimer(1)
            arr.[rand]<!BeginPushSum(10.0 ** -10.0)

       |"imp3D" ->
           let gridSize=cubeRoot (bigint totalNodes) 3 |> int //int(ceil(sqrt totalNodes)) // side of cube
           let totalGrid=gridSize*gridSize*gridSize |> int// total no. of node in cube
           handler<!NumParticipants(totalGrid)
           let arr= Array.zeroCreate (totalGrid)
           for i in [0..totalGrid-1] do
              arr.[i]<-system.ActorOf(Props.Create(typeof<Node>,handler,i+1),""+string(i))
            //   nodeStatus.Add(arr.[i], false)

           for x in [0..gridSize-1] do
               for y in [0..gridSize-1] do
                   for z in [0..gridSize-1] do
                       let k = (x + gridSize*(y + gridSize*z))
                       if(k < totalGrid) then
                            let dx = [|0; 0; 1; -1; 0; 0|]
                            let dy = [|1; -1; 0; 0; 0; 0|]
                            let dz = [|0; 0; 0; 0; 1; -1|]
                            let mutable neighbours:IActorRef[]=[||]
                            for i in 0..dx.Length-1 do
                                let x1 = x+(Array.get dx i)
                                let y1 = y+(Array.get dy i)
                                let z1 = z+(Array.get dz i)
                                if(isValid(x1, y1, z1, totalGrid, gridSize)) then
                                    neighbours<-(Array.append neighbours [|arr.[(x1 + gridSize*(y1 + gridSize*z1))]|])
                            let mutable added = false
                            while(not added) do
                                let r = rand.Next(0,totalGrid)
                                if(r <> k && r <> k-1 && r <> k-gridSize && r <> k+1 && r <> k+gridSize) then
                                    neighbours<-(Array.append neighbours [|arr.[r]|])
                                    added <- true
                            arr.[k]<!Initialize(neighbours)

           let rand = rand.Next(0,gridSize)  
           if protocol="gossip" then
            handler<!NumParticipants(gridSize)
            handler<!StartTimer(1)
            arr.[rand]<!BeginGossip("imp3D")
           else if protocol="push-sum" then
            handler<!StartTimer(1)
            arr.[rand]<!BeginPushSum(10.0 ** -10.0)
      | _-> ()
System.Console.ReadLine()|>ignore