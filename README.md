# my-ai
AI Code generator using deepseek chat. No need for cutting and pasting of code anymore from the LLM.
It makes use of the deepseek platform and API capability. It still needs a bit of fine tuning, but it is goed enough for my purpose to generate Razor Page dot net applications.
It sometimes work a 100% and sometimes it still needs help from the user, but it is much quicker that doing the web UI version and to cut and paste the code manually into the correct folders.

## The scope
I have only tested it with dotnet razor page application generation, but I am sure with a bit of finetuning it will be able to generate any code.

## How to get started
- Setup an account with platform.deepseek.com  (You need an API key to interface with their APIs).
- Clone this repo.
- Make sure you have all the required software installed on your PC to run and compile dotnet version 8 (and higher) application.
- This was developed on Linux, so I have not tested it Windows yet, but I think the issues you will encounter is OS paths. You might need to change that in the program to use "\" instead of "/".
- Replace the MYAPIKEY with your own. it is the first line in the Main method in Programs.cs.

## How to use
- Compile the code (after creating a directory my-ai and changing ito it). Use dotnet build.
- To run simply run dotnet run.
- For help just type help and enter. (However the first screen will give you all the available commands)
