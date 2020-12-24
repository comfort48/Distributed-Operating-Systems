module Client

open System
open Akka.Actor
open Akka.FSharp
open Server

let selectClient(id: int) = 
    let actorPath = @"akka://FSharp/user/client"+ string id
    ///system.ActorSelection(actorPath)
    select actorPath system

let serverActor = 
   /// system.ActorSelection(@"akka://FSharp/user/server")
    select @"akka://FSharp/user/server" system

let getRandomTweet (newsFeed: List<String>) : String = 
    let rand = Random()
    newsFeed.[rand.Next(newsFeed.Length)]

let ClientActor id (mailbox: Actor<_>) = 
    let mutable newsFeedList = []
    let mutable inActiveNewsFeedList = []
    let mutable mentionsListOfUser = []
    let simActor = select @"akka://FSharp/user/simulator" system
    let rec loop()  = actor {
        let! message = mailbox.Receive()
        let server = serverActor
        let self = selectClient(id)
        match message with
        | Register(username) ->
            server <! RegisterActor(id, username)
        
        | Login(liveUsersPercentage) ->
            server <! LoginActor(id, liveUsersPercentage)

        | LoginSuccess ->
            newsFeedList <- newsFeedList @ inActiveNewsFeedList
            inActiveNewsFeedList <- List.empty
            //printfn "user id is %i and newsfeed is %A" id newsFeedList

        | Logout ->
            server <! LogoutActor(id)

        | LogoutSuccess ->
            inActiveNewsFeedList <- newsFeedList @ inActiveNewsFeedList
            newsFeedList <- List.empty
            //printfn "after logout ----- user id is %i and inActiveNewsFeedList is %A" id inActiveNewsFeedList

        | FollowActor(myId, targetId) -> 
            server <! message

        | Tweet(myId, tweet, isRetweet) ->
            server <! message
        
        | NewsFeedTweet(tweet) ->
            newsFeedList <- tweet :: newsFeedList
            ///printfn "my user id is %i and my news feed is %A \n" id newsFeedList

        | InActiveNewsFeedTweet(tweet) ->
            inActiveNewsFeedList <- tweet :: inActiveNewsFeedList

        | ReTweet ->
            if newsFeedList.Length > 0 then
                let randomTweet = getRandomTweet newsFeedList
                let reTweetedTweet = randomTweet + " RT"
                self <! Tweet(id, reTweetedTweet, true)
            else
                printfn "Nothing to re tweet"

        | Query ->
            server <! QueryActor(id)

        | QueryResult(mentionsList) ->
            mentionsListOfUser <- mentionsList
            //printfn "mentionsList of user %i is %A"id mentionsListOfUser
            simActor <! QueryAck

        | _ -> failwith "Unknown message receiveddd!"
        return! loop()
    }
    loop()

let register (id: int) (username: string)  = 

    selectClient(id) <! Register(username)

let initiate (numUsers: int) = 
    
    let usersList = [1..numUsers] |>
                        List.map (fun id -> 
                            spawn system ("client"+ string id) (ClientActor id)) 
    
    [1..numUsers] |> List.iter (fun id -> register id ("user" + string id))

let login (id: int) (liveUsersPercentage: int) = 

    selectClient(id) <! Login(liveUsersPercentage)

let logout (id: int) = 

    selectClient(id) <! Logout

let follow (myId: int) (targetId: int) = 

    selectClient(myId) <! FollowActor(myId, targetId)

let tweet (myId: int) (tweet: string) =

    selectClient(myId) <! Tweet(myId, tweet, false)

let reTweet (myId: int) = 

    selectClient(myId) <! ReTweet

let queryMention (myId: int) =

    selectClient(myId) <! Query
