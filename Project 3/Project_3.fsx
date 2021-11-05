#time "on"
#r "nuget: Akka"
#r "nuget: Akka.FSharp"
#r "nuget: Akka.Remote"

open Akka.FSharp
open System
open System.Threading
open System.Security.Cryptography


type NodeMessage = 
    | PeerJoin of numBits : int * n : bigint * numOfRequests : int
    | FetchSuccessor of peerNode : bigint * id : bigint * condition : string * key : int * hops : int
    | KeyLookUpSuccess of peerId : bigint * hops : int
    | NodeZero of numBits : int * n : bigint * numOfRequests : int
    | NetworkPush of bigint
    | Update of bigint
    | UpdateNetwork of bigint
    | UpdateSuccessorNode of bigint
    | ForwardPredecessor
    | UpdateFingerTable
    | FingerTableUpdate
    
type MainMessage =
    | Parameters of noOfPeers : int * noOfRequests : int
    | UpdateHopsCount of totalHops : int
    | Output

// truncate SHA1 hash to  big int
let hashToInt (bytarr: byte[]) : bigint =
    let mutable multiplier = bigint(1)
    let mutable two = bigint(2)
    let mutable res = bigint(0)
    for i in bytarr.Length - 1..-1..0 do
        let mutable byt = int(bytarr.[i])
        for j in 1..8 do
            let bit = bigint(byt &&& 1)
            res <- res + (bit * multiplier)
            multiplier <- multiplier * two
            byt <- byt >>> 1
    res

let getIds (num : int) : bigint[] =
    Array.init num (fun idx -> 
        let b: byte[] = Array.zeroCreate 20
        Random().NextBytes(b)
        let shaHash = SHA1Managed.Create().ComputeHash(b)
        hashToInt(shaHash)
    )


let temp = bigint(-100)


let system = System.create "local-system" <| Configuration.load ()
let mutable keepAlive = true

let Boss (mailbox: Actor<_>) = 
    let mutable currenthop = 0
    let mutable received = 0

    let mutable totalRequests = 0
    let mutable totalPeers = 0


    let rec loop() = actor{
        let! message = mailbox.Receive()
        match message with
        
        | Parameters(peers, requests) ->
            totalRequests <- requests
            totalPeers <- peers

        | Output ->
            let res = double(currenthop) / double(totalPeers * totalRequests)
            printfn "Average number of hops needed for successful lookup : %A" ((res*(double 10))|> int)
            keepAlive <- false    

        | UpdateHopsCount(hops)->
            received <- received + 1
            currenthop <- currenthop + hops
            if received = totalPeers then
                system.ActorSelection("akka://local-system/user/Boss") <! Output        
        
        return! loop()
    }
    loop()

let peer (mailbox: Actor<_>) = 
    let mutable next = 0
    let mutable fingerTable : bigint[] = Array.empty
    let mutable predecessor = bigint(-1)
    let mutable successor = bigint(-1)
    let mutable n = bigint(-1)
    let mutable m = bigint(-1)
    let mutable hashLength = 0
    let stabilizationCancellationToken = new CancellationTokenSource()
    let mutable totalNumHops = 0
    let mutable totalRequests = 0  

    let rec loop() = actor{
        let! message = mailbox.Receive()
        let sender = mailbox.Sender()
        
        match message with        

        | NetworkPush(chordPeer) ->
            system.ActorSelection("akka://local-system/user/" + string(chordPeer)) <! FetchSuccessor(n, n, "push", 0, 0)
        
        | UpdateSuccessorNode(peerID) ->
            successor <- peerID

        | PeerJoin(numBits, num, numOfRequests) ->
            totalRequests <- numOfRequests
            hashLength <- numBits

            n <- num
            m <- bigint(2) ** numBits
            
            fingerTable <- [|for i in 1 .. hashLength -> temp|]           
            
            successor <- temp
            predecessor <- temp
            
        | ForwardPredecessor ->
            if successor <> temp && predecessor <> temp then
                system.ActorSelection("akka://local-system/user/" + string(successor)) <! Update(predecessor)

        | Update(predecessor) ->
            let mutable updated = false
            //pred E (n, succ)
            if successor <= successor then
                updated <- (predecessor > n) || (predecessor < successor)
            else
                updated <- (predecessor > n) && (predecessor < successor)

            if updated then
                successor <- predecessor

            system.ActorSelection("akka://local-system/user/" + string(successor)) <! UpdateNetwork(n)

        | UpdateNetwork(peer) ->
            let mutable changePredecessor = false
            if predecessor = n then
               changePredecessor <- true
            elif predecessor = temp then 
                changePredecessor <- true
            else
                if predecessor >= n then
                    changePredecessor <- (peer > predecessor) || (peer < n)
                else
                    changePredecessor <- (peer > predecessor) && (peer < n)
            if changePredecessor then predecessor <- peer

        | FetchSuccessor(peerNode, currentNode, condition, fingerIdx, hops) ->
            // id E (n, succ)
            let mutable found = false
            if n >= successor then
                found <- (currentNode > n) || (currentNode <= successor)
            else 
                found <- (currentNode > n) && (currentNode <= successor)

            if found then
                let sender = system.ActorSelection("akka://local-system/user/" + string(peerNode))

                match condition with
                | "push" -> sender <! UpdateSuccessorNode(successor)
                | "update" -> fingerTable.[fingerIdx - 1] <- successor //UpdateFingerSuccessor(successor, fingerIdx)
                | "find" -> sender <! KeyLookUpSuccess(successor, hops)
                | _ -> ()
                

            else
                let mutable tableUpdate = true
                let mutable hashIndex = hashLength - 1
                let mutable nextSucc = n

                while tableUpdate do
                    let succ = fingerTable.[hashIndex]
                    if succ <> temp then
                        //succ E (n, id)
                        if n >= currentNode then
                            if (((succ < n) && (succ < currentNode)) || (succ >n))  then
                                nextSucc <- succ
                                tableUpdate <- false                                   
                        elif (succ > n) && (succ < currentNode) then
                            nextSucc <- succ
                            tableUpdate <- false          
                    hashIndex <- hashIndex - 1
                    if hashIndex = -1 then tableUpdate <- false        
                
                system.ActorSelection("akka://local-system/user/" + string(nextSucc)) <! FetchSuccessor(peerNode, currentNode, condition, fingerIdx, hops + 1)

        | UpdateFingerTable ->
            if successor <> temp then
                next <- next + 1
                if next > hashLength then
                    next <- 1

                let mutable key = (n + (bigint(2) ** (next - 1))) % m
                system.ActorSelection("akka://local-system/user/" + string(n)) <! FetchSuccessor(n, key, "update", next, 0)

       
        | FingerTableUpdate ->
            let stabilize = async{
                while true do
                    let x = system.ActorSelection("akka://local-system/user/" + string(n))
                    x <! ForwardPredecessor
                    x <! UpdateFingerTable
                    do! Async.Sleep 1000
            }

            Async.Start(stabilize, stabilizationCancellationToken.Token)
    
        | KeyLookUpSuccess(peerId, hops) ->
            totalNumHops <- totalNumHops + hops
            totalRequests <- totalRequests - 1
            if totalRequests = 0 then
                system.ActorSelection("akka://local-system/user/Boss") <! UpdateHopsCount(totalNumHops)

        | NodeZero(numBits, num, numOfRequests) ->
            
            totalRequests <- numOfRequests
            hashLength <- numBits
            
            n <- num
            m <- bigint(2) ** numBits
            
            fingerTable <- [|for i in 1 .. hashLength -> temp|]
            
            predecessor <- n
            successor <- n
        
        return! loop()
    }
    loop()


let numOfReq = int(fsi.CommandLineArgs.[2])
let numOfPeers = int(fsi.CommandLineArgs.[1])

let bossActor =  spawn system "Boss" <| Boss 
bossActor <! Parameters(numOfPeers, numOfReq)

let mutable peers = []

let isStable = new CancellationTokenSource()
let stabilize = async{ 
    while true do
        for peer in peers do
            system.ActorSelection("akka://local-system/user/" + peer) <! ForwardPredecessor  
        do! Async.Sleep 50
}


let isConverged = new CancellationTokenSource()
let updtFinger = async{
   
    while true do
        for peer in peers do
            system.ActorSelection("akka://local-system/user/" + peer) <! UpdateFingerTable
        do! Async.Sleep 50
}

Async.Start(stabilize, isStable.Token)
Async.Start(updtFinger, isConverged.Token)


let mutable firstNode = temp

let addWorkerNode (peerId : bigint) = 
    let newPeer = spawn system (string(peerId)) <| peer
    peers <- List.append peers [string(peerId)]
    newPeer <! PeerJoin(160, peerId, numOfReq)
    newPeer <! NetworkPush(firstNode)

let findKey peerId key  = 
    system.ActorSelection("akka://local-system/user/" + string(peerId)) <! FetchSuccessor(peerId, key, "find", 0, 0)


let newWorkers = getIds numOfPeers
firstNode <- newWorkers.[0] 
peers <- List.append peers [string(firstNode)]
(spawn system (string(firstNode)) <| peer)<! NodeZero(160, firstNode, numOfReq)     


for i in 1 .. newWorkers.Length-1 do
    addWorkerNode newWorkers.[i]

let requests = getIds numOfReq

for worker in newWorkers do
    let workerActor = system.ActorSelection("akka://local-system/user/" + string(worker))
    for y in requests do
        workerActor <! FetchSuccessor(worker, y, "find", 0, 0)


if not keepAlive then
    isStable.Cancel()
    isConverged.Cancel()

printfn "Press enter to exit after result"
System.Console.ReadLine() |> ignore
