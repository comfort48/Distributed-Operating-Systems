#r "nuget: Akka.FSharp"

open System
open Akka.FSharp
open Akka.Actor

let mutable endFlag = false

let maxGossipCount = 100 // Can be changed to any other value
let numOfNodes = int fsi.CommandLineArgs.[1]
let topology = fsi.CommandLineArgs.[2]
let algorithm = fsi.CommandLineArgs.[3]
let mutable network = Map.empty
(*
let sqrtVal = sqrt(float numOfNodes) |> int
let rows = [for i in sqrtVal..1 do yield i] |> List.find(fun i -> numOfNodes % i = 0)
let cols = numOfNodes / rows
*)

type ActorMsg = 
    | Rumor
    | LoopGossip
    | ConvergeActor of id: int
    | UpdateActor of id: int
    | PushSumMessage of s: float * w: float
    | UpdatePSActor of id: int
    | LoopPS


let system = ActorSystem.Create("FSharp")

let selectRandomNode(nodes: List<_>) = 
    let rand = Random()
    nodes.[rand.Next(nodes.Length)]

let getRandomNum : int = 
    let rand = Random()
    [1..numOfNodes].[rand.Next(numOfNodes)]

let inRange (x : int, y : int, num : int) : bool = 
    x >= 0 && x < num && y >= 0 && y < num

let buildTopology() = 
    let nodes = [1..numOfNodes]
    let squareRoot = sqrt(float numOfNodes)
    let isPerfectSquare =  squareRoot % 1.0 = 0.0
    let mutable num = 1;
    if isPerfectSquare then
        num <- int squareRoot
    else
        num <- Math.Ceiling(squareRoot) |> int
    //let num = Math.Round(squareRoot) |> int
    
    match topology with
    | "full" -> 
        //printfn "I entered full: %A" nodes
        nodes 
        |> List.map (fun id -> 
            let neighbors = List.filter (fun n -> id <> n) nodes 
            network <- network.Add(id, neighbors)) |> ignore
        //printfn "The network is: %A" network
    
    | "line" -> 
        //printfn "I entered line: %A" nodes
        nodes
        |> List.iter (fun id -> let mutable neighbors = []
                                if id = 1 then 
                                    if (id+1) <= numOfNodes then
                                        neighbors <- [id + 1]
                                elif id = nodes.Length then
                                    if (id-1) >= 1 then
                                        neighbors <- [id - 1]
                                else 
                                    neighbors <- [id - 1 ; id + 1]
                                network <- network.Add(id, neighbors)
        )
    
    | "2D" ->
        //printfn "I entered 2D: %A" nodes
        for i = 0 to num - 1 do
          for j = 0 to num - 1 do
            let mutable neighbors = []
            let number = num * i + j + 1
            if inRange(i - 1, j, num) then
                if (number - num) >= 1 && (number - num) <= numOfNodes then
                    neighbors <- number - num :: neighbors
            if inRange(i + 1, j, num) then
                if (number + num) >= 1 && (number + num) <= numOfNodes then
                    neighbors <- number + num :: neighbors
            if inRange(i, j - 1, num) then
                if (number - 1) >= 1 && (number - 1) <= numOfNodes then
                    neighbors <- number - 1 :: neighbors
            if inRange(i, j + 1, num) then
                if (number + 1) >= 1 && (number + 1) <= numOfNodes then
                    neighbors <- number + 1 :: neighbors
            
            network <- network.Add(number, neighbors)
    
    | "imp2D" ->
        //printfn "I entered imp2D: %A" nodes
        for i = 0 to num - 1 do
          for j = 0 to num - 1 do
            let mutable neighbors = []
            let number = num * i + j + 1
            if inRange(i - 1, j, num) then
                if (number - num) >= 1 && (number - num) <= numOfNodes then
                    neighbors <- number - num :: neighbors
            if inRange(i + 1, j, num) then
                if (number + num) >= 1 && (number + num) <= numOfNodes then
                    neighbors <- number + num :: neighbors
            if inRange(i, j - 1, num) then
                if (number - 1) >= 1 && (number - 1) <= numOfNodes then
                    neighbors <- number - 1 :: neighbors
            if inRange(i, j + 1, num) then
                if (number + 1) >= 1 && (number + 1) <= numOfNodes then
                    neighbors <- number + 1 :: neighbors
            
            let temp = [number] @ neighbors
            let newList = 
                    nodes |>
                        List.filter (fun id -> not (temp |> List.contains id))
            
            if not newList.IsEmpty then
                let randNeighbor = selectRandomNode(newList)
                neighbors <- randNeighbor :: neighbors
            
            network <- network.Add(number, neighbors)
        
    | _ -> ignore()

///select gossip actor
let selectActor(id: int) = 
    let actorPath = @"akka://FSharp/user/actor" + string id
    select actorPath system

///select push sum actor
let selectPSActor(id: int) = 
    let actorPath = @"akka://FSharp/user/actorPushSum" + string id
    select actorPath system
 
///get random neighbor for gossip
let getRandomNeighbor(id: int, finishedActors: list<int>) =
    let neighborList = network.TryFind(id).Value
    let finishedActorsSet = Set.ofList finishedActors;
    let unfinishedActors = 
        neighborList |>
            List.filter (fun id -> not (finishedActorsSet |> Set.contains id))
    
    if unfinishedActors.Length > 0 then
        selectActor(selectRandomNode(unfinishedActors))
    
    else
        let activeActors = 
            [1..numOfNodes] |>
                List.filter (fun id-> not (finishedActorsSet |> Set.contains id))
        selectActor(selectRandomNode(activeActors))

///Get random neighbor for push sum
let getRandomPSNeighbor(id: int, finishedActors: list<int>) =
    let neighborList = network.TryFind(id).Value
    let finishedActorsSet = Set.ofList finishedActors;
    let unfinishedActors = 
        neighborList |>
            List.filter (fun id -> not (finishedActorsSet |> Set.contains id))
    
    if unfinishedActors.Length > 0 then
        selectPSActor(selectRandomNode(unfinishedActors))
    
    else
        let activeActors = 
            [1..numOfNodes] |>
                List.filter (fun id-> not (finishedActorsSet |> Set.contains id))
        selectPSActor(selectRandomNode(activeActors))
    
let MasterNode (mailbox: Actor<_>) = 
    let rec loop count = actor {
        let! message = mailbox.Receive()
        match message with 
        | ConvergeActor(id) ->
            ///printfn "Converged Node ID: %i" id
            if (count = numOfNodes-1) then 
                ///printfn "Algorithm has converged"
                endFlag <- true
            if (algorithm.Equals("push-sum")) then
                ///printfn "Algorithm has converged"
                endFlag <- true
        | _ -> failwith "Unknown message received!"
        return! loop (count+1)
    }
    loop 0

let masterActor = spawn system "master" MasterNode

///spread gossip rumor
let spreadRumor(id: int, finishedActors: list<int>) = 
    //printfn "To spread! %i" id
    getRandomNeighbor(id, finishedActors) <! Rumor

///spread push sum message
let spreadPSMessage(id: int, state: float, weight: float, finishedActors: list<int>) = 
    getRandomPSNeighbor(id, finishedActors) <! PushSumMessage(state, weight)

///update gossip neighbors
let updateNeighbors(finishedActorID: int) = 
    [1..numOfNodes] |>
        List.iter (fun id -> selectActor(id) <! UpdateActor(finishedActorID))

///update push sum neighbors
let updatePSNeighbors(finishedActorID: int) = 
    [1..numOfNodes] |>
        List.iter (fun id -> selectPSActor(id) <! UpdatePSActor(finishedActorID))

let check(oldRatio: float, newRatio: float) : Boolean = 
    Math.Abs(newRatio - oldRatio) < Math.Pow(10.0, -10.0)

let GossipNode id (mailbox: Actor<_>) = 
    let rec loop count finishedActors = actor {
        let! message = mailbox.Receive()
        let self = selectActor(id)
        let updatedCount, newFinishedList = 
            if count < maxGossipCount then
                match message with 
                | LoopGossip ->
                    spreadRumor(id, finishedActors)
                    self <! LoopGossip
                    count, finishedActors
                | Rumor ->
                    //printfn "Count value is: %i" (count+1)
                    spreadRumor(id, finishedActors)
                    if count = 0 then
                        self <! LoopGossip
                    if count = maxGossipCount - 1 then
                        updateNeighbors(id)
                        masterActor <! ConvergeActor(id)
                    count+1, finishedActors
                | UpdateActor(id) ->
                    count, id::finishedActors
                | _ -> failwith "Unknown message received!"
            else
                count+1, finishedActors
        return! loop updatedCount newFinishedList
    }
    loop 0 List.empty

let PushSumNode id (mailbox: Actor<_>) = 
    let mutable loopNum = 0
    let mutable count = 0
    let mutable s : float = float id
    let mutable w : float = 1.0
    let rec loop finishedActors = actor {
        let! message = mailbox.Receive()
        //printfn "message %A" message
        let self = selectPSActor(id)
        let newFinishedActors =
            if count < 3 then
                match message with
                | LoopPS ->
                    spreadPSMessage(id, s/2.0, w/2.0, finishedActors)
                    self <! LoopPS
                    s <- s/2.0
                    w <- w/2.0
                    finishedActors
                | PushSumMessage(snew, wnew) ->
                    ///printfn "snew is %f and wnew is %f for actor %i" snew wnew id
                    
                    let newState: float = s + snew
                    let newWeight: float = w + wnew
                    let oldRatio: float = s/w
                    let newRatio: float = newState/newWeight
                    
                    if loopNum = 0 then
                        loopNum <- loopNum + 1
                        //self <! LoopPS
                    if check(oldRatio, newRatio) then
                        count <- count + 1
                        if count = 3 then
                            ///updatePSNeighbors(id)
                            ///printfn "new ratio is %f" newRatio
                            masterActor <! ConvergeActor(id)
                        else
                            spreadPSMessage(id, newState/2.0, newWeight/2.0, finishedActors)
                    else 
                        count <- 0
                        spreadPSMessage(id, newState/2.0, newWeight/2.0, finishedActors)

                    s <- newState/2.0
                    w <- newWeight/2.0
                    finishedActors
                    
                | UpdatePSActor(id) ->
                    id::finishedActors
                | _ -> failwith "Unknown message receiveddd!"
            else
                finishedActors
        return! loop newFinishedActors
    }
    loop List.empty

let randomActorNodeGossip = 
    [1..numOfNodes] |>
        List.map (fun id -> 
            spawn system ("actor"+ string id) (GossipNode id)) |> selectRandomNode

let randomActorNodePushSum = 
    [1..numOfNodes] |>
        List.map (fun id -> 
            spawn system ("actorPushSum"+ string id) (PushSumNode id)) |> selectRandomNode

buildTopology()
//printfn "map is %A" network
let stopWatch = Diagnostics.Stopwatch.StartNew()

if algorithm.Equals("gossip") then
    randomActorNodeGossip <! Rumor
else
    randomActorNodePushSum <! PushSumMessage(0.0, 0.0)

while not endFlag do
    ignore()

stopWatch.Stop()
printfn "Time taken for Convergence with %s topology and %s algorithm is: %f milli seconds" topology algorithm stopWatch.Elapsed.TotalMilliseconds