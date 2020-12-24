module Server

open System
open Akka.Actor
open Akka.FSharp
open System.Text.RegularExpressions

type ActorMsg = 
    | Register of username: string
    | RegisterActor of id: int * username: string
    | Login of liveUsersPercentage: int
    | LoginActor of id: int * liveUsersPercentage: int
    | Logout
    | LogoutActor of id: int
    | FollowActor of myId: int * targetId: int
    | Tweet of myId: int * tweet: string * isRetweet: bool
    | NewsFeedTweet of tweet: string
    | InActiveNewsFeedTweet of tweet: string
    | ReTweet
    | Query
    | QueryActor of id: int
    | QueryResult of results: List<String>
    | LogoutSuccess
    | LoginSuccess
    | RegAck
    | BeginProcess
    | TweetAck
    | FollowAck
    | Simulate
    | LoginSim
    | LoginAck
    | FollowSim
    | StartTweeting
    | StartRetweeting
    | ReTweetAck
    | StartQuerying
    | QueryAck

let system = ActorSystem.Create("FSharp")

let clientSelection(id: int) = 
    let actorPath = @"akka://FSharp/user/client"+ string id
    ///system.ActorSelection(actorPath)
    select actorPath system

let simulatorActor = 
    select @"akka://FSharp/user/simulator" system

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

    let registerUser (id: int) (username: string) =
        let userId = string id
        if userTweets.ContainsKey(userId) then
                printfn "user with user id %s already exists" userId
        else
                userTweets <- userTweets.Add(userId, Set.empty)
                userFollowers <- userFollowers.Add(userId, Set.empty)
                userToUsername <- userToUsername.Add(string id, username);
                usernames <- usernames.Add(username)
                simulatorActor <! RegAck

    
    let loginUser (id: int) =
        let userId = string id
        let userExists = userTweets.ContainsKey(userId)
        if not userExists then
                printfn "user with user id %s is not registered, please register first!" userId
                
        elif activeUsers.Contains(userId) then
                printfn "user with user id %s is already logged in" userId

        else 
                activeUsers <- activeUsers.Add(userId)
                //printfn "successfully logged in user %s" userId
                clientSelection(id) <! LoginSuccess
                simulatorActor <! LoginAck

    let logoutUser (id: int) =
        let userId = string id
        let isUserActive = activeUsers.Contains(userId)

        if not isUserActive then
                printfn "user with user id %s is not logged in" userId

        else 
                activeUsers <- activeUsers.Remove(userId)
                printfn "user successfully logged out, user id %s" userId
                clientSelection(id) <! LogoutSuccess
            
    let followUser (myId: int) (targetId: int) =
         // myId follows target id
            let myUserId = string myId
            let userToFollow = string targetId
            let myUserExists = userTweets.ContainsKey(myUserId)
            let userToFollowExists = userTweets.ContainsKey(userToFollow)
            if myUserExists && userToFollowExists then
                if myId <> targetId then
                    let mutable followersSet = userFollowers.TryFind(userToFollow).Value
                    followersSet <- followersSet.Add(myUserId)
                    userFollowers <- userFollowers.Add(userToFollow, followersSet)

                //printfn "user %s now follows user %s" myUserId userToFollow
                simulatorActor <! FollowAck
            else
                printfn "One or both of the user ids - %s, %s are not registered" myUserId userToFollow

    let handleTweet (myId: int) (tweet: string) (isRetweet: bool) = 
        //printfn "user id %i has tweeted %s" myId tweet
        // if activeUsersPercentage <> 100 then
        let totalUsers = usernames.Count |> decimal
        let dec = activeUsersPercentage |> decimal
        let liveUsers = (dec/100m)*totalUsers |> int
        let liveUsersList = getList [1..usernames.Count] liveUsers
        let lis : List<string> = liveUsersList |> List.map (fun i -> i.ToString())
        activeUsers <- Set.ofSeq lis
            
        let tweetCount = string tweetCounter
            //adding in tweets map
        tweets <- tweets.Add(tweetCount, tweet)

            //traverse followers of user and send tweet to their newsfeed
        let followersSet = userFollowers.TryFind(string myId).Value
        Set.iter (fun id -> if activeUsers.Contains(id) then
                                    clientSelection(int id) <! NewsFeedTweet(tweet)
                                else
                                    clientSelection(int id) <! InActiveNewsFeedTweet(tweet)) followersSet

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
        let mutable tweetsSet = userTweets.TryFind(string myId).Value
        tweetsSet <- tweetsSet.Add(tweetCount)
        userTweets <- userTweets.Add(string myId, tweetsSet)

        tweetCounter <- tweetCounter + 1
        if isRetweet then
            simulatorActor <! ReTweetAck
        else
            simulatorActor <! TweetAck

    let rec loop()  = actor {
        let! message = mailbox.Receive()
        match message with
        | RegisterActor(id, username) ->
            registerUser id username
            //serverAuth <! message
        
        | LoginActor(id, liveUsersPercentage) ->
            activeUsersPercentage <- liveUsersPercentage
            loginUser id
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
        
        | QueryActor(id) ->
            let mentionsSet = mentions.TryFind("user" + string id).Value
            let res: List<string> = mentionsSet |> Set.toList |> List.map (fun i -> tweets.TryFind(i).Value)
            clientSelection(id) <! QueryResult(res)

        | _ -> failwith "Unknown message receiveddd!"
        return! loop()
    }
    loop()

