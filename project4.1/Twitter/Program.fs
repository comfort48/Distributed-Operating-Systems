// Learn more about F# at http://fsharp.org

module App
open System.Text.RegularExpressions
open System.Collections.Generic
open MathNet.Numerics.Distributions
open System.Threading
open Akka.Actor
open Akka.FSharp
open Server
open Client
let mutable inAction = true

[<EntryPoint>]
let main argv =
    
    let numUsers = argv.[0] |> int
    let liveUsersPercentage = argv.[2] |> int
    let maxSubs = argv.[1] |> int
    spawn system "server" serverMaster |> ignore
    let stopWatch = System.Diagnostics.Stopwatch()
    let stopWatchForFollow = System.Diagnostics.Stopwatch()
    let stopWatchForTweet = System.Diagnostics.Stopwatch()
    let stopWatchForQuery = System.Diagnostics.Stopwatch()
    let randomHashtags = ["#MerryChristmas"; "#HappyHolidays"; "#HowYouDoin"; "#RonaldoGOAT"; "#Beach";
                     "#randomHashtag"; "#Running"; "#GoGators"; "#2020"; "#Apocalypse"]
    let rand = System.Random()
    let userList = [1..numUsers]

    let mutable subsMap : Map<int, int> = Map.empty
    let mutable totalSubs : int = 0
    let zipfSub = Zipf(0.9, maxSubs)   
    for i in userList do
      let sample = zipfSub.Sample()
      totalSubs <- totalSubs + sample
      subsMap <- subsMap.Add(i, sample)

    printfn "total subscriptions are %i" totalSubs

    let numUsersToRetweet : int = floor(double(numUsers/5)) |> int

    let printer (mailbox:Actor<_>) = 
        let rec loop() = actor{
            let! message = mailbox.Receive()
            printfn "%s" message
            return! loop()
        }
        loop()

    let simulator (mailbox: Actor<_>) = 
        let mutable regAckCount = 0
        let mutable loginAckCount = 0
        let mutable followAckCount = 0
        let mutable tweetAckCount = 0
        let mutable retweetAckCount = 0
        let mutable queryAckCount = 0
        let printerRef = select @"akka://FSharp/user/printer" system
        let self = select @"akka://FSharp/user/simulator" system
        let rec loop()  = actor {
            let! message = mailbox.Receive()
            match message with
            | BeginProcess ->
                stopWatch.Start()
                initiate numUsers


            | RegAck ->
                regAckCount <- regAckCount + 1
                if regAckCount = numUsers then
                    printerRef <! "All the users have been registered!"
                   // printfn "tweet map count %i" userTweets.Count
                    self <! LoginSim
                    
            
            | LoginSim ->
                userList |> List.iter (fun i -> login i liveUsersPercentage)


            | LoginAck ->
                loginAckCount <- loginAckCount + 1
                if loginAckCount = numUsers then
                    printerRef <! "All the users have been logged in!"
                   // printfn "number of active users %i" activeUsers.Count
                    self <! FollowSim


            | FollowSim ->
                stopWatchForFollow.Start()
                for i in userList do
                     let numFollowers = subsMap.TryFind(i).Value
                     let followersList = getList userList numFollowers
                     for j in followersList do
                        follow j i


            | FollowAck ->
                followAckCount <- followAckCount + 1
                if followAckCount = totalSubs then
                    printerRef <! "user subscription process is complete!"
                    stopWatchForFollow.Stop()
                    printfn "--------total time subscriptions is %f" stopWatchForFollow.Elapsed.TotalMilliseconds
                    self <! StartTweeting


            | StartTweeting ->
                stopWatchForTweet.Start()
                for i in userList do
                    let numOfTweets = subsMap.TryFind(i).Value
                    for j in [1..numOfTweets] do
                        let randUser = rand.Next(userList.Length) + 1
                        let randHashtag = randomHashtags.Item(rand.Next(randomHashtags.Length))
                        let tweetStr = "Hi, this is user" + string i + ". I like @user" + string randUser + ", and my fav is " + randHashtag
                        tweet i tweetStr

            
            | TweetAck ->
                tweetAckCount <- tweetAckCount + 1
                if tweetAckCount = totalSubs then
                    printerRef <! "tweets done succesfully"
                    stopWatchForTweet.Stop()
                    printfn "--------total time for tweeting is %f" stopWatchForTweet.Elapsed.TotalMilliseconds
                    self <! StartRetweeting


            | StartRetweeting ->
                let retweetList = getList userList numUsersToRetweet
                for i in retweetList do
                    reTweet i


            | ReTweetAck ->
                retweetAckCount <- retweetAckCount + 1
                if retweetAckCount = numUsersToRetweet then
                    printerRef <! "Retweeting done succesfully"
                    self <! StartQuerying


            | StartQuerying ->
                stopWatchForQuery.Start()
                for i in userList do
                    queryMention i
            

            | QueryAck ->
                queryAckCount <- queryAckCount + 1
                if queryAckCount = numUsers then
                    printerRef <! "Querying done succesfully"
                    stopWatchForQuery.Stop()
                    printfn "--------total time for querying is is %f" stopWatchForQuery.Elapsed.TotalMilliseconds
                    stopWatch.Stop()
                    printfn "--------total execution time is %f" stopWatch.Elapsed.TotalMilliseconds
                    inAction <- false

            | _ -> failwith "Unknown message receiveddd!"
            return! loop()
         }
        loop()


    spawn system "printer" printer |> ignore
    let simRef = spawn system "simulator" simulator
    simRef <! BeginProcess

    while inAction do 
        ignore()

    0 // return an integer exit code
