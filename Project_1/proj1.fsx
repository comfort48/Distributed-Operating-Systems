#time "on"
#r "nuget: Akka.FSharp"

open System
open Akka.FSharp

// Creation of Akka Actor system
let system = System.create "system" (Configuration.defaultConfig())

let N = int fsi.CommandLineArgs.[1]  // Given input N value 
let k = int fsi.CommandLineArgs.[2]  // Given input k value

let mutable exitFlag = 0
let numberOfActors = Environment.ProcessorCount  // Number of Actors based on system architecture


// Custom message types that are sent between Worker actors and Boss actor
type ActorMsg =
    | Work of startNumber : int * endNumber : int * k : int 
    | Result of number : int
    | Calculate
    | Done

// Worker Actor, which takes work load from Boss and perform computation
let Worker(mailbox : Actor<_>) =
    let findSum(number : int) : bigint = 
        let firstPart = bigint number
        let secondPart = firstPart + (bigint 1)
        let thirdPart = (((bigint 2) * firstPart) + (bigint 1))

        (firstPart * secondPart * thirdPart) / (bigint 6)
    
    let sumOfSquares(startNumber : int, k : int): bigint =
        findSum(startNumber + k - 1) - findSum(startNumber-1)

    let isPerfectSquare(value : bigint) : bool =
        let valB = round(sqrt(double value)) |> bigint
        (valB * valB) = value
    
    let compute(startNumber : int, k : int) : int =
        if(isPerfectSquare(sumOfSquares(startNumber, k))) then startNumber else 0

    let rec loop () = actor {
        let! message = mailbox.Receive()
        let sender = mailbox.Sender()
        match message with
        | Work(startNumber, endNumber, k) -> for number = startNumber to (startNumber + endNumber - 1) do
                                                if compute(number, k) <> 0 then 
                                                    printf "%i\n" number
                                                sender <! Done
        | _ -> failwith "Worker message is not implemented!"
        return! loop ()
    }
    loop()

// Boss Actor which takes the given parameters, splits the work between actors and triggers the Worker Actors
let Boss(mailbox : Actor<_>) = 
    let mutable totalComputations =  0
    let one =  1

    let rec loop () = actor {
        let! message = mailbox.Receive()
        match message with
        | Done -> totalComputations <- totalComputations + one
                  if totalComputations = N then
                    exitFlag <- 1
 
        | Calculate -> let workUnit = N / numberOfActors
                       let remainder = N % numberOfActors
                       let workingActors = if workUnit = 0 then remainder else numberOfActors
                       let workerPool = Array.create workingActors (spawn system "workerActor" Worker)
                       {0..workingActors-1} |> Seq.iter (fun a ->
                           workerPool.[a] <- spawn system (string a) Worker
                           ()
                        )
                       for act = 1 to workingActors do
                           if act <= remainder then
                               let startNumber = (act - 1) * (workUnit + 1) + 1
                               workerPool.[act-1] <! Work(startNumber, workUnit + 1, k)
                           else
                               let startNumber = remainder * (workUnit + 1) + (act - 1 - remainder) * workUnit + 1
                               workerPool.[act-1] <! Work(startNumber, workUnit, k)
        | _ -> failwith "Worker message is not implemented!"
        return! loop ()
    }
    loop()

// Boss Actor computation trigger point
let bossActorRef = spawn system "bossActor" Boss
bossActorRef <! Calculate

// Exit condition
while exitFlag <> 1 do
    ignore()
