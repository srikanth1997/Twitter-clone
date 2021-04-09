#time "on"
#r "nuget: Akka.FSharp"
#r "nuget: Akka.TestKit"

#load "TwitterEngine2.fsx"

open TwitterEngine2
open System
open Akka.Actor
open Akka.FSharp
open Akka.TestKit
open System.Collections.Generic
open System.IO


let Client (name:string) (engine: IActorRef) (mailbox: Actor<_>) =
    
    let rec nodeLoop () =
        actor {
            
            let! returnOfReceive = mailbox.Receive()

            match returnOfReceive with
            | Register (name) ->
                engine <! CreateUser(name)
            | Logout (name) -> engine <! EngineLogout(name)
            | Login (name) -> engine <! EngineLogin(name)
            | Retweet(username) ->
                engine <! EngineRetweet(username)
            | ReTweeted (username, tweet) -> 
                File.AppendAllText("output.txt", "timeline of "+name+": "+string(username) + " retweeted " + tweet + "\n")
            | Tweet (name, message) -> engine <! EngineTweet(message, name)
            | Tweeted (username, tweet) -> 
                File.AppendAllText("output.txt", "timeline of "+name+": "+string(username) + " tweeted " + tweet + "\n")
            | GetHashtagTweets (queryString) -> engine <! QueryHashtags(queryString)
            | GetMentionTweets -> engine <! QueryMentions(name)
            
            | Follow (name, personToFollow) ->
                if not(name.Equals(personToFollow)) then
                    engine <! AddFollower(name, personToFollow)
            | _ -> failwith "failure from client"

            return! nodeLoop ()


        }

    nodeLoop ()
