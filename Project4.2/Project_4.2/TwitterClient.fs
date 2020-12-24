module Client

open System
open Akka.Actor
open Akka.FSharp
open Server
open Suave.WebSocket
open Server

let selectClient(id: string) = 
    let actorPath = @"akka://FSharp/user/client"+ id
    ///system.ActorSelection(actorPath)
    select actorPath system

let serverActor = 
   /// system.ActorSelection(@"akka://FSharp/user/server")
    select @"akka://FSharp/user/server" system

let getRandomTweet (newsFeed: List<String>) : String = 
    let rand = Random()
    newsFeed.[rand.Next(newsFeed.Length)]

let makeTweet (tweet: string) (username: string) (tweetId: string) (isRetweet: bool) = 
    let mutable  str = ""
    if isRetweet then
        str <- "[RETWEET] [" + username + "] [" + tweetId + "] --- " + tweet
    else
        str <- "[TWEET] [" + username + "] [" + tweetId + "] --- " + tweet
    str

let ClientActor (id: string) (userWs: WebSocket) (mailbox: Actor<_>) = 
    let mutable newsFeedList = []
    let mutable inActiveNewsFeedList = []
    let mutable mentionsListOfUser = []
    let simActor = select @"akka://FSharp/user/simulator" system
    let rec loop()  = actor {
        let! message = mailbox.Receive()
        let server = serverActor
        let self = selectClient(id)
        match message with
        | Register(username, password) ->
            server <! RegisterActor(username, password)
        
        | Login(username, password) ->
            server <! LoginActor(username, password)

        | LoginSuccess ->
            newsFeedList <- newsFeedList @ inActiveNewsFeedList
            inActiveNewsFeedList <- List.empty
            workerSelection <! LoginAck(true, id, newsFeedList)
            //printfn "user id is %i and newsfeed is %A" id newsFeedList

        | Logout ->
            server <! LogoutActor(id)

        | LogoutSuccess ->
            inActiveNewsFeedList <- newsFeedList @ inActiveNewsFeedList
            newsFeedList <- List.empty
            workerSelection <! LogoutAck(id)
            //printfn "after logout ----- user id is %i and inActiveNewsFeedList is %A" id inActiveNewsFeedList

        | FollowActor(myId, targetId) -> 
            server <! message

        | Tweet(myId, tweet, isRetweet) ->
            server <! message
        
        | SelfTweet(myId, tweetId, tweet, isRetweet) ->
            let tweet = makeTweet tweet myId tweetId isRetweet
            newsFeedList <- tweet :: newsFeedList
            sendResponse userWs tweet

        | NewsFeedTweet(myId, tweet, tweetId) ->
            let newsFeedTweet = makeTweet tweet myId tweetId false
            newsFeedList <- newsFeedTweet :: newsFeedList
            sendResponse userWs newsFeedTweet
            printfn "my user id is %s and my news feed is %A \n" id newsFeedList

        | InActiveNewsFeedTweet(myId, tweet, tweetId) ->
            let inActivenewsFeedTweet = makeTweet tweet myId tweetId false
            inActiveNewsFeedList <- inActivenewsFeedTweet :: inActiveNewsFeedList

        | ReTweet(myId, tweetId) ->
            server <! ReTweet(myId, tweetId)

        | Query(myId, queryStr) ->
            server <! QueryActor(myId, queryStr)

        | QuerySuccess(result) ->
            let newRes = "[QUERY RESULT]" + result
            sendResponse userWs newRes

        | QueryFailure(myId, queryStr) ->
            let error = "No results found for " + queryStr
            sendResponse userWs error

        | _ -> failwith "Unknown message receiveddd!"
        return! loop()
    }
    loop()

let register (username: string) (password: string) = 

    selectClient(username) <! Register(username, password)

// let initiate (username: string) = 
    
//     spawn system ("client"+ username) (ClientActor username) |> ignore
//     register username

let login (id: string) (password: string)  = 

    selectClient(id) <! Login(id, password)

let logout (id: string) = 

    selectClient(id) <! Logout

let follow (myId: string) (targetId: string) = 

    selectClient(myId) <! FollowActor(myId, targetId)

let tweet (myId: string) (tweet: string) =

    selectClient(myId) <! Tweet(myId, tweet, false)

let reTweet (myId: string) (tweetId: string) = 

    selectClient(myId) <! ReTweet(myId, tweetId)

let query (myId: string) (queryStr: string) =

    selectClient(myId) <! Query(myId, queryStr)
