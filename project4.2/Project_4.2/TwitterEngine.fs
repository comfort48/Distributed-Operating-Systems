module Server

open System
open Akka.Actor
open Akka.FSharp
open System.Text.RegularExpressions
open Suave.WebSocket

open Suave.Sockets
open Suave.Sockets.Control

type ActorMsg = 
    | Register of username: string * pwd: string
    | RegisterActor of username: string * pwd: string
    | Login of id: string * pwd: string
    | LoginActor of id: string * pwd: string
    | Logout
    | LogoutActor of id: string
    | FollowActor of myId: string * targetId: string
    | Tweet of myId: string * tweet: string * isRetweet: bool
    | NewsFeedTweet of myId: string * tweet: string * tweetId: string
    | InActiveNewsFeedTweet of myId: string * tweet: string * tweetId: string
    | ReTweet of myId: string * tweetId: string
    | Query of myId: string * queryStr: string
    | QueryActor of id: string * queryStr: string
    | QueryResult of results: List<String>
    | LogoutSuccess
    | LoginSuccess
    | RegAck of success: bool * username: string
    | BeginProcess
    | TweetAck
    | FollowAck of myId : string * targetId : string
    | Simulate
    | LoginSim
    | LoginAck of success: bool * username: string * newsFeedList: List<String>
    | FollowSim
    | StartTweeting
    | StartRetweeting
    | ReTweetAck
    | StartQuerying
    | QueryAck
    | SelfTweet of myId: string * tweetId: string * tweet: string * isRetweet: bool
    | ReTweetFail of myId: string
    | QueryFailure of myId: string * queryStr: string
    | QuerySuccess of result: string
    | LogoutAck of id: string

let system = ActorSystem.Create("FSharp")

let clientSelection(id: string) = 
    let actorPath = @"akka://FSharp/user/client"+ id
    ///system.ActorSelection(actorPath)
    select actorPath system

let convertByte (text: string) =
     text |> System.Text.Encoding.ASCII.GetBytes |>ByteSegment

let sendResponse (webSocket : WebSocket) (message: string) =
    let msg = convertByte message
    //printfn "Printing msg %A" msg

    let s = socket {
            let! res = webSocket.send Text msg true 
            return res;
            }
    Async.StartAsTask s |> ignore

let workerSelection = 
  select @"akka://FSharp/user/workerActor" system 

let random = System.Random()

let getList originalList length = 
        originalList |> List.sortBy (fun _ -> random.Next() ) |> List.take length

//let mutable users : Map<String, User> = Map.empty
let mutable userTweets : Map<String, Set<String>> = Map.empty //store tweetids of a user
let mutable userFollowers : Map<String, Set<String>> = Map.empty //store followers of user
let mutable tweets : Map<String, String> = Map.empty //tweet to tweet id mapping
let mutable mentions : Map<String, Set<String>> = Map.empty
let mutable hashtags : Map<String, Set<String>> = Map.empty
let mutable userToUsername:  Map<String, String> = Map.empty
let mutable creds : Map<String, String> = Map.empty
let mutable activeUsers : Set<String> = Set.empty //store user ids
let mutable usernames : Set<String> = Set.empty //store the usernames of users
let mutable activeUsersPercentage: int = 100
let mutable tweetCounter : int = 1

let mentionsRegex = "@[A-Za-z0-9]*"
let hashtagsRegex = "#[A-Za-z0-9]*"

let checkPattern (tweet: string) (pattern: string) = 
    let r =  Regex(pattern)
    let ms = r.Matches tweet
    //printfn "ms.Count = %A" ms.Count
    //for i in 0..ms.Count-1 do
        //printfn "ms.Item.[%d] = %A" i <| ms.Item i
    ms

let serverMaster (mailbox: Actor<_>) = 

    let registerUser (id: string) (pwd: string) =
        let userId = id
        if userTweets.ContainsKey(userId) then
                printfn "user with user id %s already exists" userId
                workerSelection <! RegAck(false, id)
        else
                userTweets <- userTweets.Add(userId, Set.empty)
                userFollowers <- userFollowers.Add(userId, Set.empty)
                creds <- creds.Add(userId, pwd)
                //userToUsername <- userToUsername.Add(string id, username);
                usernames <- usernames.Add(id)
                printfn "in register with name %s" id
                workerSelection <! RegAck(true, id)
                //simulatorActor <! RegAck

    
    let loginUser (id: string) (pwd: string) =
        let userId = id
        let userExists = userTweets.ContainsKey(userId)
        if not userExists then
                printfn "user with user id %s is not registered, please register first!" userId
                
        elif activeUsers.Contains(userId) then
                printfn "user with user id %s is already logged in" userId

        else 
                let savedPwd = creds.TryFind(id).Value
                if savedPwd = pwd then
                    clientSelection(id) <! LoginSuccess
                    activeUsers <- activeUsers.Add(userId)
                else 
                    workerSelection <! LoginAck(false, id, List.empty)
                //printfn "successfully logged in user %s" userId
                    
                //simulatorActor <! LoginAck

    let logoutUser (id: string) =
        let userId = id
        let isUserActive = activeUsers.Contains(userId)

        if not isUserActive then
                printfn "user with user id %s is not logged in" userId

        else 
                activeUsers <- activeUsers.Remove(userId)
                printfn "user successfully logged out, user id %s" userId
                clientSelection(id) <! LogoutSuccess
            
    let followUser (myId: string) (targetId: string) =
         // myId follows target id
            let myUserId =  myId
            let userToFollow = targetId
            let myUserExists = userTweets.ContainsKey(myUserId)
            let userToFollowExists = userTweets.ContainsKey(userToFollow)
            if myUserExists && userToFollowExists then
                if myId <> targetId then
                    let mutable followersSet = userFollowers.TryFind(userToFollow).Value
                    followersSet <- followersSet.Add(myUserId)
                    userFollowers <- userFollowers.Add(userToFollow, followersSet)
                    workerSelection <! FollowAck(myId, targetId)

                //printfn "user %s now follows user %s" myUserId userToFollow
               // simulatorActor <! FollowAck
            else
                printfn "One or both of the user ids - %s, %s are not registered" myUserId userToFollow


    let handleTweet (myId: string) (tweet: string) (isRetweet: bool) = 
        //printfn "user id %i has tweeted %s" myId tweet
        // if activeUsersPercentage <> 100 then
        // let totalUsers = usernames.Count |> decimal
        // let dec = activeUsersPercentage |> decimal
        // let liveUsers = (dec/100m)*totalUsers |> int
        // let liveUsersList = getList [1..usernames.Count] liveUsers
        // let lis : List<string> = liveUsersList |> List.map (fun i -> i.ToString())
        // activeUsers <- Set.ofSeq lis
            
        let tweetCount = string tweetCounter
            //adding in tweets map
        tweets <- tweets.Add(tweetCount, tweet)
        if not isRetweet then
            clientSelection(myId) <! SelfTweet(myId, tweetCount, tweet, false)
        else 
            clientSelection(myId) <! SelfTweet(myId, tweetCount, tweet, true)
       
            //traverse followers of user and send tweet to their newsfeed
        let followersSet = userFollowers.TryFind(myId).Value
        Set.iter (fun id -> if activeUsers.Contains(id) then
                                    clientSelection(id) <! NewsFeedTweet(myId, tweet, tweetCount)
                                else
                                    clientSelection(id) <! InActiveNewsFeedTweet(myId, tweet, tweetCount)) followersSet

            //check mentions and add it in mentions map
        let mentionsList = checkPattern tweet mentionsRegex
        for i in 0..mentionsList.Count-1 do
                let mention = mentionsList.Item i |> string
                let mentionName = mention.Substring(1)
                if mentions.ContainsKey(mentionName) then
                    let mutable mentionSet = mentions.TryFind(mentionName).Value
                    mentionSet <- mentionSet.Add(tweetCount)
                    mentions <- mentions.Add(mentionName, mentionSet)
                else
                    let mutable mentionSet = Set.empty
                    mentionSet <- mentionSet.Add(tweetCount)
                    mentions <- mentions.Add(mentionName, mentionSet)

            //check hashtags and add it in hashtags map
        let hastagsList = checkPattern tweet hashtagsRegex
        for i in 0..hastagsList.Count-1 do
                let hashtag = hastagsList.Item i |> string
                let hashtagName = hashtag.Substring(1)
                if hashtags.ContainsKey(hashtagName) then
                    let mutable hashtagSet = hashtags.TryFind(hashtagName).Value
                    hashtagSet <- hashtagSet.Add(tweetCount)
                    hashtags <- hashtags.Add(hashtagName, hashtagSet)
                else
                    let mutable hashtagSet = Set.empty
                    hashtagSet <- hashtagSet.Add(tweetCount)
                    hashtags <- hashtags.Add(hashtagName, hashtagSet)
                
            
            //adding in users map
        let mutable tweetsSet = userTweets.TryFind(myId).Value
        tweetsSet <- tweetsSet.Add(tweetCount)
        userTweets <- userTweets.Add(string myId, tweetsSet)

        tweetCounter <- tweetCounter + 1
        // if isRetweet then
        //     simulatorActor <! ReTweetAck
        // else
        //     simulatorActor <! TweetAck


    let reTweet (myId: string) (tweetId: string) = 
        if tweets.ContainsKey(tweetId) then
            let tweet = tweets.TryFind(tweetId).Value
            handleTweet myId tweet true
        else
            workerSelection <! ReTweetFail(myId)
        

     
    let rec loop()  = actor {
        let! message = mailbox.Receive()
        match message with
        | RegisterActor(username, pwd) ->
            registerUser username pwd
            //serverAuth <! message
        
        | LoginActor(id, pwd) ->
            loginUser id pwd
           //serverAuth <! message

        | LogoutActor(id) ->
            logoutUser id
            //serverAuth <! message
        
        | FollowActor(myId, targetId) ->
            followUser myId targetId
            //followHandler <! message

        | Tweet(myId, tweet, isRetweet) ->
            handleTweet myId tweet isRetweet
            //tweetHandler <! message

        | ReTweet(myId, tweetId) ->
            reTweet myId tweetId
        
        | QueryActor(id, queryStr) ->
            if queryStr.[0] = '@' then
                let mention = queryStr.[1..]
                if mentions.ContainsKey(mention) then
                    let mentionsSet = mentions.TryFind(mention).Value
                    let res: List<string> = mentionsSet |> Set.toList |> List.map (fun i -> tweets.TryFind(i).Value)
                    let resultStr = res |> String.concat "|"
                    clientSelection(id) <! QuerySuccess(resultStr)
                else
                    clientSelection(id) <! QueryFailure(id, queryStr)
            else
                    let hashtag = queryStr.[1..]
                    if hashtags.ContainsKey(hashtag) then
                        let hashstagSet = hashtags.TryFind(hashtag).Value
                        let res: List<string> = hashstagSet |> Set.toList |> List.map (fun i -> tweets.TryFind(i).Value)
                        let resultStr = res |> String.concat "|"
                        clientSelection(id) <! QuerySuccess(resultStr)
                    else
                        clientSelection(id) <! QueryFailure(id, queryStr)

        | _ -> failwith "Unknown message receiveddd!"
        return! loop()
    }
    loop()

