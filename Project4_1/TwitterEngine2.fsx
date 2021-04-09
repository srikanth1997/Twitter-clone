#time "on"
#r "nuget: Akka.FSharp"
#r "nuget: Akka.TestKit"

open System
open System.IO
open Akka.Actor
open Akka.FSharp
open System.Collections.Generic
open System.Collections.Concurrent

open System.Text.RegularExpressions

let system = ActorSystem.Create("Twitter")


let rand = System.Random()
type ReturnOfReceive =
    | CreateUser of string
    | EngineLogin of string
    | EngineLogout of string
    | AddFollower of string * string
    | EngineTweet of string * string
    | QueryMentions of string
    | QueryHashtags of string
    | EngineRetweet of string

    | RegisterAllUsers
    | RegisterAck
    | LoginAndFollow
    | TweetsAndRetweets
    | Quering
    | LogoutRest
    | LogoutAck

    | Register of string
    | Login of string
    | Logout of string
    | Follow of string * string
    | Tweet of string * string
    | GetMentionTweets
    | GetHashtagTweets of string
    | Delete of string
    | Retweet of string
    | Tweeted of string * string
    | ReTweeted of string * string

let (|FirstRegexGroup|_|) pattern input =
    let m = Regex.Match(input,pattern) 
    if (m.Success) then Some m.Groups.[1].Value else None 


let userList = new Collections.Generic.List<string>()


// tweet id - username
let tweetUserMap = new ConcurrentDictionary<int, string>()

// tweet id - tweet
let tweetDictionary = new ConcurrentDictionary<int, string>()

// username - username
let followerDict =
    new ConcurrentDictionary<string, Collections.Generic.List<string>>()

// username - list of tweet ids
let timeline =
    new ConcurrentDictionary<string, Collections.Generic.List<int>>()

let hashtags =
    new ConcurrentDictionary<string, Collections.Generic.List<int>>()

let mentions =
    new ConcurrentDictionary<string, Collections.Generic.List<int>>()
// list of usernames
let activeUser = new Collections.Generic.List<string>()
let mutable tweetCounter = 0
let addHashtagsAndMentions (tweetId:int) =
    let tweetStr = tweetDictionary.Item(tweetId)
    let hashtagList = new List<string>()
    let mentionList = new List<string>()
    let testRegex s = 
        match s with
        | FirstRegexGroup ".*?#(.*)" ht ->
            hashtagList.Add(ht)
        | FirstRegexGroup ".*?@(.*)" m ->
            mentionList.Add(m)
        | _ ->  ()
    
    let words = tweetStr.Split ' '
    for word in words do
        testRegex word
        
    for htag in hashtagList do
        if hashtags.ContainsKey(htag) then
            let tweetList = hashtags.Item(htag)
            tweetList.Add(tweetId)
        else
            let tweetList = new List<int>()
            tweetList.Add(tweetId)
            hashtags.TryAdd(htag,tweetList) |>ignore
    
    for mtn in mentionList do
        if mentions.ContainsKey(mtn) then
            let tweetList = mentions.Item(mtn)
            tweetList.Add(tweetId)
        else
            let tweetList = new List<int>()
            tweetList.Add(tweetId)
            mentions.TryAdd(mtn,tweetList) |>ignore

let basePath = "akka://Twitter/user/"
let TwitterEngine2 (mailbox: Actor<_>) =
    let simref = select @"akka://Twitter/user/Simulator" system


    let rec engineLoop () =
        actor {
            
            // let mutable logoutCount = 0
            let self = mailbox.Self
            let! returnOfReceive = mailbox.Receive()

            match returnOfReceive with
            
            | CreateUser (username: string) ->
                
                if (userList.Contains(username)) then
                    File.AppendAllText("output.txt", string(username) + " already exists. Please try a different name\n" )
                    

                else
                    userList.Add(username)
                    timeline.TryAdd(username, new List<int>()) |>ignore
                    followerDict.TryAdd(username, new List<string>()) |>ignore
                    File.AppendAllText("output.txt", string(username) + " is created\n")
                    
                   
                    simref <! RegisterAck

            | AddFollower (username: string, personToFollow: string) ->
                // get the folowers list of user
                let followers = followerDict.Item(personToFollow)
                if not(followers.Contains(username)) then
                    followers.Add(username)
                    
                    File.AppendAllText("output.txt", string(username) + " is following " + string(personToFollow) + "\n")
                    


            | EngineLogin (name: string) ->
                if (not (activeUser.Contains(name))) then 
                    activeUser.Add(name)
                    File.AppendAllText("output.txt", "User:" + string(name) + " has logged in\n")
                

            | EngineLogout (username: string) -> 
                if (activeUser.Contains(username)) then 
                    if activeUser.Remove(username) then
                        File.AppendAllText("output.txt", "User:" + string(username) + " has logged out\n")
                        simref <! LogoutAck
                        
            | QueryMentions (queryString) ->
                if(mentions.ContainsKey(queryString)) then
                    let tweetIds = mentions.Item(queryString)
                    File.AppendAllText("output.txt", "List of tweets with mention " + string(queryString) + "\n")
                    
                    for t in tweetIds do
                        File.AppendAllText("output.txt", tweetDictionary.Item(t) + "\n")
                        
            | QueryHashtags (queryString) ->
                // get all hashtags
                
                if(hashtags.ContainsKey(queryString)) then
                    
                    let tweetIds = hashtags.Item(queryString)
                    File.AppendAllText("output.txt", "List of tweets with hashtag " + string(queryString) + "\n")

                    for t in tweetIds do
                        File.AppendAllText("output.txt", tweetDictionary.Item(t) + "\n")
                        

            | EngineTweet (tweet: string, username: string) ->
                // get an unique tweet id which is monotonically increasing and positive
                if activeUser.Contains(username) then
                    let tweetId = tweetCounter + 1
                    tweetCounter <- tweetCounter+1
                    
                    tweetDictionary.TryAdd(tweetId, tweet) |> ignore
                    tweetUserMap.TryAdd(tweetId, username) |> ignore

                    let tempTimelineList = timeline.Item(username)
                    tempTimelineList.Add(tweetId)

                    for name in followerDict.Item(username) do
                        let templist = timeline.Item(name)
                        templist.Add(tweetId)
                        if activeUser.Contains(name) then
                            system.ActorSelection(String.Concat("/user/",name))
                            <!Tweeted (username, tweet)

                    addHashtagsAndMentions(tweetId)
                    system.ActorSelection(String.Concat("/user/",username))
                     <!Tweeted (username, tweet)
                else 
                    File.AppendAllText("output.txt", string(username) + " is not logged in! " + "\n")
                    
            | EngineRetweet(username) ->
                if activeUser.Contains(username) then
                    let tweetsOnTimeline = timeline.Item(username)
                    let randTweetId = tweetsOnTimeline.Item(rand.Next(tweetsOnTimeline.Count))
                    let tweet = tweetDictionary.Item(randTweetId)
                    File.AppendAllText("output.txt", string(username) + " retweeted " + tweet + "\n")
                    
                    for name in followerDict.Item(username) do
                        let templist = timeline.Item(name)
                        templist.Add(randTweetId)
                        if activeUser.Contains(name) then
                            system.ActorSelection(String.Concat("/user/",name))
                            <!ReTweeted (username, tweet)
                    
                    system.ActorSelection(String.Concat("/user/",username))
                            <!ReTweeted (username, tweet)
                else
                    File.AppendAllText("output.txt", string(username) + " is not logged in!" + "\n")
                    
            | _ -> failwith "failed from engine"

            return! engineLoop ()
        }

    engineLoop ()


