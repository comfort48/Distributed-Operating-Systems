open Suave
open Suave.Http
open Suave.Operators
open Suave.Filters
open Suave.Successful
open Suave.Files
open Suave.RequestErrors
open Suave.Logging
open Suave.Utils

open System
open System.Net

open Suave.Sockets
open Suave.Sockets.Control
open Suave.WebSocket
open System.Threading

open Akka.FSharp
open Server
open Client

let mutable users : Map<String, WebSocket> = Map.empty 
let convertByte (text: string) =
     text |> System.Text.Encoding.ASCII.GetBytes |>ByteSegment

let mutable userSet: Set<String> = Set.empty
let mutable loggedinUsers: Set<String> = Set.empty

let ws (webSocket : WebSocket) (context: HttpContext) =
  socket {
    // if `loop` is set to false, the server will stop receiving messages
    let mutable loop = true

    while loop do
      // the server will wait for a message to be received without blocking the thread
      let! msg = webSocket.read()

      match msg with
      // the message has type (Opcode * byte [] * bool)
      //
      // Opcode type:
      //   type Opcode = Continuation | Text | Binary | Reserved | Close | Ping | Pong
      //
      // byte [] contains the actual message
      //
      // the last element is the FIN byte, explained later
      | (Text, data, true) ->
        // the message can be converted to a string
        let str = UTF8.toString data
        let response = sprintf "response to %s" str
        if str.Contains("register") then
            printfn "regis %s" str 
            let index = str.IndexOf("/") + 1
            let user = str.[index..]
            if users.ContainsKey(user) then
                sendResponse webSocket "User already exists"
            else 
                users <- users.Add(user, webSocket);

            // the `send` function sends a message back to the client
            //do! webSocket.send Text byteResponse true 

        elif str.Contains("retweet") then
            let a = str.IndexOf("/") + 1
            let b = str.LastIndexOf("/")
            let username = str.[a..b-1]
            let tweetId = str.[b+1..]
            reTweet username tweetId

        
        elif str.Contains("tweet") then
            printfn "tweet is %s" str
            let i = str.IndexOf("/") + 1
            let j = str.LastIndexOf("/")
            let myId = str.[i..j-1]
            let myTweet = str.[j+1..]
            tweet myId myTweet
            // for KeyValue(key, value) in users do 
            //      sendResponse value str |> ignore
            //      //do! value.send Text str1 true 
        
        elif str.Contains("follow") then
            //printfn "followe user: %s" str
            let o = str.IndexOf("/") + 1
            let p = str.LastIndexOf("/")
            let userToFollow = str.[o..p-1]
            let myUser = str.[p+1..]
            if users.ContainsKey(userToFollow) then
                follow myUser userToFollow 
            else
                let err = "Cannot follow user " + userToFollow + " as it does not exist!"
                sendResponse webSocket err

        elif str.Contains("query") then
            let c = str.IndexOf("/") + 1
            let d = str.LastIndexOf("/")
            let queryByUser = str.[c..d-1]
            let queryStr = str.[d+1..]
            query queryByUser queryStr

        elif str.Contains("logout") then
            printfn "in logout %s" str
            let e = str.IndexOf("/") + 1
            let f = str.LastIndexOf("/")
            let logoutUser = str.[e..f-1]
            loggedinUsers <- loggedinUsers.Remove(logoutUser)
            logout logoutUser
        // the response needs to be converted to a ByteSegment
        ////do! value.send Text str1 true 

      | (Close, _, _) ->
        let emptyResponse = [||] |> ByteSegment
        do! webSocket.send Close emptyResponse true

        // after sending a Close message, stop the loop
        loop <- true

      | _ -> ()
    }

/// An example of explictly fetching websocket errors and handling them in your codebase.
let wsWithErrorHandling (webSocket : WebSocket) (context: HttpContext) = 
   
   let exampleDisposableResource = { new IDisposable with member __.Dispose() = printfn "Resource needed by websocket connection disposed" }
   let websocketWorkflow = ws webSocket context
   
   async {
    let! successOrError = websocketWorkflow
    match successOrError with
    // Success case
    | Choice1Of2() -> ()
    // Error case
    | Choice2Of2(error) ->
        // Example error handling logic here
        printfn "Error: [%A]" error
        exampleDisposableResource.Dispose()
        
    return successOrError
   }

let selectWorker = 
  select @"akka://FSharp/user/workerActor" system 

let Worker (mailbox:Actor<_>) = 
        let rec loop() = actor {
            let! message = mailbox.Receive()
            match message with
              | Register(username, pwd) ->
                  register username pwd
                  //printfn "username is %s /n" username
              
              | Login(username, pwd) ->
                  login username pwd

              | RegAck(success, username) ->
                  let ws: WebSocket = users.TryFind(username).Value
                  if success then
                      let msg = "Registration successful for user: " + username
                      sendResponse ws msg
                  else 
                      sendResponse ws "Registration not successful"

              | LoginAck(success, username, newsFeedList) ->
                  let ws: WebSocket = users.TryFind(username).Value
                  if success then
                      loggedinUsers <- loggedinUsers.Add(username)
                      let msg = "Login successful for user: " + username
                      if newsFeedList.Length > 0 then
                          let resultStr = newsFeedList |> String.concat "|"
                          let finalMsg = msg + "/" + resultStr
                          sendResponse ws finalMsg
                      else
                          sendResponse ws msg
                  else 
                      sendResponse ws "Login not successful"
              
              | LogoutAck(id) ->
                  printfn "Logout done"

              | FollowAck(myId, targetId) ->
                  //printfn "user followers map is %A" userFollowers
                  let ws: WebSocket = users.TryFind(myId).Value
                  let followMsg = myId + " follows " + targetId + " now!"
                  sendResponse ws followMsg

              | ReTweetFail(myId) ->
                let ws: WebSocket = users.TryFind(myId).Value
                sendResponse ws "Re tweet not successful, Please use a valid tweet id"

              | _ -> failwith "Unknown message receiveddd!"
            return! loop()
        }
        loop()

let websocketUser name = 
  let user = "/websocket/" + name
  path user

let registerUser =
  request (fun r -> 
                    let username = match r.queryParam "username" with
                                    | Choice1Of2 username -> username
                                    | _ -> ""
                    let pwd = match r.queryParam "pwd" with
                                    | Choice1Of2 pwd -> pwd
                                    | _ -> ""
                    let userExists = userSet.Contains(username)
                    if not userExists then
                        userSet <- userSet.Add(username);
                        Thread.Sleep(500)
                        let userWs = users.TryFind(username).Value
                        spawn system ("client"+ username) (ClientActor username userWs) |> ignore
                        selectWorker <? Register(username, pwd) |> ignore
                    
                    
                    NO_CONTENT)
  

let loginUser = 
    request (fun r -> 
                    let username = match r.queryParam "usernameLogin" with
                                    | Choice1Of2 usernameLogin -> usernameLogin
                                    | _ -> ""
                    let pwd = match r.queryParam "pwdLogin" with
                                    | Choice1Of2 pwdLogin -> pwdLogin
                                    | _ -> ""
                    let userExists = userSet.Contains(username)
                    if userExists then
                      selectWorker <? Login(username, pwd) |> ignore
                    else
                      printfn "Please Register and then login"
                    
                    
                    NO_CONTENT)
  //printfn "username is %s"
  

let app : WebPart = 
  choose [
    //path "/websocket" >=> handShake ws
    pathScan "/websocket/%s" (fun id -> websocketUser id >=> handShake ws)
    path "/websocketWithSubprotocol" >=> handShakeWithSubprotocol (chooseSubprotocol "test") ws
    path "/websocketWithError" >=> handShake wsWithErrorHandling
    GET >=> choose [ 
      path "/register" >=> registerUser
      path "/login" >=> loginUser
      path "/" >=> file "index.html"; browseHome 
      ]
    POST >=> choose [ path "/" >=> NO_CONTENT ]
    NOT_FOUND "Found no handlers." ]

[<EntryPoint>]
let main _ =
  spawn system "server" serverMaster |> ignore
  spawn system "workerActor" Worker |> ignore
  startWebServer { defaultConfig with logger = Targets.create Verbose [||] } app
  0

//
// The FIN byte:
//
// A single message can be sent separated by fragments. The FIN byte indicates the final fragment. Fragments
//
// As an example, this is valid code, and will send only one message to the client:
//
// do! webSocket.send Text firstPart false
// do! webSocket.send Continuation secondPart false
// do! webSocket.send Continuation thirdPart true
//
// More information on the WebSocket protocol can be found at: https://tools.ietf.org/html/rfc6455#page-34
//