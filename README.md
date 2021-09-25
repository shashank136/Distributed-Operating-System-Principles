# Project 1: Bitcoin Miner
This project is a mimics the distributed implementation of bitcoin miner using **Actor Model** in a functional programming language F#.

## How to run?

### Run on one machine
In order to run the code on single machine execute `server.fsx` file in command line.<br>
`dotnet fsi server.fsx <k>`<br>
Here `k` is no. of trailing zeros we are looking for in the hash of a string.
### For Distributed Implementation
Run the `server.fsx` file on the host server machine and run `client.fsx` on remote client machine which want to join as remote workers using:<br>
`dotnet fsi client.fsx <IP Address of Host Server Machine>`

Example:<br>
server: `dotnet fsi server.fsx 4` <br>
client: `dotnet fsi client.fsx 192.168.249.1`


## Design

 - Both server can remote client can mine coins.
 - On each machine we create a actor with responsibility of Coordinator and multiple actors with responsibility of workers.
 - Coordinator is only responsible to initiate workers and assign job to them. On receiving success from any worker coordinator sends message to all other workers to stop working as result has been evaluated.
 - Each worker on initiation communicates with coordinator for job and convey success message when suitable string is found.
 - Each worker works on 1000 strings before asking for new work.
 - Remote client has a coordinator who communicates with the remote server for job request and conveys success message on finding suitable string.

## Observations

 1. **Work unit**: I observed that my system of actors work optimally when the work unit is 1000 string. This helped worker to compute hash of more string in less time without waiting for communication with coordinator. With work unit less then 1000 most of the time is spent in communication between coordinator and workers.
 2. **Result of program for input 4**: For input 4 my code generate following string as result:
 `"shashankk.hayyal;5xWBYHnqOnXU87T" "0000a0f49b1e9fc786689f09e6d658b1ed685112f3802ba2c9ecfd87837aa38c"`
![Resut screenshot](https://github.com/shashank136/Distributed-Operating-System-Principles/blob/main/result_for_input_4.png)
 3. **Running Time**: For the input of 4, **CPU time** reported is **00:00:02.437** and **Real time** reported is **00:00:00.839**. The ratio of CPU time to real time is approx. 2.437/0.839 ~ 2.9.
 4. **Coins with maximum zeros mined**: I am able to mine coins with max. 8 zeros.
 5. I'm able to run my code on 3 machines and one of them being the host server machine.
