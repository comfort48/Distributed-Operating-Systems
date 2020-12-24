#time "on"
#r "nuget: Akka.FSharp"

open System
open Akka.Actor
open Akka.FSharp

let mutable endFlag = false

let numOfNodes = int fsi.CommandLineArgs.[1]
let numOfRequests = int fsi.CommandLineArgs.[2]

let baseValue =  ceil(log10(float(numOfNodes))/log10(float(4))) |> int
let nodeSpace = Math.Pow(float(4), float(baseValue)) |> int


let system = ActorSystem.Create("FSharp")

type ActorMsg = 
            | StartRouting
            | Route of a: string * b: int * c: int * d: int
            | RouteFinish of a: int
            | StartJoining
            | FirstJoin of a: int array
            | PrimaryJoin
            | SecondaryJoin
            | CompletedJoining
            | AddRow of a: int * b: (int array)
            | AddLeaf of a: int array
            | UpdateID of a: int
            | Acknowledgement

let rand = Random()

let swap (a: _[]) x y =
    let tmp = a.[x]
    a.[x] <- a.[y]
    a.[y] <- tmp

let shuffle a =
    Array.iteri (fun i _ -> swap a i (rand.Next(i, Array.length a))) a
    a

let randomMapping = shuffle [|0 .. nodeSpace-1|]

let mutable firstGroupSize: int = 0
if numOfNodes <= 1024 then
    firstGroupSize <- numOfNodes
else
    firstGroupSize <- 1024

let selectActor(id: int) = 
    let actorPath = "akka://FSharp/user/master/" + string id
    select actorPath system

let selectAllActors() = 
    system.ActorSelection("akka://FSharp/user/master/*")


let intToDig b source =
    let rec loop (b : int) num digits =
        let (quo, rem) = bigint.DivRem(num, bigint b)
        match quo with
        | zero when zero = 0I -> int rem :: digits
        | _ -> loop b quo (int rem :: digits)
    loop b source []

let digToStr length source =
    let base4Str = source |> List.map (fun (x : int) -> x.ToString("X").ToLowerInvariant()) |> String.concat ""
    let zeroes = length - base4Str.Length
    String.replicate zeroes "0" + base4Str

let toBase4Str source length =
    let bigintToBase4 = intToDig 4
    bigint(int source) |> bigintToBase4 |>  digToStr length

let getPrefixMatchLength (str1:string) (str2:string) = 
    let mutable len = 0
    while len < str1.Length && str1.[len] = str2.[len] do
        len <- len+1
    len


let pastryNode nodeID (mailbox: Actor<_>) =
    let routingTable = Array2D.create baseValue 4 -1
    let mutable smallLeafSet = Set.empty
    let mutable largeLeafSet = Set.empty
    let mutable numberOfAcks = 0

    let addNewID id = 
        if id > nodeID && not (largeLeafSet.Contains id) then
            if largeLeafSet.Count < 4 then
                largeLeafSet <- largeLeafSet.Add(id)
            elif id < largeLeafSet.MaximumElement then
                largeLeafSet <- largeLeafSet.Remove largeLeafSet.MaximumElement
                largeLeafSet <- largeLeafSet.Add(id)
        elif id < nodeID && not (smallLeafSet.Contains id) then
            if smallLeafSet.Count < 4 then
                smallLeafSet <- smallLeafSet.Add(id)
            elif id > smallLeafSet.MinimumElement then
                smallLeafSet <- smallLeafSet.Remove smallLeafSet.MinimumElement
                smallLeafSet <- smallLeafSet.Add(id) 
        let prefixLen = getPrefixMatchLength (toBase4Str nodeID baseValue) (toBase4Str id baseValue)
        if routingTable.[prefixLen, (toBase4Str id baseValue).[prefixLen] |> string |> int] = -1 then
           routingTable.[prefixLen, (toBase4Str id baseValue).[prefixLen] |> string |> int] <- id
    
    let addBuffer iDs = 
        for id in iDs do
            addNewID id
        

    let rec loop() = actor {
        let! message = mailbox.Receive()
        let sender = mailbox.Sender()
        match message with
            | Route(message, sourceNode, destinationNode, hops) ->
                if message = "Join" then
                    let prefixLen = getPrefixMatchLength (toBase4Str nodeID baseValue) (toBase4Str destinationNode baseValue)
                    let destinationActorRef = selectActor(destinationNode)
                    if hops = -1 && prefixLen > 0 then
                        for index=0 to prefixLen-1 do
                            destinationActorRef <! AddRow(index, Array.copy routingTable.[index, *])
                    destinationActorRef <! AddRow(prefixLen, Array.copy routingTable.[prefixLen, *])

                    if ((smallLeafSet.Count > 0 && smallLeafSet.MinimumElement <= destinationNode && nodeID >= destinationNode) ||(largeLeafSet.Count >0 && destinationNode <= largeLeafSet.MaximumElement && destinationNode >= nodeID)) then
                        let mutable diffSpace = nodeSpace + 10
                        let mutable nearestNode = -1
                        if destinationNode < nodeID then
                            for node in smallLeafSet do
                                if abs(node - destinationNode) < diffSpace then
                                    nearestNode <- node
                                    diffSpace <- abs(node - destinationNode)                                  
            
                        else 
                            for node in largeLeafSet do
                                if abs(node - destinationNode) < diffSpace then
                                    nearestNode <- node
                                    diffSpace <- abs(node - destinationNode)

                        if abs(nodeID - destinationNode) > diffSpace then
                            selectActor(nearestNode) <! Route(message, sourceNode, destinationNode, hops+1)
                        else
                            let destinationLeafs = (Set.union smallLeafSet largeLeafSet).Add(nodeID)
                            destinationActorRef <! AddLeaf(Set.toArray destinationLeafs)
                    
                    elif largeLeafSet.Count > 0 && largeLeafSet.Count < 4 && destinationNode > largeLeafSet.MaximumElement then
                        selectActor(largeLeafSet.MaximumElement) <! Route(message, sourceNode, destinationNode, hops+1)
                    elif smallLeafSet.Count > 0 && smallLeafSet.Count < 4 && destinationNode < smallLeafSet.MinimumElement then
                        selectActor(smallLeafSet.MinimumElement) <! Route(message, sourceNode, destinationNode, hops+1)
                    elif ((destinationNode < nodeID && smallLeafSet.Count = 0) || (destinationNode > nodeID && largeLeafSet.Count = 0)) then
                        let destinationLeafs = (Set.union smallLeafSet largeLeafSet).Add(nodeID)
                        destinationActorRef <! AddLeaf(Set.toArray destinationLeafs)
                    
                    elif routingTable.[prefixLen, (toBase4Str destinationNode baseValue).[prefixLen] |> string |> int] <> -1 then
                        selectActor(routingTable.[prefixLen, (toBase4Str destinationNode baseValue).[prefixLen] |> string |> int]) <! Route(message, sourceNode, destinationNode, hops+1)
                    elif destinationNode < nodeID then
                        selectActor(smallLeafSet.MinimumElement) <! Route(message, sourceNode, destinationNode, hops+1)
                    elif destinationNode > nodeID then
                        selectActor(largeLeafSet.MaximumElement) <! Route(message, sourceNode, destinationNode, hops+1)
                    else
                        printfn "Message is not expected!"
                
                elif message = "Route" then
                    if destinationNode = nodeID then
                        (select ("akka://FSharp/user/master") system) <! RouteFinish(hops+1)
                    else
                        let prefixLen = getPrefixMatchLength (toBase4Str nodeID baseValue) (toBase4Str destinationNode baseValue)
                        if ((smallLeafSet.Count > 0 && smallLeafSet.MinimumElement <= destinationNode && nodeID > destinationNode) ||(largeLeafSet.Count > 0 && largeLeafSet.MaximumElement >= destinationNode && nodeID < destinationNode)) then
                            let mutable diffSpace = nodeSpace + 10
                            let mutable nearestNode = -1
                            if nodeID > destinationNode then
                                for node in smallLeafSet do
                                    if abs(node - destinationNode) < diffSpace then
                                        nearestNode <- node
                                        diffSpace <- abs(node - destinationNode)
                            else
                                for node in largeLeafSet do
                                    if abs(node - destinationNode) < diffSpace then
                                        nearestNode <- node
                                        diffSpace <- abs(node - destinationNode)

                            if abs(nodeID - destinationNode) > diffSpace then
                                selectActor(nearestNode) <! Route(message, sourceNode, destinationNode, hops+1)
                            else
                                (select ("akka://FSharp/user/master") system) <! RouteFinish(hops+1)

                        elif smallLeafSet.Count > 0 && smallLeafSet.Count < 4 && destinationNode < smallLeafSet.MinimumElement then
                            selectActor(smallLeafSet.MinimumElement) <! Route(message, sourceNode, destinationNode, hops+1)
                        elif largeLeafSet.Count > 0 && largeLeafSet.Count < 4 && destinationNode > largeLeafSet.MaximumElement then
                            selectActor(largeLeafSet.MaximumElement) <! Route(message, sourceNode, destinationNode, hops+1)
                        elif ((smallLeafSet.Count = 0 && nodeID > destinationNode) || (largeLeafSet.Count = 0 && nodeID < destinationNode)) then
                            (select ("akka://FSharp/user/master") system) <! RouteFinish(hops+1)
                        elif routingTable.[prefixLen, (toBase4Str destinationNode baseValue).[prefixLen] |> string |> int] <> -1 then 
                            selectActor(routingTable.[prefixLen, (toBase4Str destinationNode baseValue).[prefixLen] |> string |> int]) <! Route(message, sourceNode, destinationNode, hops+1)
                        elif nodeID < destinationNode then
                            selectActor(largeLeafSet.MaximumElement) <! Route(message, sourceNode, destinationNode, hops+1)
                        elif nodeID > destinationNode then
                            selectActor(smallLeafSet.MinimumElement) <! Route(message, sourceNode, destinationNode, hops+1)
                        else
                            printfn "This conditions shouldn't occur!"

            
            | AddRow(rowIndex, routingTableRow) ->
                for index in 0..routingTableRow.Length-1 do
                    if routingTable.[rowIndex, index] = -1 then
                        routingTable.[rowIndex, index] <- routingTableRow.[index]
            

            | AddLeaf(incomingLeafSet) ->
                addBuffer(incomingLeafSet)
                for node in smallLeafSet do
                    numberOfAcks <- numberOfAcks + 1
                    selectActor(node) <! UpdateID(nodeID)
                for node in largeLeafSet do
                    numberOfAcks <- numberOfAcks + 1
                    selectActor(node) <! UpdateID(nodeID)
                for index in 0..baseValue-1 do
                    routingTable.[index, (toBase4Str nodeID baseValue).[index] |> string |> int] <- nodeID


            | FirstJoin(firstGroup) -> 
                let firstGroupNodes = firstGroup |> Array.filter (fun id -> id <> nodeID)
                addBuffer firstGroupNodes
                for i in 0..baseValue-1 do
                    routingTable.[i, (toBase4Str nodeID baseValue).[i] |> string |> int] <- nodeID
                sender <! CompletedJoining


            | StartRouting -> 
                for i in 1..numOfRequests do
                    let message = Route("Route", nodeID, rand.Next(nodeSpace), -1)
                    system.Scheduler.ScheduleTellOnce(TimeSpan.FromMilliseconds(1000 |> float), mailbox.Self, message)
            

            | UpdateID(newID) ->
                addNewID(newID)
                sender <! Acknowledgement
            

            | Acknowledgement ->
                numberOfAcks <- numberOfAcks - 1
                if numberOfAcks = 0 then
                    (select ("akka://FSharp/user/master") system) <! CompletedJoining

            | _ -> failwith "Message is not expected!"


        return! loop()
    }
    loop()


let MasterNode(mailbox: Actor<_>) =
    let firstGroup = Array.copy randomMapping.[0..firstGroupSize-1]
    let mutable numOfJoinedNodes = 0
    let mutable numOfHops = 0
    let mutable numOfRoutedRequests = 0

    
    for i in 0..numOfNodes-1 do
        spawn mailbox (string randomMapping.[i]) (pastryNode randomMapping.[i]) |> ignore  // Supervisor strategy

    let rec loop() = actor{
        let! message = mailbox.Receive()
        match message with
        | StartJoining -> 
            for id in firstGroup do
                let pastryNodeRef = selectActor id
                pastryNodeRef <! FirstJoin (Array.copy firstGroup)


        | CompletedJoining ->  
            numOfJoinedNodes <- numOfJoinedNodes + 1
            if numOfJoinedNodes > firstGroupSize then
                if numOfJoinedNodes = numOfNodes then 
                    mailbox.Self <! StartRouting
                else 
                    mailbox.Self <! SecondaryJoin
            if numOfJoinedNodes = firstGroupSize then
                if numOfJoinedNodes < numOfNodes then 
                    mailbox.Self <! SecondaryJoin
                else 
                    mailbox.Self <! StartRouting 

        | StartRouting -> 
            selectAllActors() <! StartRouting


        | SecondaryJoin -> 
            let randomID = randomMapping.[rand.Next(numOfJoinedNodes)]
            let randomRef = selectActor randomID
            randomRef <! Route("Join", randomID, randomMapping.[numOfJoinedNodes], -1)


        | RouteFinish(hops) -> 
            numOfRoutedRequests <- numOfRoutedRequests + 1
            numOfHops <- numOfHops + hops

            if numOfRoutedRequests >= numOfNodes*numOfRequests then
                printf "\n"
                let avgHopsCount = float(numOfHops) / float(numOfRoutedRequests)
                printfn "Average number of hops per a route request is = %f" avgHopsCount
                system.Stop(mailbox.Self)
                endFlag <- true

        | _-> failwith "Unknown message received!"

        return! loop()
    }
    loop()


let masterRef = spawn system "master" (MasterNode)

masterRef <! StartJoining

while not endFlag do
    ignore()
