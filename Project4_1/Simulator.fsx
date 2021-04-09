#load "TwitterEngine2.fsx"
#load "Client.fsx"

#time "on"
#r "nuget: Akka.FSharp"
#r "nuget: Akka.TestKit"
#r "nuget: MathNet.Numerics"


open TwitterEngine2
open Client
open System
open System.IO
open Akka.Actor
open Akka.FSharp
open Akka.TestKit
open System.Linq
open System.Collections.Generic
open System.Collections.Concurrent
open MathNet.Numerics.Random
open MathNet.Numerics.Distributions

let numUsers = int fsi.CommandLineArgs.[1]
// let numUsers = 50

let engineRef =
    spawn system "TwitterEngine2" TwitterEngine2

let usernamesList = new List<string>()


for i in 0 .. numUsers do
    usernamesList.Add(String.Concat("name", i))

let readHashtags = File.ReadAllLines("Hashtags.txt")
let readWords = File.ReadAllLines("words.txt")

let userDict = new Dictionary<string, IActorRef>()
for id in 0 .. (numUsers - 1) do
    let name = String.Concat("name", id)
    userDict.Add((name), spawn system (name) (Client name engineRef))

let userFollowerCount = Array.create numUsers 0
let zipfArray = Zipf(1.0, 9) 

zipfArray.Samples(userFollowerCount)

let randNum = System.Random()


let randomTweetGen () = 
    let mutable t:string = " "
    
    for i = 0 to 6 do
        t <- String.Concat(t," ",readWords.ToArray().[randNum.Next(readWords.Count())])
    let numHashtags = rand.Next(5)
    let hashtags = new List<string>()
    for i = 0 to numHashtags - 1 do
        let hashtag = readHashtags.ToArray().[randNum.Next(readHashtags.Count())]
        if not(hashtags.Contains(hashtag)) then hashtags.Add(hashtag)
    for h in hashtags do
        t <- String.Concat(t," #",h)
    let numMentions = rand.Next(5)
    let mentions = new List<string>()
    for i = 0 to numMentions - 1 do
        let mention = usernamesList.Item(randNum.Next(usernamesList.Count))
        if not(mentions.Contains(mention)) then mentions.Add(mention)
    
    let tempList = new List<int>()
    while tempList.Count < 4 do
        tempList.Add(rand.Next(10))
    for m in mentions do
        t <- String.Concat(t," @",m)
    t


let someHashTag() = 
    let hashtag = readHashtags.ToArray().[randNum.Next(readHashtags.Count())]
    hashtag

File.WriteAllText("output.txt", "")

let mutable endFlag = false

let mutable regtimer = Diagnostics.Stopwatch.StartNew()
let Simulator (mailbox: Actor<_>) =
    
    let mutable registerCounter = 0
    let mutable logoutCounter = 0
    let self = select @"akka://Twitter/user/Simulator" system



    let rec simulatorLoop () =
        actor {
            let! returnOfReceive = mailbox.Receive()
            

            match returnOfReceive with
            | RegisterAllUsers ->
                
                for user in userDict.Keys do
                    userDict.Item(user) <! Register(user)
                
                
            | RegisterAck ->
                registerCounter <- registerCounter + 1
                if registerCounter = numUsers then
                    printfn "All users are registered"
                    regtimer.Stop()
                    printfn "Program took %f milliseconds to register %d users" regtimer.Elapsed.TotalMilliseconds numUsers
                    
                    regtimer.Start()
                    self <! LoginAndFollow
            | LoginAndFollow -> 
            
                for i in 0..(userFollowerCount.Length-1) do
                    
                    let tempList = new List<int>()
                    while tempList.Count < userFollowerCount.[i]  do
                        let num = rand.Next(numUsers) 
                        if num <> i then
                            tempList.Add(rand.Next(numUsers))
                    for j in tempList do
                        userDict.Item("name" + string(j)) <! Login("name" + string(j)) 
                        userDict.Item("name" + string(j)) <! Follow("name" + string(j), "name" + string(i))
                self <! TweetsAndRetweets
            | TweetsAndRetweets ->
                for index in 0..(userFollowerCount.Length-1) do
                    let numTweets = userFollowerCount.[index] * 10
                    for i in 0..(numTweets-1) do
                        let randtweet = randomTweetGen()
                        userDict.Item("name" + string(index)) <! Tweet("name" + string(index), randtweet)
                    let numReTweets = userFollowerCount.[index] * 2
                    for i in 0..(numReTweets-1) do
                        let randtweet = randomTweetGen()
                        userDict.Item("name" + string(index)) <! Retweet("name" + string(index))
                self <! Quering
            | Quering ->
                for index in 0..(userFollowerCount.Length-1) do
                    let mutable randIndex = rand.Next(numUsers-1)
                    let randBool = rand.NextBoolean()
                    if randBool then
                        userDict.Item("name" + string(randIndex)) <! GetMentionTweets
                        userDict.Item("name" + string(randIndex)) <! GetHashtagTweets(someHashTag())

                    randIndex <- rand.Next(numUsers-1)
                    userDict.Item("name" + string(randIndex)) <! Logout("name" + string(randIndex))
                self <! LogoutRest
            | LogoutRest ->           
                // regtimer.Stop()
                // printfn "Program took %f milliseconds to perform operations " regtimer.Elapsed.TotalMilliseconds
                let remainingUsers = activeUser.ToList()
                for user in remainingUsers do
                    userDict.Item(user) <! Logout(user)
            | LogoutAck ->
                if activeUser.Count = 0 then
                    endFlag <- true
                else
                    self <! LogoutRest
            | _ -> failwith "unkown from simulator"

            return! simulatorLoop ()


        }

    simulatorLoop ()

let timer = Diagnostics.Stopwatch.StartNew()

let simulatorRef = spawn system "Simulator" Simulator

simulatorRef <! RegisterAllUsers

while not endFlag do
    ignore()

timer.Stop()
printfn "Program took %f milliseconds to make %d tweets by %d users" timer.Elapsed.TotalMilliseconds tweetCounter numUsers